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
    int TextureTableOrdinal = -1);
