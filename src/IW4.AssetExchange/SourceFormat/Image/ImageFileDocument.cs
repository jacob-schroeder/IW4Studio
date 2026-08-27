namespace IW4.AssetExchange.SourceFormat.Image;

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
