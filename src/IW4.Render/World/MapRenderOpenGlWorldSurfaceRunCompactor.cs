using IW4.Render.Visibility;

namespace IW4.Render.World;

/// <summary>
/// Immutable index ownership and conservative cull bounds for one authored
/// world surface inside an editor-preview material batch.
/// </summary>
internal readonly record struct MapRenderOpenGlWorldSurfaceSpan(
    int SurfaceIndex,
    int FirstIndex,
    int IndexCount,
    RenderBounds Bounds)
{
    public int EndIndexExclusive => checked(FirstIndex + IndexCount);
}

/// <summary>
/// One contiguous visible interval in a shared world element buffer. Adjacent
/// visible surface spans collapse into one interval without copying geometry.
/// </summary>
internal readonly record struct MapRenderOpenGlWorldVisibleRun(
    int FirstIndex,
    int IndexCount,
    int SurfaceSpanCount)
{
    public int EndIndexExclusive => checked(FirstIndex + IndexCount);
}

internal readonly record struct MapRenderOpenGlWorldSurfaceCompactionResult(
    int RunCount,
    int VisibleSurfaceSpanCount,
    long VisibleIndexCount);

/// <summary>
/// Allocation-free editor-preview transfer from the PS3-shaped flat surface
/// visibility mask to contiguous host draw ranges. The input order is retained
/// exactly; this does not sort or reinterpret authored material/pass state.
/// </summary>
internal static class MapRenderOpenGlWorldSurfaceRunCompactor
{
    public static MapRenderOpenGlWorldSurfaceCompactionResult Compact(
        ReadOnlySpan<MapRenderOpenGlWorldSurfaceSpan> spans,
        ReadOnlySpan<uint> dpvsSurfaceWords,
        bool hasDpvsVisibility,
        MapRenderCameraFrustum? frustum,
        Span<MapRenderOpenGlWorldVisibleRun> destination)
    {
        if (destination.Length < spans.Length)
        {
            throw new ArgumentException(
                "The visible-run destination must cover the worst-case one-run-per-span result.",
                nameof(destination));
        }

        int runCount = 0;
        int visibleSpanCount = 0;
        long visibleIndexCount = 0;
        for (int spanOrdinal = 0; spanOrdinal < spans.Length; spanOrdinal++)
        {
            MapRenderOpenGlWorldSurfaceSpan span = spans[spanOrdinal];
            if (!IsVisible(
                    span,
                    dpvsSurfaceWords,
                    hasDpvsVisibility,
                    frustum))
            {
                continue;
            }

            visibleSpanCount++;
            visibleIndexCount = checked(
                visibleIndexCount + span.IndexCount);
            if (runCount != 0)
            {
                MapRenderOpenGlWorldVisibleRun previous =
                    destination[runCount - 1];
                if (previous.EndIndexExclusive == span.FirstIndex)
                {
                    destination[runCount - 1] = previous with
                    {
                        IndexCount = checked(
                            previous.IndexCount + span.IndexCount),
                        SurfaceSpanCount = checked(
                            previous.SurfaceSpanCount + 1)
                    };
                    continue;
                }
            }

            destination[runCount++] = new(
                span.FirstIndex,
                span.IndexCount,
                SurfaceSpanCount: 1);
        }

        return new(
            runCount,
            visibleSpanCount,
            visibleIndexCount);
    }

    private static bool IsVisible(
        MapRenderOpenGlWorldSurfaceSpan span,
        ReadOnlySpan<uint> dpvsSurfaceWords,
        bool hasDpvsVisibility,
        MapRenderCameraFrustum? frustum)
    {
        if (frustum is not null && !frustum.Intersects(span.Bounds))
            return false;

        if (!hasDpvsVisibility)
            return true;

        int wordIndex = span.SurfaceIndex >> 5;
        if ((uint)wordIndex >= (uint)dpvsSurfaceWords.Length)
        {
            // Match the existing conservative editor-preview policy: a
            // malformed or incomplete optional mask cannot hide geometry.
            return true;
        }

        uint mask = 0x8000_0000u >> (span.SurfaceIndex & 31);
        return (dpvsSurfaceWords[wordIndex] & mask) != 0;
    }
}
