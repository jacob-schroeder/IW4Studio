using IW4.Game.Database;

namespace IW4.Loaders.IO;

/// <summary>The loader-owned managed handle for a Sys_File descriptor.</summary>
public sealed class SysFileHandle : IDisposable
{
    private Stream? _handle;

    public SysFileHandle(Stream handle, int startOffset)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (!handle.CanRead)
            throw new ArgumentException("The SysFile handle must be readable.", nameof(handle));

        File = new SysFile(startOffset);
        _handle = handle;
    }

    public SysFile File { get; }

    public Stream Handle => Volatile.Read(ref _handle)
        ?? throw new ObjectDisposedException(nameof(SysFileHandle));

    public void Dispose() => Interlocked.Exchange(ref _handle, null)?.Dispose();
}
