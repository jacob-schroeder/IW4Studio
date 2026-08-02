using System.Numerics;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;

namespace IW4.Render.Geometry;

public sealed record MapRenderInstancedSolidBatch(
    float[] Vertices,
    uint[] Indices,
    IReadOnlyList<MapRenderStaticModelInstance> Instances);
