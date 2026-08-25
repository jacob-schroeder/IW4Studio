using Avalonia.Platform.Storage;
using IW4.Assets.D3dbsp;

namespace IW4.Studio.Desktop.Editors.D3dbsp;

internal static class D3dbspDesktopFileStorage
{
    public static FilePickerFileType FileType { get; } = new("IW4 D3DBSP files")
    {
        Patterns = ["*.d3dbsp", "*.D3DBSP"]
    };

    public static async Task<(string Path, string? TemporaryPath)>
        ResolveInputPathAsync(IStorageFile source)
    {
        ArgumentNullException.ThrowIfNull(source);
        string? localPath = source.TryGetLocalPath();
        if (localPath is not null)
            return (localPath, null);

        string temporaryPath = Path.Combine(
            Path.GetTempPath(),
            $"IW4Studio-{Guid.NewGuid():N}.d3dbsp");
        try
        {
            await using Stream input = await source.OpenReadAsync();
            await using var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await input.CopyToAsync(output);
            await output.FlushAsync();
            return (temporaryPath, temporaryPath);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    public static async Task WriteExportAsync(
        D3dbspFile file,
        IStorageFile destination)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(destination);
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
                await Task.Run(() => file.Write(temporaryPath));
                File.Move(temporaryPath, localPath, overwrite: true);
            }
            finally
            {
                TryDelete(temporaryPath);
            }
            return;
        }

        throw new NotSupportedException(
            "The selected non-local storage destination cannot guarantee an atomic replacement write.");
    }

    public static void TryDelete(string? path)
    {
        if (path is null)
            return;

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
