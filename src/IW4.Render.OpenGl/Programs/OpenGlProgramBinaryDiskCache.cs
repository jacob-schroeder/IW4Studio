using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl.Programs;

/// <summary>
/// Best-effort persistence for exact linked-program binaries. Entries are
/// qualified by the complete shader sources, link profile, OpenGL driver,
/// context profile, and supported binary formats. A rejected or malformed
/// entry is removed and treated as a normal source-link miss.
/// </summary>
internal sealed class OpenGlProgramBinaryDiskCache : IDisposable
{
    private const int SchemaVersion = 1;
    private const int MaximumShaderSourceBytes = 16 * 1024 * 1024;
    private const int MaximumIdentityBytes =
        (MaximumShaderSourceBytes * 2) + (1024 * 1024);
    private const int MaximumProgramBinaryBytes = 64 * 1024 * 1024;
    private const int ChecksumLength = 32;
    private const long MaximumCacheBytes = 512L * 1024 * 1024;
    private const long MaximumPendingWriteBytes = 64L * 1024 * 1024;
    private const string EntryExtension = ".glpb";
    private static readonly byte[] FileMagic = "IW4GLPB1"u8.ToArray();
    private static readonly TimeSpan AccessTimestampResolution =
        TimeSpan.FromDays(1);

    private readonly byte[] _driverIdentity = [];
    private readonly int _maximumEntryCount;
    private readonly string? _directory;
    private readonly object _writeGate = new();
    private readonly Channel<PendingWrite>? _pendingWrites;
    private readonly Task? _writeWorker;
    private int _knownEntryCount;
    private long _knownByteCount;
    private long _pendingWriteBytes;
    private bool _disposed;

    internal OpenGlProgramBinaryDiskCache(
        GL gl,
        string cacheRootDirectory,
        int maximumEntryCount)
    {
        ArgumentNullException.ThrowIfNull(gl);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRootDirectory);
        if (maximumEntryCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumEntryCount));

        _maximumEntryCount = maximumEntryCount;
        try
        {
            int majorVersion = gl.GetInteger(GetPName.MajorVersion);
            int minorVersion = gl.GetInteger(GetPName.MinorVersion);
            bool supportsProgramBinary =
                majorVersion > 4 ||
                (majorVersion == 4 && minorVersion >= 1) ||
                gl.IsExtensionPresent("GL_ARB_get_program_binary");
            int binaryFormatCount = supportsProgramBinary
                ? gl.GetInteger(GetPName.NumProgramBinaryFormats)
                : 0;
            if (binaryFormatCount <= 0 || binaryFormatCount > 1024)
                return;

            _driverIdentity = CreateDriverIdentity(
                gl,
                majorVersion,
                minorVersion,
                binaryFormatCount);
            string directory = Path.Combine(
                Path.GetFullPath(cacheRootDirectory),
                $"v{SchemaVersion}");
            Directory.CreateDirectory(directory);
            _directory = directory;
            RefreshBoundsAndTrim();
            _pendingWrites = Channel.CreateUnbounded<PendingWrite>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = false
                });
            _writeWorker = Task.Run(PersistPendingWritesAsync);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            // Program binaries are an optimization. The source linker remains
            // authoritative if capability discovery or cache setup fails.
        }
    }

    internal bool IsAvailable => _directory is not null;

    internal bool TryLoad(
        GL gl,
        OpenGlProgramKey key,
        string vertexGlsl,
        string pixelGlsl,
        out uint programHandle)
    {
        ArgumentNullException.ThrowIfNull(gl);
        programHandle = 0;
        if (_directory is null ||
            !TryCreateEntryIdentity(
                key,
                vertexGlsl,
                pixelGlsl,
                out byte[] identity))
        {
            return false;
        }

        string path = CreateEntryPath(identity);
        if (!File.Exists(path))
            return false;

        uint candidate = 0;
        bool invalidateEntry = false;
        try
        {
            long fileLength = new FileInfo(path).Length;
            long maximumFileLength =
                FileMagic.Length +
                sizeof(int) * 3L +
                sizeof(uint) +
                MaximumIdentityBytes +
                MaximumProgramBinaryBytes +
                ChecksumLength;
            if (fileLength <= 0 || fileLength > maximumFileLength)
            {
                invalidateEntry = true;
                return false;
            }

            byte[] entry = File.ReadAllBytes(path);
            if (!TryReadEntry(
                    entry,
                    identity,
                    out GLEnum binaryFormat,
                    out byte[] binary))
            {
                invalidateEntry = true;
                return false;
            }

            candidate = gl.CreateProgram();
            if (candidate == 0)
                return false;

            gl.ProgramBinary<byte>(candidate, binaryFormat, binary);
            gl.GetProgram(
                candidate,
                ProgramPropertyARB.LinkStatus,
                out int linkStatus);
            if (linkStatus == 0)
            {
                invalidateEntry = true;
                SafeDeleteProgram(gl, candidate);
                candidate = 0;
                return false;
            }

            programHandle = candidate;
            candidate = 0;
            TouchEntry(path);
            return true;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            invalidateEntry = candidate != 0;
            return false;
        }
        finally
        {
            if (candidate != 0)
                SafeDeleteProgram(gl, candidate);
            if (invalidateEntry)
                SafeDeleteFile(path);
        }
    }

    internal bool TryStore(
        GL gl,
        OpenGlProgramKey key,
        string vertexGlsl,
        string pixelGlsl,
        uint programHandle)
    {
        ArgumentNullException.ThrowIfNull(gl);
        if (_directory is null ||
            programHandle == 0 ||
            !TryCreateEntryIdentity(
                key,
                vertexGlsl,
                pixelGlsl,
                out byte[] identity))
        {
            return false;
        }

        try
        {
            gl.GetProgram(
                programHandle,
                ProgramPropertyARB.ProgramBinaryLength,
                out int binaryLength);
            if (binaryLength <= 0 ||
                binaryLength > MaximumProgramBinaryBytes)
            {
                return false;
            }

            var binary = new byte[binaryLength];
            gl.GetProgramBinary<byte>(
                programHandle,
                out uint writtenLength,
                out GLEnum binaryFormat,
                binary);
            if (writtenLength == 0 || writtenLength > binary.Length)
                return false;

            byte[] entry = CreateEntry(
                identity,
                binaryFormat,
                binary.AsSpan(0, checked((int)writtenLength)));
            return TryQueueWrite(
                new PendingWrite(
                    CreateEntryPath(identity),
                    entry));
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return false;
        }
    }

    private string CreateEntryPath(ReadOnlySpan<byte> identity)
    {
        string fileName =
            Convert.ToHexString(SHA256.HashData(identity)) +
            EntryExtension;
        return Path.Combine(_directory!, fileName);
    }

    private bool TryCreateEntryIdentity(
        OpenGlProgramKey key,
        string vertexGlsl,
        string pixelGlsl,
        out byte[] identity)
    {
        identity = [];
        ArgumentNullException.ThrowIfNull(vertexGlsl);
        ArgumentNullException.ThrowIfNull(pixelGlsl);
        if (!key.MatchesSources(
                vertexGlsl,
                pixelGlsl,
                key.LinkProfileIdentity))
        {
            return false;
        }

        int vertexByteCount = Encoding.UTF8.GetByteCount(vertexGlsl);
        int pixelByteCount = Encoding.UTF8.GetByteCount(pixelGlsl);
        if (vertexByteCount > MaximumShaderSourceBytes ||
            pixelByteCount > MaximumShaderSourceBytes)
        {
            return false;
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(
            stream,
            Encoding.UTF8,
            leaveOpen: true);
        writer.Write(_driverIdentity.Length);
        writer.Write(_driverIdentity);
        WriteText(writer, key.LinkProfileIdentity);
        WriteText(writer, vertexGlsl);
        WriteText(writer, pixelGlsl);
        writer.Flush();
        if (stream.Length > MaximumIdentityBytes)
            return false;
        identity = stream.ToArray();
        return true;
    }

    public void Dispose()
    {
        Channel<PendingWrite>? pendingWrites;
        Task? writeWorker;
        lock (_writeGate)
        {
            if (_disposed)
                return;
            _disposed = true;
            pendingWrites = _pendingWrites;
            writeWorker = _writeWorker;
            pendingWrites?.Writer.TryComplete();
        }

        if (writeWorker is null)
            return;
        try
        {
            writeWorker.GetAwaiter().GetResult();
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            // Persistence is best effort and never owns renderer validity.
        }
    }

    private bool TryQueueWrite(PendingWrite write)
    {
        Channel<PendingWrite>? pendingWrites = _pendingWrites;
        if (pendingWrites is null)
            return false;
        lock (_writeGate)
        {
            if (_disposed ||
                write.Entry.LongLength > MaximumPendingWriteBytes ||
                _pendingWriteBytes >
                    MaximumPendingWriteBytes - write.Entry.LongLength)
            {
                return false;
            }
            _pendingWriteBytes += write.Entry.LongLength;
            if (pendingWrites.Writer.TryWrite(write))
                return true;
            _pendingWriteBytes -= write.Entry.LongLength;
            return false;
        }
    }

    private async Task PersistPendingWritesAsync()
    {
        Channel<PendingWrite> pendingWrites = _pendingWrites!;
        await foreach (PendingWrite write in
                       pendingWrites.Reader.ReadAllAsync()
                           .ConfigureAwait(false))
        {
            try
            {
                PersistEntry(write);
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                // A missed disk entry only causes a later source-link miss.
            }
            finally
            {
                lock (_writeGate)
                {
                    _pendingWriteBytes = Math.Max(
                        0,
                        _pendingWriteBytes - write.Entry.LongLength);
                }
            }
        }
    }

    private void PersistEntry(PendingWrite write)
    {
        string temporaryPath = Path.Combine(
            _directory!,
            $".{Path.GetFileName(write.DestinationPath)}." +
            $"{Guid.NewGuid():N}.tmp");
        try
        {
            bool replacing = File.Exists(write.DestinationPath);
            long replacedLength = replacing
                ? TryGetFileLength(write.DestinationPath)
                : 0;
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                stream.Write(write.Entry);
            }
            File.Move(
                temporaryPath,
                write.DestinationPath,
                overwrite: true);

            if (!replacing)
                _knownEntryCount = checked(_knownEntryCount + 1);
            _knownByteCount = Math.Max(
                0,
                _knownByteCount - replacedLength + write.Entry.Length);
            if (_knownEntryCount > _maximumEntryCount ||
                _knownByteCount > MaximumCacheBytes)
            {
                RefreshBoundsAndTrim();
            }
        }
        finally
        {
            SafeDeleteFile(temporaryPath);
        }
    }

    private static unsafe byte[] CreateDriverIdentity(
        GL gl,
        int majorVersion,
        int minorVersion,
        int binaryFormatCount)
    {
        var binaryFormats = new int[binaryFormatCount];
        fixed (int* formats = binaryFormats)
            gl.GetInteger(GetPName.ProgramBinaryFormats, formats);
        Array.Sort(binaryFormats);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(
            stream,
            Encoding.UTF8,
            leaveOpen: true);
        writer.Write(SchemaVersion);
        WriteText(writer, gl.GetStringS(StringName.Vendor));
        WriteText(writer, gl.GetStringS(StringName.Renderer));
        WriteText(writer, gl.GetStringS(StringName.Version));
        WriteText(
            writer,
            gl.GetStringS(StringName.ShadingLanguageVersion));
        writer.Write(majorVersion);
        writer.Write(minorVersion);
        writer.Write(gl.GetInteger(GetPName.ContextProfileMask));
        writer.Write(gl.GetInteger(GetPName.ContextFlags));
        writer.Write(binaryFormats.Length);
        foreach (int binaryFormat in binaryFormats)
            writer.Write(binaryFormat);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] CreateEntry(
        byte[] identity,
        GLEnum binaryFormat,
        ReadOnlySpan<byte> binary)
    {
        using var stream = new MemoryStream(
            checked(
                FileMagic.Length +
                sizeof(int) * 3 +
                sizeof(uint) +
                identity.Length +
                binary.Length +
                ChecksumLength));
        using (var writer = new BinaryWriter(
                   stream,
                   Encoding.UTF8,
                   leaveOpen: true))
        {
            writer.Write(FileMagic);
            writer.Write(SchemaVersion);
            writer.Write(identity.Length);
            writer.Write(identity);
            writer.Write((uint)binaryFormat);
            writer.Write(binary.Length);
            writer.Write(binary);
            writer.Flush();
        }

        byte[] content = stream.ToArray();
        byte[] checksum = SHA256.HashData(content);
        var entry = new byte[checked(content.Length + checksum.Length)];
        content.CopyTo(entry, 0);
        checksum.CopyTo(entry, content.Length);
        return entry;
    }

    private static bool TryReadEntry(
        byte[] entry,
        ReadOnlySpan<byte> expectedIdentity,
        out GLEnum binaryFormat,
        out byte[] binary)
    {
        binaryFormat = default;
        binary = [];
        if (entry.Length <= ChecksumLength)
            return false;

        int contentLength = entry.Length - ChecksumLength;
        byte[] actualChecksum = SHA256.HashData(
            entry.AsSpan(0, contentLength));
        if (!CryptographicOperations.FixedTimeEquals(
                actualChecksum,
                entry.AsSpan(contentLength, ChecksumLength)))
        {
            return false;
        }

        try
        {
            using var stream = new MemoryStream(
                entry,
                0,
                contentLength,
                writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            if (!reader.ReadBytes(FileMagic.Length).AsSpan()
                    .SequenceEqual(FileMagic) ||
                reader.ReadInt32() != SchemaVersion)
            {
                return false;
            }

            int identityLength = reader.ReadInt32();
            if (identityLength < 0 ||
                identityLength > MaximumIdentityBytes ||
                identityLength != expectedIdentity.Length)
            {
                return false;
            }
            byte[] identity = reader.ReadBytes(identityLength);
            if (identity.Length != identityLength ||
                !identity.AsSpan().SequenceEqual(expectedIdentity))
            {
                return false;
            }

            binaryFormat = (GLEnum)reader.ReadUInt32();
            int binaryLength = reader.ReadInt32();
            if (binaryLength <= 0 ||
                binaryLength > MaximumProgramBinaryBytes)
            {
                return false;
            }
            binary = reader.ReadBytes(binaryLength);
            return binary.Length == binaryLength &&
                   stream.Position == contentLength;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            binaryFormat = default;
            binary = [];
            return false;
        }
    }

    private void RefreshBoundsAndTrim()
    {
        if (_directory is null)
            return;

        try
        {
            var directory = new DirectoryInfo(_directory);
            DateTime staleTemporaryCutoff =
                DateTime.UtcNow - TimeSpan.FromDays(1);
            foreach (FileInfo temporary in directory
                         .EnumerateFiles("*.tmp"))
            {
                if (temporary.LastWriteTimeUtc >= staleTemporaryCutoff)
                    continue;
                try
                {
                    temporary.Delete();
                }
                catch (Exception exception) when (IsRecoverable(exception))
                {
                    // A concurrent process may still own the temporary file.
                }
            }

            FileInfo[] entries = directory
                .EnumerateFiles($"*{EntryExtension}")
                .OrderBy(entry => entry.LastWriteTimeUtc)
                .ToArray();
            long totalBytes = entries.Sum(
                entry => Math.Max(0, entry.Length));
            _knownEntryCount = entries.Length;
            _knownByteCount = totalBytes;
            if (entries.Length <= _maximumEntryCount &&
                totalBytes <= MaximumCacheBytes)
            {
                return;
            }

            int targetCount = Math.Max(
                0,
                _maximumEntryCount -
                Math.Max(1, _maximumEntryCount / 8));
            long targetBytes = MaximumCacheBytes * 7 / 8;
            foreach (FileInfo entry in entries)
            {
                if (_knownEntryCount <= targetCount &&
                    _knownByteCount <= targetBytes)
                {
                    break;
                }

                long length = Math.Max(0, entry.Length);
                try
                {
                    entry.Delete();
                    _knownEntryCount--;
                    _knownByteCount = Math.Max(
                        0,
                        _knownByteCount - length);
                }
                catch (Exception exception) when (IsRecoverable(exception))
                {
                    // Another process may be loading or replacing the entry.
                }
            }
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            // Cache maintenance never blocks source compilation.
        }
    }

    private static void WriteText(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static void TouchEntry(string path)
    {
        try
        {
            DateTime now = DateTime.UtcNow;
            if (now - File.GetLastWriteTimeUtc(path) >=
                AccessTimestampResolution)
            {
                File.SetLastWriteTimeUtc(path, now);
            }
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            // Approximate LRU timestamps are sufficient.
        }
    }

    private static long TryGetFileLength(string path)
    {
        try
        {
            return Math.Max(0, new FileInfo(path).Length);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return 0;
        }
    }

    private static void SafeDeleteProgram(GL gl, uint program)
    {
        try
        {
            gl.DeleteProgram(program);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            // The active context remains the authority for resource cleanup.
        }
    }

    private static void SafeDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            // A concurrent process may still own the path.
        }
    }

    private static bool IsRecoverable(Exception exception) =>
        exception is IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException or
            ArgumentException or
            NotSupportedException or
            InvalidOperationException or
            EndOfStreamException or
            OverflowException;

    private readonly record struct PendingWrite(
        string DestinationPath,
        byte[] Entry);
}
