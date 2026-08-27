namespace IW4.AssetExchange.SourceFormat.Image;

/// <summary>
/// A decoded two-dimensional image file. Mip levels are ordered from the
/// largest level to the smallest and contain RGBA8 pixels.
/// </summary>
public sealed class ImageFileDocument
{
    internal ImageFileDocument(
        IReadOnlyList<ImageSourceMipLevel> mipLevels,
        bool? usesSrgbReads)
    {
        MipLevels = Array.AsReadOnly(
            (mipLevels ?? throw new ArgumentNullException(nameof(mipLevels)))
            .ToArray());
        UsesSrgbReads = usesSrgbReads;
    }

    public IReadOnlyList<ImageSourceMipLevel> MipLevels { get; }

    /// <summary>
    /// Whether the file explicitly identifies sRGB sampling. Null means the
    /// container carries no color-space metadata.
    /// </summary>
    public bool? UsesSrgbReads { get; }
}
