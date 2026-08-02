using System.Numerics;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;

using IW4.Render.Textures;

namespace IW4.Render.Materials;

public sealed record MapRenderColorLayer(
    int LayerIndex,
    int SamplerArgIndex,
    ushort SamplerDest,
    uint SamplerHash,
    byte TextureSemantic,
    MapRenderTexture Texture,
    MapRenderUvRoute UvRoute,
    int BlendWeightComponent);
