using System.Numerics;
using IW4.Render.Geometry;
using IW4.Render.Resources;

namespace IW4.Render.World;

/// <summary>
/// Builds immutable per-surface ownership over a scene-builder world batch.
/// Failure is conservative: the renderer retains the original whole-batch draw
/// instead of dropping geometry whose range metadata cannot be resolved.
/// </summary>
internal static class MapRenderWorldSurfaceSpanCatalog
{
    /// <summary>
    /// Builds the same ordered full-index-coverage catalog from the immutable
    /// normal-camera snapshot consumed by native backends. Bounds are
    /// intentionally empty: translated RSX programs use DPVS-only visibility
    /// because their decoded positions are not conservative.
    /// </summary>
    public static bool TryCreate(
        RenderNormalCameraPreparedPassSnapshot pass,
        out MapRenderWorldSurfaceSpan[] spans)
    {
        ArgumentNullException.ThrowIfNull(pass);
        spans = [];
        if (pass.SourceKind != RenderNormalCameraDrawSourceKind.World)
            return false;

        int surfaceRangeCount = 0;
        foreach (RenderMaterialPickRangeSnapshot range in pass.PickRanges)
        {
            if (range.Kind == MapRenderPickKind.GfxSurface)
                surfaceRangeCount++;
        }
        if (surfaceRangeCount == 0)
            return false;

        int expectedFirstIndex = 0;
        int ordinal = 0;
        var result = new MapRenderWorldSurfaceSpan[
            surfaceRangeCount];
        foreach (RenderMaterialPickRangeSnapshot range in pass.PickRanges)
        {
            if (range.Kind != MapRenderPickKind.GfxSurface)
                continue;
            if (range.SurfaceIndex < 0 ||
                range.FirstIndex != expectedFirstIndex ||
                range.IndexCount <= 0 ||
                range.IndexCount % 3 != 0)
            {
                return false;
            }

            int endIndex;
            try
            {
                endIndex = checked(range.FirstIndex + range.IndexCount);
            }
            catch (OverflowException)
            {
                return false;
            }
            if (endIndex > pass.Geometry.IndexCount)
                return false;

            result[ordinal++] = new(
                range.SurfaceIndex,
                range.FirstIndex,
                range.IndexCount,
                RenderBounds.Empty);
            expectedFirstIndex = endIndex;
        }

        if (expectedFirstIndex != pass.Geometry.IndexCount)
            return false;

        spans = result;
        return true;
    }

    public static bool TryCreate(
        MapRenderTexturedBatch batch,
        out MapRenderWorldSurfaceSpan[] spans,
        bool includeDecodedBounds = true)
    {
        ArgumentNullException.ThrowIfNull(batch);
        spans = [];

        int surfaceRangeCount = 0;
        foreach (MapRenderPickRange range in batch.PickRanges)
        {
            if (range.Kind == MapRenderPickKind.GfxSurface)
                surfaceRangeCount++;
        }
        if (surfaceRangeCount == 0)
            return false;

        // A partial catalog could hide unowned indices. Require exact ordered
        // coverage and fall back to the original whole-batch draw otherwise.
        int expectedFirstIndex = 0;
        int ordinal = 0;
        var result = new MapRenderWorldSurfaceSpan[surfaceRangeCount];
        foreach (MapRenderPickRange range in batch.PickRanges)
        {
            if (range.Kind != MapRenderPickKind.GfxSurface)
                continue;
            if (range.SurfaceIndex < 0 ||
                range.FirstIndex != expectedFirstIndex ||
                range.IndexCount <= 0 ||
                range.IndexCount % 3 != 0)
            {
                return false;
            }

            int endIndex;
            try
            {
                endIndex = checked(range.FirstIndex + range.IndexCount);
            }
            catch (OverflowException)
            {
                return false;
            }
            if (endIndex > batch.Indices.Length)
                return false;

            RenderBounds bounds = RenderBounds.Empty;
            if (includeDecodedBounds &&
                !TryResolveBounds(batch, range, out bounds))
            {
                return false;
            }
            result[ordinal] = new(
                range.SurfaceIndex,
                range.FirstIndex,
                range.IndexCount,
                bounds);
            expectedFirstIndex = endIndex;
            ordinal++;
        }

        if (expectedFirstIndex != batch.Indices.Length)
            return false;

        spans = result;
        return true;
    }

    private static bool TryResolveBounds(
        MapRenderTexturedBatch batch,
        MapRenderPickRange range,
        out RenderBounds bounds)
    {
        bounds = RenderBounds.Empty;
        for (int indexOffset = range.FirstIndex;
             indexOffset < range.FirstIndex + range.IndexCount;
             indexOffset++)
        {
            uint vertexIndex = batch.Indices[indexOffset];
            long vertexOffset =
                (long)vertexIndex * MapRenderScene.TexturedVertexFloatCount;
            if (vertexOffset > int.MaxValue ||
                vertexOffset + 2 >= batch.Vertices.Length)
                return false;
            int positionOffset = (int)vertexOffset;

            var position = new Vector3(
                batch.Vertices[positionOffset],
                batch.Vertices[positionOffset + 1],
                batch.Vertices[positionOffset + 2]);
            if (float.IsFinite(position.X) &&
                float.IsFinite(position.Y) &&
                float.IsFinite(position.Z))
            {
                bounds = bounds.Include(position);
            }
        }

        // Empty/non-finite bounds are intentionally retained. The conservative
        // frustum treats them as visible, matching the previous whole-batch
        // behavior while DPVS can still identify the authored surface.
        return true;
    }
}
