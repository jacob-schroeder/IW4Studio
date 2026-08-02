using IW4.Assets.Assets.Image;
using IW4.Render.Textures;

namespace IW4.Render.Materials;

/// <summary>
/// Optional resource resolution supplied to the pure material-table planner.
/// The raw material row remains authoritative for hash, semantic, and sampler
/// identity even when this resolved resource disagrees.
/// </summary>
public sealed record MapRenderEditorMaterialTextureResolution(
    GfxImageAsset? Image,
    MapRenderTexture? Texture);
