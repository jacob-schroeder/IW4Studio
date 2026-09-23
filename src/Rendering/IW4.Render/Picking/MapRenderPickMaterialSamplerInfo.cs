using System.Numerics;
using IW4.Game.Assets.ColMap;
using IW4.Game.Assets.GfxMap;
using IW4.Game.Zone;
using IW4.Runtime.Database;

using IW4.Render.Materials;

namespace IW4.Render.Picking;

public sealed record MapRenderPickMaterialSamplerInfo(
    int SamplerArgIndex,
    ushort SamplerDest,
    uint SamplerHash,
    byte TextureSemantic,
    string TextureName,
    UvRoute? UvRoute);
