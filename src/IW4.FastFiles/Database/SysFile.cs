namespace IW4.FastFiles.Database;

// Sys_File is represented as an OS handle followed by a 32-bit start offset.
// Stream is the managed handle; this model does not expose a native pointer.
public sealed class SysFile : IDisposable
{
    private Stream? _handle;

    public SysFile(Stream handle, int startOffset)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (!handle.CanRead)
            throw new ArgumentException("The SysFile handle must be readable.", nameof(handle));
        if (startOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(startOffset));

        _handle = handle;
        StartOffset = startOffset;
    }

    public bool IsOpen => Volatile.Read(ref _handle) is not null;

    public Stream Handle => Volatile.Read(ref _handle)
        ?? throw new ObjectDisposedException(nameof(SysFile));

    public int StartOffset { get; }

    public void Dispose()
    {
        Interlocked.Exchange(ref _handle, null)?.Dispose();
    }
}
