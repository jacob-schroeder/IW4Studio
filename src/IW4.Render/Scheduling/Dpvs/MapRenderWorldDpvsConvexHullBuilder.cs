using System.Buffers;
using System.Numerics;

namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Convex reduction used by the PS3 queued-portal path at 0x002560E0. The
/// native 0.001 point-to-edge tolerance is retained.
/// </summary>
internal static class MapRenderWorldDpvsConvexHullBuilder
{
    private const float EdgeDistanceEpsilon = 0.001f;

    public static Vector2[] Build(IReadOnlyList<Vector2> points)
    {
        Span<Vector2> destination =
            stackalloc Vector2[64];
        int count = BuildInto(points, destination);
        return destination[..count].ToArray();
    }

    internal static int BuildInto(
        IReadOnlyList<Vector2> points,
        Span<Vector2> destination)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count is < 3 or > 64)
            return 0;
        if (destination.Length < 64)
        {
            throw new ArgumentException(
                "The convex-hull destination must retain the native sixty-four-point capacity.",
                nameof(destination));
        }

        HullPoint[] sorted =
            ArrayPool<HullPoint>.Shared.Rent(points.Count);
        Vector2[] chains =
            ArrayPool<Vector2>.Shared.Rent(points.Count * 2);
        try
        {
            return BuildIntoCore(
                points,
                destination,
                sorted,
                chains);
        }
        finally
        {
            ArrayPool<HullPoint>.Shared.Return(
                sorted,
                clearArray: false);
            ArrayPool<Vector2>.Shared.Return(
                chains,
                clearArray: false);
        }
    }

    internal static int BuildInto(
        IReadOnlyList<Vector2> points,
        Span<Vector2> destination,
        Scratch scratch)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(scratch);
        return BuildIntoCore(
            points,
            destination,
            scratch.Sorted,
            scratch.Chains);
    }

    private static int BuildIntoCore(
        IReadOnlyList<Vector2> points,
        Span<Vector2> destination,
        HullPoint[] sorted,
        Vector2[] chains)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count is < 3 or > 64)
            return 0;
        if (destination.Length < 64 ||
            sorted.Length < 64 ||
            chains.Length < 128)
        {
            throw new ArgumentException(
                "Convex-hull working-set storage does not retain the native capacities.");
        }

        for (int index = 0; index < points.Count; index++)
            sorted[index] = new(points[index], index);
        SortStable(sorted, points.Count);

        int lowerCount = 0;
        for (int index = 0; index < points.Count; index++)
        {
            AddPoint(
                chains,
                start: 0,
                ref lowerCount,
                sorted[index].Point);
        }

        int upperStart = points.Count;
        int upperCount = 0;
        for (int index = points.Count - 1; index >= 0; index--)
        {
            AddPoint(
                chains,
                upperStart,
                ref upperCount,
                sorted[index].Point);
        }

        int lowerRetainedCount = Math.Max(0, lowerCount - 1);
        int upperRetainedCount = Math.Max(0, upperCount - 1);
        int hullCount = checked(
            lowerRetainedCount + upperRetainedCount);
        if (hullCount < 3)
            return 0;

        int startIndex = 0;
        for (int index = 1; index < hullCount; index++)
        {
            if (GetHullPoint(
                    chains,
                    lowerRetainedCount,
                    upperStart,
                    index).Y <
                GetHullPoint(
                    chains,
                    lowerRetainedCount,
                    upperStart,
                    startIndex).Y)
            {
                startIndex = index;
            }
        }

        // IW3 Com_ConvexHull grows the final HullAxis-space winding
        // clockwise. Start at the minimum-Y point and walk the monotone
        // chain backwards without materializing intermediate rotated arrays.
        for (int index = 0; index < hullCount; index++)
        {
            int sourceIndex =
                (startIndex - index + hullCount) % hullCount;
            destination[index] = GetHullPoint(
                chains,
                lowerRetainedCount,
                upperStart,
                sourceIndex);
        }
        return hullCount;
    }

    private static void SortStable(
        HullPoint[] points,
        int count)
    {
        // Native portal hulls retain at most sixty-four points. Insertion sort
        // avoids Array.Sort's comparer adapter allocation while preserving the
        // exact X/Y/input-ordinal ordering used to make duplicate points
        // deterministic.
        for (int index = 1; index < count; index++)
        {
            HullPoint value = points[index];
            int insertion = index;
            while (insertion > 0 &&
                   HullPointComparer.Instance.Compare(
                       points[insertion - 1],
                       value) > 0)
            {
                points[insertion] = points[insertion - 1];
                insertion--;
            }
            points[insertion] = value;
        }
    }

    private static Vector2 GetHullPoint(
        Vector2[] chains,
        int lowerRetainedCount,
        int upperStart,
        int index) =>
        index < lowerRetainedCount
            ? chains[index]
            : chains[
                upperStart +
                index -
                lowerRetainedCount];

    private static void AddPoint(
        Vector2[] chain,
        int start,
        ref int count,
        Vector2 point)
    {
        while (count >= 2 && !IsStrictlyLeft(
                   chain[start + count - 2],
                   chain[start + count - 1],
                   point))
        {
            count--;
        }
        chain[start + count++] = point;
    }

    private static bool IsStrictlyLeft(Vector2 first, Vector2 second, Vector2 point)
    {
        Vector2 edge = second - first;
        float length = edge.Length();
        if (!(length > 0f))
            return false;
        float cross = edge.X * (point.Y - first.Y) -
            edge.Y * (point.X - first.X);
        return cross > EdgeDistanceEpsilon * length;
    }

    internal readonly record struct HullPoint(
        Vector2 Point,
        int InputIndex);

    internal sealed class Scratch
    {
        internal HullPoint[] Sorted { get; } = new HullPoint[64];

        internal Vector2[] Chains { get; } = new Vector2[128];
    }

    private sealed class HullPointComparer : IComparer<HullPoint>
    {
        internal static HullPointComparer Instance { get; } = new();

        public int Compare(HullPoint left, HullPoint right)
        {
            int comparison = left.Point.X.CompareTo(right.Point.X);
            if (comparison != 0)
                return comparison;
            comparison = left.Point.Y.CompareTo(right.Point.Y);
            return comparison != 0
                ? comparison
                : left.InputIndex.CompareTo(right.InputIndex);
        }
    }
}
