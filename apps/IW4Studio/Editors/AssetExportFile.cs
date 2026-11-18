using Avalonia.Platform.Storage;

namespace IW4.Studio.Desktop.Editors;

internal static class AssetExportFile
{
    internal static async Task WriteAsync(
        IStorageFile destination,
        byte[] bytes)
    {
        string? localPath = destination.TryGetLocalPath();
        if (localPath is not null)
        {
            string directory = Path.GetDirectoryName(localPath) ??
                throw new IOException(
                    "The selected destination has no parent directory.");
            string temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(localPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllBytesAsync(temporaryPath, bytes);
                File.Move(temporaryPath, localPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            return;
        }

        await using Stream stream = await destination.OpenWriteAsync();
        if (!stream.CanSeek)
        {
            throw new NotSupportedException(
                "The selected storage destination does not support a safe replacement write.");
        }
        stream.SetLength(0);
        stream.Position = 0;
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }
}
