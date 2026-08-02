using IW4.FastFiles.Database;

namespace IW4.FastFiles.Loaders.IO;

public sealed class SysFileSystem
{
    public SysFile Sys_OpenFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        try
        {
            return new SysFile(stream, startOffset: 0);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public SysFile Sys_OpenFile(Stream handle, int startOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (!handle.CanRead)
            throw new ArgumentException("The system-file handle must be readable.", nameof(handle));
        if (startOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(startOffset));
        if (handle.CanSeek && startOffset > handle.Length)
            throw new ArgumentOutOfRangeException(nameof(startOffset), "The system-file start offset is beyond the handle length.");
        if (!handle.CanSeek && startOffset != 0)
        {
            throw new ArgumentException(
                "A non-seekable SysFile cannot represent a non-zero start offset.",
                nameof(handle));
        }

        if (handle.CanSeek)
            handle.Position = startOffset;

        return new SysFile(handle, startOffset);
    }

    public int Sys_Read(SysFile file, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(file);
        return file.Handle.Read(destination);
    }

    public byte[] Sys_Read(SysFile file, int byteCount)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (byteCount < 0)
            throw new ArgumentOutOfRangeException(nameof(byteCount));

        byte[] bytes = new byte[byteCount];
        file.Handle.ReadExactly(bytes);
        return bytes;
    }

    public byte[] Sys_ReadToEnd(SysFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        Stream handle = file.Handle;
        if (handle.CanSeek)
        {
            handle.Position = file.StartOffset;
            long byteCount = handle.Length - file.StartOffset;
            if (byteCount > int.MaxValue)
                throw new InvalidDataException("SysFile exceeds the managed 2 GiB source limit.");

            return Sys_Read(file, checked((int)byteCount));
        }

        using var output = new MemoryStream();
        handle.CopyTo(output);
        return output.ToArray();
    }

    public void Sys_CloseFile(SysFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        file.Dispose();
    }
}
