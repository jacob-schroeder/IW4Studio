using Avalonia.Media.Imaging;

namespace IW4.Studio.Desktop.Editors.RawFile;

/// <summary>
/// Routes RawFile names to small, native editor previews. Add a new extension
/// and preview constructor here without expanding the RawFile editor shell.
/// </summary>
public static class RawFileNativeViewerRegistry
{
    private static readonly IReadOnlyDictionary<string, RawFileNativeViewerKind>
        ViewersByExtension = new Dictionary<string, RawFileNativeViewerKind>(
            StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = RawFileNativeViewerKind.Png
        };

    public static bool TryGetViewer(
        string fileName,
        out RawFileNativeViewerKind viewerKind)
    {
        string extension = Path.GetExtension(fileName);
        return ViewersByExtension.TryGetValue(extension, out viewerKind);
    }

    /// <summary>
    /// Creates a detached preview only for a registered extension. Invalid
    /// content deliberately returns false so the editor retains its Hex view.
    /// </summary>
    public static bool TryCreatePreview(
        string fileName,
        ReadOnlySpan<byte> content,
        out RawFileNativeViewerKind viewerKind,
        out Bitmap? preview)
    {
        preview = null;
        if (!TryGetViewer(fileName, out viewerKind))
            return false;

        try
        {
            preview = viewerKind switch
            {
                RawFileNativeViewerKind.Png => CreateBitmap(content),
                _ => throw new ArgumentOutOfRangeException(nameof(viewerKind))
            };
            return true;
        }
        catch (Exception) when (viewerKind == RawFileNativeViewerKind.Png)
        {
            preview?.Dispose();
            preview = null;
            return false;
        }
    }

    private static Bitmap CreateBitmap(ReadOnlySpan<byte> content)
    {
        using var stream = new MemoryStream(content.ToArray(), writable: false);
        return new Bitmap(stream);
    }
}

public enum RawFileNativeViewerKind
{
    Png
}
