using System.Numerics;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;

namespace IW4.Render.Picking;

public readonly record struct MapRenderPickTriangleTexCoords(
    Vector2 Uv0,
    Vector2 Uv1,
    Vector2 Uv2,
    Vector2 LightmapUv0,
    Vector2 LightmapUv1,
    Vector2 LightmapUv2);
