using IW4.Assets.Assets.Image;
using IW4.Render.Textures;

namespace IW4.Render.Materials;

/// <summary>
/// Optional authoritative resource resolution supplied to the pure
/// material-table planner. When a resolution is provided, a null image means
/// unresolved and does not fall back to the material row. The raw row remains
/// authoritative for hash, semantic, and sampler identity.
/// </summary>
public sealed record EditorMaterialTextureResolution(
    GfxImageAsset? Image,
    Texture? Texture);
