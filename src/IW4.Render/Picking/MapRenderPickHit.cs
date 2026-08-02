using System.Numerics;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Render.Geometry;

namespace IW4.Render.Picking;

public readonly record struct MapRenderPickHit(
    MapRenderPickKind Kind,
    int ObjectIndex,
    int SurfaceIndex,
    int TriangleIndex,
    string Name,
    float Distance,
    Vector3 Position,
    MapRenderPickMaterialInfo? Material,
    MapRenderPickTriangleTexCoords? TexCoords = null,
    string AuthoredMaterialName = "");
