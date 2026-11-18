namespace IW4.Studio.Desktop.Editors.Image;

public sealed class ImageEditorMip
{
    internal ImageEditorMip(int level, int width, int height, byte[] rgbaBytes)
    {
        Level = level;
        Width = width;
        Height = height;
        RgbaBytes = rgbaBytes;
    }

    public int Level { get; }
    public int Width { get; }
    public int Height { get; }

    internal byte[] RgbaBytes { get; }
}
