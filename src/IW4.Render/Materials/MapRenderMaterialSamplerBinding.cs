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
        (Texture is not null ||
         !string.IsNullOrWhiteSpace(ExternalResourceIdentity)) &&
        // Material samplers consume declaration-routed coordinates. Custom
        // cube/runtime samplers use shader-produced directions or coordinates
        // and therefore correctly carry no host UV route.
        (SamplerArgIndex < 0 || UvRoute is not null);

    public string ResourceBindingIdentity =>
        Texture?.BindingIdentity ??
        ExternalResourceIdentity ??
        "MISSING";

    public string ShaderResourceIdentity =>
        ExternalResourceIdentity ?? TextureName;
}
