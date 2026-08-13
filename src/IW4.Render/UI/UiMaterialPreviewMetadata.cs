using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;

namespace IW4.Render.UI;

public sealed record UiMaterialPreviewMaterialMetadata(
    string Name,
    MaterialGameFlags GameFlags,
    MaterialSortKey SortKey,
    MaterialStateFlags StateFlags,
    GfxCameraRegionType CameraRegion,
    string? TechniqueSetName);

/// <summary>
/// Preserves authored atlas bytes while exposing the effective full-texture
/// grid used when atlas animation is disabled.
/// </summary>
public readonly record struct UiMaterialPreviewAtlasMetadata(
    byte AuthoredRowCount,
    byte AuthoredColumnCount)
{
    public bool IsValid =>
        (AuthoredRowCount == 0) == (AuthoredColumnCount == 0);

    public bool IsEnabled =>
        AuthoredRowCount > 0 && AuthoredColumnCount > 0;

    public int EffectiveRowCount => IsEnabled ? AuthoredRowCount : 1;

    public int EffectiveColumnCount => IsEnabled ? AuthoredColumnCount : 1;

    public int EffectiveCellCount =>
        checked(EffectiveRowCount * EffectiveColumnCount);
}

public sealed record UiMaterialPreviewImageMetadata(
    string Name,
    int Width,
    int Height,
    int Depth,
    byte Format,
    byte LevelCount,
    MapType MapType,
    GfxImageDimension DimensionCount,
    byte MultiFaceControl);
