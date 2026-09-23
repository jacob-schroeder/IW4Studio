using System.Numerics;
using IW4.Game.Assets.ColMap;
using IW4.Game.Assets.GfxMap;
using IW4.Game.Zone;
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
