using IW4.Assets.Assets.Image;
using IW4.Render.Textures;

namespace IW4.Render.Materials;

/// <summary>
/// Immutable projection of one material texture-table row for EditorPreview.
/// It keeps raw identity and resolved resources in the same packet without
/// mutating the source material asset.
/// </summary>
public sealed record MapRenderEditorMaterialTextureBinding(
    int TextureTableOrdinal,
    MapRenderEditorMaterialTextureRole Role,
    uint NameHash,
    byte NameStart,
    byte NameEnd,
    byte TextureSemantic,
    byte SamplerState,
    GfxImageAsset? Image,
    MapRenderTexture? ResolvedTexture,
    MapRenderSamplerState DecodedSamplerState)
{
    public string Tag => string.Create(
        2,
        (NameStart, NameEnd),
        static (chars, bytes) =>
        {
            chars[0] = (char)bytes.NameStart;
            chars[1] = (char)bytes.NameEnd;
        });
}
