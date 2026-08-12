using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using IW4.Assets.Assets.Image;
using IW4.FastFiles.Database;
using IW4.FastFiles.Database.Streaming;
using Microsoft.Win32.SafeHandles;

namespace IW4.FastFiles.Streaming.Images;

public sealed class GfxImageStreamResolver : IDisposable
{
    private const int PackageHeaderSize = DbHeader.UnsignedPrefixLength;
    private const int FullBlockSize = 0x10000;
    private const ushort BlockTerminator = 1;
    private const int PackageReadAttemptCount = 3;
    private const long MaxCachedPackageBlockBytes = 64L * 1024 * 1024;
    private const int MaxCachedPackageBlockCount = 2048;

    private readonly ImmutableArray<DbHeaderImageStreamEntry> _entriesByStreamIndex;
    private readonly string _packageDirectory;
    private readonly ConcurrentDictionary<uint, string> _packagePaths = [];
    private readonly ConcurrentDictionary<
        (DbHeaderImageStreamEntry Entry, int ByteCount),
        Lazy<PackagePayloadReadResult>> _payloadCache = [];
    private readonly ConcurrentDictionary<
        uint,
        Lazy<PackageOpenResult>> _packageReaders = [];
    private readonly ConcurrentDictionary<
        PackageBlockCacheKey,
        Lazy<PackageBlockReadResult>> _packageBlocks = [];
    private readonly object _packageBlockLruGate = new();
    private readonly Dictionary<PackageBlockCacheKey, PackageBlockLruEntry>
        _packageBlockLruEntries = [];
    private readonly LinkedList<PackageBlockCacheKey> _packageBlockLru = [];
    private readonly object _lifetimeGate = new();
    private long _cachedPackageBlockBytes;
    private int _activeReads;
    private bool _disposeStarted;
    private bool _disposed;

    public GfxImageStreamResolver(DbHeader header, string fastFilePath)
    {
        _entriesByStreamIndex = header.ImageStreamEntries;
        _packageDirectory = Path.GetDirectoryName(Path.GetFullPath(fastFilePath)) ?? Environment.CurrentDirectory;
    }

    public bool TryReadBestPayload(
        GfxImageAsset image,
        out byte[] payload,
        out int width,
        out int height,
        out string reason)
    {
        EnterRead();
        try
        {
            payload = [];
            width = 0;
            height = 0;
            reason = string.Empty;

            if (image.StreamImageIndex is not { } imageIndex)
            {
                reason = "image has no PS3 stream index";
                return false;
            }

            // Initial/diagnostic rendering only needs the highest-resolution
            // payload. Do not inflate every lower authored mip merely to discard
            // it; strict resources use TryReadMipPayloads and preserve the full
            // chain. OpenGL generates missing mipmaps from this top level.
            string? lastReason = null;
            foreach (var candidate in image.StreamData
                         .Select((streamData, partIndex) => new { streamData, partIndex })
                         .Where(x => x.streamData.Width > 0 && x.streamData.Height > 0 && x.streamData.CumulativeByteCount != 0)
                         .OrderByDescending(x => x.streamData.Width * x.streamData.Height))
            {
                GfxImageStreamData streamData = candidate.streamData;
                int previousByteCount = candidate.partIndex == 0
                    ? 0
                    : image.StreamData[candidate.partIndex - 1].CumulativeByteCount;
                int byteCount = checked(streamData.CumulativeByteCount - previousByteCount);
                if (byteCount <= 0)
                {
                    lastReason = $"stream part {candidate.partIndex} byte count is zero";
                    continue;
                }

                int streamEntryIndex = checked(imageIndex * GfxImageStreamData.EntryCount + candidate.partIndex);
                if (!TryGetEntry(image, candidate.partIndex, streamEntryIndex, out DbHeaderImageStreamEntry entry, out reason))
                {
                    lastReason = reason;
                    continue;
                }

                if (!TryReadPackagePayload(entry, byteCount, out payload, out reason))
                {
                    lastReason = reason;
                    continue;
                }

                width = streamData.Width;
                height = streamData.Height;
                return true;
            }

            reason = lastReason ?? "no stream data";
            return false;
        }
        finally
        {
            ExitRead();
        }
    }

    public bool TryReadMipPayloads(
        GfxImageAsset image,
        out IReadOnlyList<GfxImageStreamMipPayload> mips,
        out string reason)
    {
        EnterRead();
        try
        {
            mips = [];
            reason = string.Empty;

            if (image.StreamImageIndex is not { } imageIndex)
            {
                reason = "image has no PS3 stream index";
                return false;
            }

            var candidates = image.StreamData
                .Select((streamData, partIndex) => new { streamData, partIndex })
                .Where(x => x.streamData.Width > 0 && x.streamData.Height > 0 && x.streamData.CumulativeByteCount != 0)
                .OrderByDescending(x => x.streamData.Width * x.streamData.Height);

            string? lastReason = null;
            var resolvedMips = new List<GfxImageStreamMipPayload>();
            foreach (var candidate in candidates)
            {
                GfxImageStreamData streamData = candidate.streamData;
                int previousByteCount = candidate.partIndex == 0
                    ? 0
                    : image.StreamData[candidate.partIndex - 1].CumulativeByteCount;
                int byteCount = checked(streamData.CumulativeByteCount - previousByteCount);
                if (byteCount <= 0)
                {
                    lastReason = $"stream part {candidate.partIndex} byte count is zero";
                    if (resolvedMips.Count > 0)
                        break;
                    continue;
                }

                if (resolvedMips.Count > 0)
                {
                    GfxImageStreamMipPayload previous = resolvedMips[^1];
                    int expectedWidth = Math.Max(1, previous.Width / 2);
                    int expectedHeight = Math.Max(1, previous.Height / 2);
                    if (streamData.Width != expectedWidth || streamData.Height != expectedHeight)
                    {
                        lastReason =
                            $"stream mip chain gap after {previous.Width}x{previous.Height}: " +
                            $"next part is {streamData.Width}x{streamData.Height}";
                        break;
                    }
                }

                int streamEntryIndex = checked(imageIndex * GfxImageStreamData.EntryCount + candidate.partIndex);
                if (!TryGetEntry(image, candidate.partIndex, streamEntryIndex, out DbHeaderImageStreamEntry entry, out reason))
                {
                    lastReason = reason;
                    if (resolvedMips.Count > 0)
                        break;
                    continue;
                }

                if (!TryReadPackagePayload(entry, byteCount, out byte[] payload, out reason))
                {
                    lastReason = reason;
                    if (resolvedMips.Count > 0)
                        break;
                    continue;
                }

                if (GfxImageStreamMipTailSplitter.TrySplit(
                        image,
                        streamData,
                        payload,
                        out IReadOnlyList<GfxImageStreamMipPayload> tailMips))
                {
                    resolvedMips.AddRange(tailMips);
                }
                else
                {
                    resolvedMips.Add(new GfxImageStreamMipPayload(
                        streamData.Width,
                        streamData.Height,
                        payload));
                }
            }

            if (resolvedMips.Count == 0)
            {
                reason = lastReason ?? "no stream data";
                return false;
            }

            mips = resolvedMips;
            return true;
        }
        finally
        {
            ExitRead();
        }
    }

    public void Dispose()
    {
        lock (_lifetimeGate)
        {
            if (_disposed)
                return;
            if (_disposeStarted)
            {
                while (!_disposed)
                    Monitor.Wait(_lifetimeGate);
                return;
            }

            _disposeStarted = true;
            while (_activeReads != 0)
                Monitor.Wait(_lifetimeGate);

            try
            {
                foreach (Lazy<PackageOpenResult> pending in _packageReaders.Values)
                {
                    if (!pending.IsValueCreated)
                        continue;

                    pending.Value.Reader?.Dispose();
                }

                _packageReaders.Clear();
                _packageBlocks.Clear();
                _payloadCache.Clear();
                _packagePaths.Clear();
                lock (_packageBlockLruGate)
                {
                    _packageBlockLruEntries.Clear();
                    _packageBlockLru.Clear();
                    _cachedPackageBlockBytes = 0;
                }
            }
            finally
            {
                _disposed = true;
                Monitor.PulseAll(_lifetimeGate);
            }
        }
    }

    private void EnterRead()
    {
        lock (_lifetimeGate)
        {
            if (_disposeStarted)
                throw new ObjectDisposedException(nameof(GfxImageStreamResolver));
            _activeReads++;
        }
    }

    private void ExitRead()
    {
        lock (_lifetimeGate)
        {
            _activeReads--;
            if (_activeReads == 0)
                Monitor.PulseAll(_lifetimeGate);
        }
    }

    private bool TryResolvePackagePath(
        uint fileIndex,
        out string packagePath,
        out string reason)
    {
        if (fileIndex == 0)
        {
            packagePath = string.Empty;
            reason =
                "image stream points at the current fastfile; image package resolver only handles imagefileN.pak";
            return false;
        }
        if (_packagePaths.TryGetValue(fileIndex, out string? resolvedPath))
        {
            packagePath = resolvedPath;
            reason = string.Empty;
            return true;
        }

        string packageFileName = $"imagefile{fileIndex}.pak";
        string adjacentPath = Path.Combine(
            _packageDirectory,
            packageFileName);
        string? parentDirectory = Path.GetDirectoryName(_packageDirectory);
        string? parentPath = parentDirectory is null
            ? null
            : Path.Combine(parentDirectory, packageFileName);
        string? availablePath = File.Exists(adjacentPath)
            ? adjacentPath
            : parentPath is not null && File.Exists(parentPath)
                ? parentPath
                : null;
        if (availablePath is null)
        {
            packagePath = string.Empty;
            reason = parentPath is null
                ? $"missing texture stream package {adjacentPath}"
                : $"missing texture stream package; checked {adjacentPath} and {parentPath}";
            return false;
        }

        packagePath = _packagePaths.GetOrAdd(fileIndex, availablePath);
        reason = string.Empty;
        return true;
    }

    private bool TryGetEntry(
        GfxImageAsset image,
        int partIndex,
        int streamIndex,
        out DbHeaderImageStreamEntry entry,
        out string reason)
    {
        if (image.StreamEntries.Count == GfxImageStreamData.EntryCount)
        {
            entry = image.StreamEntries[partIndex];
            if (entry.IsEmpty)
            {
                reason = $"stream index 0x{streamIndex:X} is empty";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        if (streamIndex >= _entriesByStreamIndex.Length)
        {
            entry = default;
            reason = $"stream index 0x{streamIndex:X} is outside the DB header table";
            return false;
        }

        entry = _entriesByStreamIndex[streamIndex];
        if (entry.IsEmpty)
        {
            reason = $"stream index 0x{streamIndex:X} is empty";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private bool TryReadPackagePayload(
        DbHeaderImageStreamEntry entry,
        int byteCount,
        out byte[] payload,
        out string reason)
    {
        var cacheKey = (entry, byteCount);
        Lazy<PackagePayloadReadResult> pending = _payloadCache.GetOrAdd(
            cacheKey,
            _ => new Lazy<PackagePayloadReadResult>(
                () => ReadPackagePayload(entry, byteCount),
                LazyThreadSafetyMode.ExecutionAndPublication));
        PackagePayloadReadResult result;
        try
        {
            result = pending.Value;
        }
        catch
        {
            RemoveExact(_payloadCache, cacheKey, pending);
            throw;
        }

        payload = result.Payload;
        reason = result.Reason;
        return result.Success;
    }

    private PackagePayloadReadResult ReadPackagePayload(
        DbHeaderImageStreamEntry entry,
        int byteCount)
    {
        if (!TryResolvePackagePath(
                entry.FileIndex,
                out string path,
                out string pathReason))
        {
            return PackagePayloadReadResult.Failed(
                pathReason);
        }

        for (int attempt = 1; attempt <= PackageReadAttemptCount; attempt++)
        {
            try
            {
                PackageOpenResult packageResult = GetPackageReader(
                    entry.FileIndex,
                    path);
                if (!packageResult.Success || packageResult.Reader is null)
                {
                    return PackagePayloadReadResult.Failed(
                        packageResult.Reason);
                }

                if (!TryReadPackagePayloadOnce(
                        packageResult.Reader,
                        entry,
                        byteCount,
                        out byte[] payload,
                        out string readReason))
                {
                    return PackagePayloadReadResult.Failed(readReason);
                }

                return PackagePayloadReadResult.Succeeded(payload);
            }
            catch (IOException exception)
            {
                if (attempt != PackageReadAttemptCount)
                    continue;

                return PackagePayloadReadResult.Failed(
                    $"I/O failed while reading {Path.GetFileName(path)} after " +
                    $"{PackageReadAttemptCount} attempts: {exception.Message}");
            }
        }

        throw new InvalidOperationException(
            "Package-read retry loop exited without a result.");
    }

    private PackageOpenResult GetPackageReader(uint fileIndex, string path)
    {
        if (!_packageReaders.TryGetValue(
                fileIndex,
                out Lazy<PackageOpenResult>? pending))
        {
            if (!File.Exists(path))
            {
                return PackageOpenResult.Failed(
                    $"missing texture stream package {path}");
            }

            pending = _packageReaders.GetOrAdd(
                fileIndex,
                _ => new Lazy<PackageOpenResult>(
                    () => OpenPackageReader(fileIndex, path),
                    LazyThreadSafetyMode.ExecutionAndPublication));
        }

        PackageOpenResult result;
        try
        {
            result = pending.Value;
        }
        catch
        {
            RemoveExact(_packageReaders, fileIndex, pending);
            throw;
        }

        // Exact payload failures remain cached, matching the prior behavior.
        // Do not retain a failed package-open result globally: another payload
        // must still be able to observe a package that was repaired or replaced.
        if (!result.Success)
            RemoveExact(_packageReaders, fileIndex, pending);
        return result;
    }

    private static PackageOpenResult OpenPackageReader(
        uint fileIndex,
        string path)
    {
        SafeFileHandle? handle = null;
        try
        {
            handle = File.OpenHandle(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                FileOptions.RandomAccess);
            long length = RandomAccess.GetLength(handle);
            if (!ValidatePackageHeader(
                    handle,
                    length,
                    path,
                    out string reason))
            {
                handle.Dispose();
                return PackageOpenResult.Failed(reason);
            }

            return PackageOpenResult.Succeeded(
                new PackageReader(fileIndex, path, handle, length));
        }
        catch
        {
            handle?.Dispose();
            throw;
        }
    }

    private bool TryReadPackagePayloadOnce(
        PackageReader package,
        DbHeaderImageStreamEntry entry,
        int byteCount,
        out byte[] payload,
        out string reason)
    {
        payload = [];
        reason = string.Empty;

        if (entry.SourceStart < PackageHeaderSize || entry.SourceStart >= package.Length)
        {
            reason =
                $"texture stream source start 0x{entry.SourceStart:X} is outside {package.FileName}";
            return false;
        }

        byte[] candidatePayload = new byte[byteCount];
        int written = 0;
        long position = entry.SourceStart;
        int skip = checked((int)entry.BlockOffset);
        while (written < byteCount)
        {
            if (entry.SourceEnd != 0 && position >= entry.SourceEnd)
            {
                reason =
                    $"texture stream entry range 0x{entry.SourceStart:X}-0x{entry.SourceEnd:X} ended before texture payload was complete";
                return false;
            }

            if (!TryReadNextBlock(
                    package,
                    position,
                    out byte[] block,
                    out long nextPosition,
                    out reason))
            {
                return false;
            }

            if (entry.SourceEnd != 0 && nextPosition > entry.SourceEnd)
            {
                reason =
                    $"texture stream package block crossed entry range 0x{entry.SourceStart:X}-0x{entry.SourceEnd:X}";
                return false;
            }
            position = nextPosition;

            if (skip >= block.Length)
            {
                skip -= block.Length;
                continue;
            }

            int remaining = byteCount - written;
            int take = Math.Min(remaining, block.Length - skip);
            block.AsSpan(skip, take).CopyTo(candidatePayload.AsSpan(written));
            written += take;
            skip = 0;
        }

        payload = candidatePayload;
        return true;
    }

    private static bool ValidatePackageHeader(
        SafeFileHandle handle,
        long length,
        string path,
        out string reason)
    {
        reason = string.Empty;
        if (length < PackageHeaderSize)
        {
            reason = $"{Path.GetFileName(path)} is too small for an image package header";
            return false;
        }

        Span<byte> header = stackalloc byte[PackageHeaderSize];
        if (!TryReadExactly(handle, 0, header))
        {
            reason =
                $"unexpected end of {Path.GetFileName(path)} while reading image package header";
            return false;
        }
        string magic = Encoding.Latin1.GetString(header[..DbHeader.MagicByteLength]);
        if (magic is not (DbHeader.UnsignedMagic or "S1ffu100"))
        {
            reason = $"{Path.GetFileName(path)} has unexpected package magic '{magic}'";
            return false;
        }
        uint version = BinaryPrimitives.ReadUInt32BigEndian(header[DbHeader.MagicByteLength..]);
        if (version != (uint)XFileVersion.ModernWarfare2)
        {
            reason =
                $"{Path.GetFileName(path)} has unsupported package version {version}; " +
                $"expected {(uint)XFileVersion.ModernWarfare2}";
            return false;
        }

        return true;
    }

    private bool TryReadNextBlock(
        PackageReader package,
        long blockOffset,
        out byte[] block,
        out long nextPosition,
        out string reason)
    {
        var cacheKey = new PackageBlockCacheKey(
            package.FileIndex,
            blockOffset);
        Lazy<PackageBlockReadResult> pending = _packageBlocks.GetOrAdd(
            cacheKey,
            _ => new Lazy<PackageBlockReadResult>(
                () => ReadNextBlock(package, blockOffset),
                LazyThreadSafetyMode.ExecutionAndPublication));
        PackageBlockReadResult result;
        try
        {
            result = pending.Value;
        }
        catch
        {
            RemoveExact(_packageBlocks, cacheKey, pending);
            throw;
        }

        // As with the old exact-payload cache, the requesting payload will cache
        // its failure. Keeping only successful blocks allows a different payload
        // to observe a subsequently repaired package.
        if (!result.Success)
            RemoveExact(_packageBlocks, cacheKey, pending);
        else
            TouchSuccessfulPackageBlock(
                cacheKey,
                pending,
                result.Block.Length);

        block = result.Block;
        nextPosition = result.NextPosition;
        reason = result.Reason;
        return result.Success;
    }

    private void TouchSuccessfulPackageBlock(
        PackageBlockCacheKey key,
        Lazy<PackageBlockReadResult> pending,
        int byteCount)
    {
        lock (_packageBlockLruGate)
        {
            if (!_packageBlocks.TryGetValue(
                    key,
                    out Lazy<PackageBlockReadResult>? current) ||
                !ReferenceEquals(current, pending))
            {
                return;
            }

            if (_packageBlockLruEntries.TryGetValue(
                    key,
                    out PackageBlockLruEntry? existing))
            {
                _packageBlockLru.Remove(existing.Node);
                _packageBlockLru.AddLast(existing.Node);
                return;
            }

            LinkedListNode<PackageBlockCacheKey> node =
                _packageBlockLru.AddLast(key);
            _packageBlockLruEntries.Add(
                key,
                new PackageBlockLruEntry(pending, node, byteCount));
            _cachedPackageBlockBytes = checked(
                _cachedPackageBlockBytes + byteCount);

            while ((_cachedPackageBlockBytes > MaxCachedPackageBlockBytes ||
                    _packageBlockLruEntries.Count >
                        MaxCachedPackageBlockCount) &&
                   _packageBlockLru.First is { } oldest)
            {
                PackageBlockCacheKey oldestKey = oldest.Value;
                PackageBlockLruEntry oldestEntry =
                    _packageBlockLruEntries[oldestKey];
                _packageBlockLru.RemoveFirst();
                _packageBlockLruEntries.Remove(oldestKey);
                _cachedPackageBlockBytes -= oldestEntry.ByteCount;
                RemoveExact(
                    _packageBlocks,
                    oldestKey,
                    oldestEntry.Pending);
            }
        }
    }

    private static PackageBlockReadResult ReadNextBlock(
        PackageReader package,
        long blockOffset)
    {
        Span<byte> sizeBytes = stackalloc byte[sizeof(ushort)];
        if (!TryReadExactly(package.Handle, blockOffset, sizeBytes))
        {
            return PackageBlockReadResult.Failed(
                $"unexpected end of {package.FileName} while reading package block size");
        }

        ushort encodedSize = BinaryPrimitives.ReadUInt16BigEndian(sizeBytes);
        if (encodedSize == BlockTerminator)
        {
            return PackageBlockReadResult.Failed(
                $"hit package block terminator in {package.FileName} before texture payload was complete");
        }

        int byteCount = encodedSize == 0 ? FullBlockSize : encodedSize;
        long dataOffset = checked(blockOffset + sizeof(ushort));
        long nextPosition = checked(dataOffset + byteCount);
        if (nextPosition > package.Length)
        {
            return PackageBlockReadResult.Failed(
                $"package block at 0x{blockOffset:X} in {package.FileName} extends past end of file");
        }

        byte[] bytes = new byte[byteCount];
        if (!TryReadExactly(package.Handle, dataOffset, bytes))
        {
            return PackageBlockReadResult.Failed(
                $"unexpected end of {package.FileName} while reading package block at " +
                $"0x{blockOffset:X}");
        }
        if (encodedSize == 0)
            return PackageBlockReadResult.Succeeded(bytes, nextPosition);

        try
        {
            return PackageBlockReadResult.Succeeded(
                InflateBlock(bytes),
                nextPosition);
        }
        catch (InvalidDataException ex)
        {
            return PackageBlockReadResult.Failed(
                $"failed to inflate package block at 0x{blockOffset:X} in {package.FileName}: {ex.Message}");
        }
    }

    private static byte[] InflateBlock(byte[] bytes)
    {
        using var input = new MemoryStream(bytes, writable: false);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        byte[] output = new byte[FullBlockSize];
        int length = 0;
        while (true)
        {
            if (length == output.Length)
            {
                int next = deflate.ReadByte();
                if (next < 0)
                    break;

                Array.Resize(ref output, checked(output.Length * 2));
                output[length++] = checked((byte)next);
                continue;
            }

            int read = deflate.Read(output.AsSpan(length));
            if (read == 0)
                break;
            length += read;
        }

        if (length != output.Length)
            Array.Resize(ref output, length);
        return output;
    }

    private static bool TryReadExactly(
        SafeFileHandle handle,
        long fileOffset,
        Span<byte> buffer)
    {
        while (buffer.Length > 0)
        {
            int read = RandomAccess.Read(handle, buffer, fileOffset);
            if (read == 0)
                return false;

            buffer = buffer[read..];
            fileOffset = checked(fileOffset + read);
        }

        return true;
    }

    private static void RemoveExact<TKey, TValue>(
        ConcurrentDictionary<TKey, TValue> cache,
        TKey key,
        TValue value)
        where TKey : notnull
    {
        ((ICollection<KeyValuePair<TKey, TValue>>)cache).Remove(
            new KeyValuePair<TKey, TValue>(key, value));
    }

    private readonly record struct PackageBlockCacheKey(
        uint FileIndex,
        long BlockOffset);

    private sealed record PackageBlockLruEntry(
        Lazy<PackageBlockReadResult> Pending,
        LinkedListNode<PackageBlockCacheKey> Node,
        int ByteCount);

    private readonly record struct PackagePayloadReadResult(
        bool Success,
        byte[] Payload,
        string Reason)
    {
        public static PackagePayloadReadResult Succeeded(byte[] payload) =>
            new(true, payload, string.Empty);

        public static PackagePayloadReadResult Failed(string reason) =>
            new(false, [], reason);
    }

    private readonly record struct PackageOpenResult(
        bool Success,
        PackageReader? Reader,
        string Reason)
    {
        public static PackageOpenResult Succeeded(PackageReader reader) =>
            new(true, reader, string.Empty);

        public static PackageOpenResult Failed(string reason) =>
            new(false, null, reason);
    }

    private readonly record struct PackageBlockReadResult(
        bool Success,
        byte[] Block,
        long NextPosition,
        string Reason)
    {
        public static PackageBlockReadResult Succeeded(
            byte[] block,
            long nextPosition) =>
            new(true, block, nextPosition, string.Empty);

        public static PackageBlockReadResult Failed(string reason) =>
            new(false, [], 0, reason);
    }

    private sealed class PackageReader : IDisposable
    {
        public PackageReader(
            uint fileIndex,
            string path,
            SafeFileHandle handle,
            long length)
        {
            FileIndex = fileIndex;
            Handle = handle;
            Length = length;
            FileName = System.IO.Path.GetFileName(path);
        }

        public string FileName { get; }

        public uint FileIndex { get; }

        public SafeFileHandle Handle { get; }

        public long Length { get; }

        public void Dispose() => Handle.Dispose();
    }
}
