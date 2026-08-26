using System.Collections.Concurrent;
using IW4.Assets.Assets.Sound;
using Microsoft.Win32.SafeHandles;

namespace IW4.FastFiles.Streaming.Sound;

/// <summary>
/// Reads raw streamed-sound ranges from the packfile*.pak files associated
/// with one disk-backed fastfile.
/// </summary>
public sealed class StreamedSoundResolver : IDisposable
{
    private readonly StreamPackagePathResolver _packagePaths;
    private readonly ConcurrentDictionary<
        uint,
        Lazy<PackageReader>> _packageReaders = [];
    private readonly object _lifetimeGate = new();
    private int _activeReads;
    private bool _disposeStarted;
    private bool _disposed;

    public StreamedSoundResolver(string fastFilePath)
    {
        _packagePaths = new StreamPackagePathResolver(
            fastFilePath,
            "packfile",
            "sound stream package");
    }

    public bool TryReadPayload(
        StreamedSound sound,
        out byte[] payload,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(sound);
        EnterRead();
        try
        {
            payload = [];
            reason = string.Empty;

            if (sound.FileIndex == 0)
            {
                reason =
                    "sound stream points at an external file; packfile resolver only handles packfileN.pak";
                return false;
            }
            if (sound.Source is not StreamedSoundFileSource source)
            {
                reason = "sound stream has no packfile range";
                return false;
            }
            if (source.StreamFileOffset < 0)
            {
                reason =
                    $"sound stream offset {source.StreamFileOffset} is negative";
                return false;
            }
            if (source.StreamFileLength < 0)
            {
                reason =
                    $"sound stream length {source.StreamFileLength} is negative";
                return false;
            }
            if (source.StreamFileLength == 0)
            {
                reason = "sound stream length is zero";
                return false;
            }
            if (!_packagePaths.TryResolve(
                    sound.FileIndex,
                    out string packagePath,
                    out reason))
            {
                return false;
            }

            return TryReadPackageRange(
                sound.FileIndex,
                packagePath,
                source.StreamFileOffset,
                source.StreamFileLength,
                out payload,
                out reason);
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
                foreach (Lazy<PackageReader> pending in _packageReaders.Values)
                {
                    if (pending.IsValueCreated)
                        pending.Value.Dispose();
                }

                _packageReaders.Clear();
                _packagePaths.Clear();
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
                throw new ObjectDisposedException(nameof(StreamedSoundResolver));
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

    private bool TryReadPackageRange(
        uint fileIndex,
        string packagePath,
        int streamOffset,
        int streamLength,
        out byte[] payload,
        out string reason)
    {
        payload = [];
        reason = string.Empty;
        string fileName = Path.GetFileName(packagePath);

        try
        {
            PackageReader package = GetPackageReader(fileIndex, packagePath);
            long rangeEnd = checked((long)streamOffset + streamLength);
            if (streamOffset >= package.Length)
            {
                reason =
                    $"sound stream offset 0x{streamOffset:X} is outside {fileName}";
                return false;
            }
            if (rangeEnd > package.Length)
            {
                reason =
                    $"sound stream range 0x{streamOffset:X}-0x{rangeEnd:X} extends past end of {fileName}";
                return false;
            }

            byte[] candidate = new byte[streamLength];
            if (!TryReadExactly(package.Handle, streamOffset, candidate))
            {
                reason =
                    $"unexpected end of {fileName} while reading sound stream range 0x{streamOffset:X}-0x{rangeEnd:X}";
                return false;
            }

            payload = candidate;
            return true;
        }
        catch (FileNotFoundException)
        {
            reason = $"missing sound stream package {packagePath}";
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            reason = $"missing sound stream package {packagePath}";
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            reason = $"access denied while reading sound stream package {packagePath}";
            return false;
        }
        catch (IOException)
        {
            reason = $"I/O failed while reading sound stream package {packagePath}";
            return false;
        }
    }

    private PackageReader GetPackageReader(uint fileIndex, string path)
    {
        Lazy<PackageReader> pending = _packageReaders.GetOrAdd(
            fileIndex,
            _ => new Lazy<PackageReader>(
                () => OpenPackageReader(path),
                LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            return pending.Value;
        }
        catch
        {
            RemoveExact(_packageReaders, fileIndex, pending);
            throw;
        }
    }

    private static PackageReader OpenPackageReader(string path)
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
            return new PackageReader(
                handle,
                RandomAccess.GetLength(handle));
        }
        catch
        {
            handle?.Dispose();
            throw;
        }
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

    private sealed class PackageReader : IDisposable
    {
        public PackageReader(
            SafeFileHandle handle,
            long length)
        {
            Handle = handle;
            Length = length;
        }

        public SafeFileHandle Handle { get; }

        public long Length { get; }

        public void Dispose() => Handle.Dispose();
    }
}
