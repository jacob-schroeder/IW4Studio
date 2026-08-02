using IW4.Assets.Math;

namespace IW4.Render.Scheduling.Dpvs;

internal static class MapRenderWorldDpvsAabbPlaneTester
{
    public static bool IsOutside(
        MapRenderWorldDpvsBounds bounds,
        ReadOnlySpan<MapRenderWorldDpvsClipPlane> planes)
    {
        foreach (MapRenderWorldDpvsClipPlane plane in planes)
        {
            if (PositiveVertexDistance(bounds, plane) <= 0f)
                return true;
        }
        return false;
    }

    public static float PositiveVertexDistance(
        MapRenderWorldDpvsBounds bounds,
        MapRenderWorldDpvsClipPlane plane) =>
        plane.NormalX * (plane.NormalX >= 0f ? bounds.MaxX : bounds.MinX) +
        plane.NormalY * (plane.NormalY >= 0f ? bounds.MaxY : bounds.MinY) +
        plane.NormalZ * (plane.NormalZ >= 0f ? bounds.MaxZ : bounds.MinZ) +
        plane.CoefficientW;

    public static float NegativeVertexDistance(
        MapRenderWorldDpvsBounds bounds,
        MapRenderWorldDpvsClipPlane plane) =>
        plane.NormalX * (plane.NormalX >= 0f ? bounds.MinX : bounds.MaxX) +
        plane.NormalY * (plane.NormalY >= 0f ? bounds.MinY : bounds.MaxY) +
        plane.NormalZ * (plane.NormalZ >= 0f ? bounds.MinZ : bounds.MaxZ) +
        plane.CoefficientW;

    public static bool TryGetBounds(
        IReadOnlyList<float> mins,
        IReadOnlyList<float> maxs,
        out MapRenderWorldDpvsBounds bounds)
    {
        bounds = default;
        if (mins.Count < 3 || maxs.Count < 3)
            return false;
        bounds = new(
            mins[0], mins[1], mins[2],
            maxs[0], maxs[1], maxs[2]);
        return bounds.IsValid;
    }

    public static bool TryGetBounds(
        Bounds source,
        out MapRenderWorldDpvsBounds bounds)
    {
        Vec3 midpoint = source.MidPoint;
        Vec3 halfSize = source.HalfSize;
        bounds = new(
            midpoint.X - halfSize.X,
            midpoint.Y - halfSize.Y,
            midpoint.Z - halfSize.Z,
            midpoint.X + halfSize.X,
            midpoint.Y + halfSize.Y,
            midpoint.Z + halfSize.Z);
        return halfSize.X >= 0f &&
            halfSize.Y >= 0f &&
            halfSize.Z >= 0f &&
            bounds.IsValid;
    }
}
