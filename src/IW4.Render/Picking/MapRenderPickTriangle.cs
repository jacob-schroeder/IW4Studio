using System.Numerics;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Render.Geometry;

namespace IW4.Render.Picking;

public readonly record struct MapRenderPickTriangle(
    MapRenderPickKind Kind,
    int ObjectIndex,
    int SurfaceIndex,
    int TriangleIndex,
    string Name,
    Vector3 P0,
    Vector3 P1,
    Vector3 P2);
