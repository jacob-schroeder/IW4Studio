namespace IW4.Formats.SourceFormat.Image;

/// <summary>
/// A decoded image file. Mip levels are ordered from the largest level to the
/// smallest and contain RGBA8 pixels. Cubemap levels concatenate their six
/// faces in DDS face order; volume levels concatenate their depth slices.
/// </summary>
public sealed class ImageFileDocument
{
    internal ImageFileDocument(
        IReadOnlyList<ImageSourceMipLevel> mipLevels,
        ImageFileShape shape,
        bool? usesSrgbReads)
    {
        MipLevels = Array.AsReadOnly(
            (mipLevels ?? throw new ArgumentNullException(nameof(mipLevels)))
            .ToArray());
        Shape = shape;
        UsesSrgbReads = usesSrgbReads;
    }

    /// <summary>Creates a two-dimensional source with one tightly packed RGBA8 mip.</summary>
    public static ImageFileDocument FromRgba(int width, int height, ReadOnlyMemory<byte> rgbaBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if ((long)width * height != rgbaBytes.Length / 4 || rgbaBytes.Length % 4 != 0)
            throw new ArgumentException("The RGBA8 buffer must match the image dimensions.", nameof(rgbaBytes));

        return new ImageFileDocument(
            [new ImageSourceMipLevel(width, height, 1, rgbaBytes)], ImageFileShape.TwoDimensional, usesSrgbReads: null);
    }

    public IReadOnlyList<ImageSourceMipLevel> MipLevels { get; }

    /// <summary>
    /// The dimensional shape represented by every mip level.
    /// </summary>
    public ImageFileShape Shape { get; }

    /// <summary>
    /// Whether the file explicitly identifies sRGB sampling. Null means the
    /// container carries no color-space metadata.
    /// </summary>
    public bool? UsesSrgbReads { get; }
}

/// <summary>
/// Dimensional shapes supported by the IW4 image exchange path.
/// </summary>
public enum ImageFileShape
{
    TwoDimensional,
    Cube,
    Volume
}
