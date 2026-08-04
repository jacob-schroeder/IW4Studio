using IW4.Assets.Assets.Image;

namespace IW4.Render.UI;

/// <summary>
/// Identifies the authority used for the selected image. Hosts with an active
/// asset pool should resolve the current canonical provider rather than retain
/// a material-row object from a shadowed zone.
/// </summary>
public enum UiMaterialPreviewImageAuthority
{
    None = 0,
    CanonicalProvider = 1,
    MaterialRowFallback = 2
}

/// <summary>
/// Result supplied by a host while resolving one material texture row. The
/// factories prevent an image from being marked both unresolved and
/// authoritative.
/// </summary>
public sealed record UiMaterialPreviewImageResolution
{
    private UiMaterialPreviewImageResolution(
        GfxImageAsset? image,
        UiMaterialPreviewImageAuthority authority,
        string? failure)
    {
        Image = image;
        Authority = authority;
        Failure = failure;
    }

    public GfxImageAsset? Image { get; }

    public UiMaterialPreviewImageAuthority Authority { get; }

    public string? Failure { get; }

    public static UiMaterialPreviewImageResolution Canonical(
        GfxImageAsset image) =>
        new(
            image ?? throw new ArgumentNullException(nameof(image)),
            UiMaterialPreviewImageAuthority.CanonicalProvider,
            null);

    public static UiMaterialPreviewImageResolution MaterialRowFallback(
        GfxImageAsset image) =>
        new(
            image ?? throw new ArgumentNullException(nameof(image)),
            UiMaterialPreviewImageAuthority.MaterialRowFallback,
            null);

    public static UiMaterialPreviewImageResolution Unavailable(
        string? failure = null) =>
        new(
            null,
            UiMaterialPreviewImageAuthority.None,
            string.IsNullOrWhiteSpace(failure) ? null : failure.Trim());
}
