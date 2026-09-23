namespace IW4.Render.Geometry;

public enum MapRenderPickKind
{
    GfxSurface,
    StaticModel,
    CollisionTriangle,
    CollisionBrushBounds,
    CollisionStaticModelBounds,
    GfxBrushModelSurface
}

public readonly record struct MapRenderPickRange(
    MapRenderPickKind Kind,
    int ObjectIndex,
    int SurfaceIndex,
    int FirstIndex,
    int IndexCount,
    string Name,
    string AuthoredMaterialName = "");
