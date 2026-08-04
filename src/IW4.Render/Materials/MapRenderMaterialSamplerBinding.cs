using System.Numerics;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;

using IW4.Render.Textures;

namespace IW4.Render.Materials;

public sealed record MapRenderMaterialSamplerBinding(
    int SamplerArgIndex,
    ushort SamplerDest,
    uint SamplerHash,
    byte TextureSemantic,
    string TextureName,
    MapRenderTexture? Texture,
    MapRenderUvRoute? UvRoute,
    MapRenderWorldRuntimeTextureIdentity? WorldRuntimeTextureIdentity = null,
    MapRenderEditorMaterialTextureRole EditorTextureRole =
        MapRenderEditorMaterialTextureRole.Unknown,
    int TextureTableOrdinal = -1,
    string? ExternalResourceIdentity = null)
{
    /// <summary>
    /// True when the texture is either carried by the map scene or owned by a
    /// host resource table under an immutable key. The latter lets UI packets
    /// share translated shader contracts without embedding image bytes.
    /// </summary>
    public bool IsOperationallyResolved =>
        UvRoute is not null &&
        (Texture is not null ||
         !string.IsNullOrWhiteSpace(ExternalResourceIdentity));

    public string ResourceBindingIdentity =>
        Texture?.BindingIdentity ??
        ExternalResourceIdentity ??
        "MISSING";

    public string ShaderResourceIdentity =>
        ExternalResourceIdentity ?? TextureName;
}
