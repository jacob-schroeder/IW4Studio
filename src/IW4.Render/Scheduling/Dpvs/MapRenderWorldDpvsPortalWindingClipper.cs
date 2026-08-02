using System.Buffers;
using System.Numerics;

namespace IW4.Render.Scheduling.Dpvs;

/// <summary>PS3 default_mp.elf 0x00349C28 / 0x00348B70.</summary>
internal static class MapRenderWorldDpvsPortalWindingClipper
{
    private const int MaximumVertexCount = 128;
    private const float EqualEpsilon = 0.001f;

    public static bool TryClip(
        IReadOnlyList<Vector3> source,
        MapRenderWorldDpvsClipPlane parentPlane,
        MapRenderWorldDpvsClipPlane? farPlane,
        IReadOnlyList<MapRenderWorldDpvsClipPlane> planes,
        int planeCount,
        out Vector3[] winding,
        out string? failure)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(planes);
        Span<Vector3> sourceBuffer =
            stackalloc Vector3[MaximumVertexCount];
        if (source.Count <= MaximumVertexCount)
        {
            for (int index = 0; index < source.Count; index++)
                sourceBuffer[index] = source[index];
        }
        Span<Vector3> destination =
            stackalloc Vector3[MaximumVertexCount];
        bool succeeded = TryClipInto(
            source.Count <= MaximumVertexCount
                ? sourceBuffer[..source.Count]
                : ReadOnlySpan<Vector3>.Empty,
            parentPlane,
            farPlane,
            planes,
            planeCount,
            destination,
            out int windingCount,
            out failure,
            declaredSourceCount: source.Count);
        winding = succeeded
            ? destination[..windingCount].ToArray()
            : [];
        return succeeded;
    }

    internal static bool TryClipInto(
        ReadOnlySpan<Vector3> source,
        MapRenderWorldDpvsClipPlane parentPlane,
        MapRenderWorldDpvsClipPlane? farPlane,
        IReadOnlyList<MapRenderWorldDpvsClipPlane> planes,
        int planeCount,
        Span<Vector3> winding,
        out int windingCount,
        out string? failure) =>
        TryClipInto(
            source,
            parentPlane,
            farPlane,
            planes,
            planeCount,
            winding,
            out windingCount,
            out failure,
            declaredSourceCount: source.Length);

    internal static bool TryClipInto(
        Vector3[] source,
        int sourceCount,
        MapRenderWorldDpvsClipPlane parentPlane,
        MapRenderWorldDpvsClipPlane? farPlane,
        IReadOnlyList<MapRenderWorldDpvsClipPlane> planes,
        int planeCount,
        Vector3[] winding,
        out int windingCount,
        out string? failure)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(winding);
        return TryClipIntoBuffers(
            source,
            sourceCount,
            parentPlane,
            farPlane,
            planes,
            planeCount,
            winding,
            out windingCount,
            out failure);
    }

    private static bool TryClipInto(
        ReadOnlySpan<Vector3> source,
        MapRenderWorldDpvsClipPlane parentPlane,
        MapRenderWorldDpvsClipPlane? farPlane,
        IReadOnlyList<MapRenderWorldDpvsClipPlane> planes,
        int planeCount,
        Span<Vector3> winding,
        out int windingCount,
        out string? failure,
        int declaredSourceCount)
    {
        ArgumentNullException.ThrowIfNull(planes);
        if ((uint)planeCount > (uint)planes.Count)
        {
            windingCount = 0;
            failure =
                $"Portal clip requested {planeCount} planes from {planes.Count} retained rows.";
            return false;
        }
        if (declaredSourceCount is < 3 or > MaximumVertexCount)
        {
            windingCount = 0;
            failure = $"Portal winding has {declaredSourceCount} vertices; the PS3 clip scratch supports three through {MaximumVertexCount}.";
            return false;
        }
        if (source.Length != declaredSourceCount ||
            winding.Length < MaximumVertexCount)
        {
            windingCount = 0;
            failure =
                "Portal clip scratch storage does not cover the declared winding.";
            return false;
        }

        Vector3[] first =
            ArrayPool<Vector3>.Shared.Rent(MaximumVertexCount);
        Vector3[] second =
            ArrayPool<Vector3>.Shared.Rent(MaximumVertexCount);
        try
        {
            source.CopyTo(first);
            bool succeeded = TryClipIntoBuffers(
                first,
                declaredSourceCount,
                parentPlane,
                farPlane,
                planes,
                planeCount,
                second,
                out windingCount,
                out failure);
            if (succeeded)
                second.AsSpan(0, windingCount).CopyTo(winding);
            return succeeded;
        }
        finally
        {
            ArrayPool<Vector3>.Shared.Return(
                first,
                clearArray: false);
            ArrayPool<Vector3>.Shared.Return(
                second,
                clearArray: false);
        }
    }

    private static bool TryClipIntoBuffers(
        Vector3[] source,
        int sourceCount,
        MapRenderWorldDpvsClipPlane parentPlane,
        MapRenderWorldDpvsClipPlane? farPlane,
        IReadOnlyList<MapRenderWorldDpvsClipPlane> planes,
        int planeCount,
        Vector3[] winding,
        out int windingCount,
        out string? failure)
    {
        ArgumentNullException.ThrowIfNull(planes);
        if ((uint)planeCount > (uint)planes.Count)
        {
            windingCount = 0;
            failure =
                $"Portal clip requested {planeCount} planes from {planes.Count} retained rows.";
            return false;
        }
        if (sourceCount is < 3 or > MaximumVertexCount)
        {
            windingCount = 0;
            failure = $"Portal winding has {sourceCount} vertices; the PS3 clip scratch supports three through {MaximumVertexCount}.";
            return false;
        }
        if (source.Length < MaximumVertexCount ||
            winding.Length < MaximumVertexCount ||
            ReferenceEquals(source, winding))
        {
            windingCount = 0;
            failure =
                "Portal clip working-set storage must provide two distinct native-capacity windings.";
            return false;
        }

        Vector3[] current = source;
        Vector3[] destination = winding;
        int currentCount = sourceCount;
        if (!TryApplyPlane(
                parentPlane,
                ref current,
                ref destination,
                ref currentCount,
                out string? clipFailure))
        {
            windingCount = 0;
            failure = clipFailure;
            return false;
        }
        if (currentCount == 0)
        {
            windingCount = 0;
            failure = null;
            return true;
        }
        if (farPlane is { } far)
        {
            if (!TryApplyPlane(
                    far,
                    ref current,
                    ref destination,
                    ref currentCount,
                    out clipFailure))
            {
                windingCount = 0;
                failure = clipFailure;
                return false;
            }
            if (currentCount == 0)
            {
                windingCount = 0;
                failure = null;
                return true;
            }
        }
        for (int planeIndex = 0;
             planeIndex < planeCount;
             planeIndex++)
        {
            if (!TryApplyPlane(
                    planes[planeIndex],
                    ref current,
                    ref destination,
                    ref currentCount,
                    out clipFailure))
            {
                windingCount = 0;
                failure = clipFailure;
                return false;
            }
            if (currentCount == 0)
            {
                windingCount = 0;
                failure = null;
                return true;
            }
        }

        current.AsSpan(0, currentCount).CopyTo(winding);
        windingCount = currentCount;
        failure = null;
        return true;
    }

    private static bool TryApplyPlane(
        MapRenderWorldDpvsClipPlane plane,
        ref Vector3[] current,
        ref Vector3[] destination,
        ref int currentCount,
        out string? failure)
    {
        ClipPlaneResult result = TryClipPlane(
            current.AsSpan(0, currentCount),
            plane,
            destination,
            out int destinationCount,
            out failure);
        switch (result)
        {
            case ClipPlaneResult.Unchanged:
                return true;
            case ClipPlaneResult.Changed:
                (current, destination) = (destination, current);
                currentCount = destinationCount;
                return true;
            case ClipPlaneResult.Empty:
                currentCount = 0;
                return true;
            case ClipPlaneResult.Failed:
                return false;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static ClipPlaneResult TryClipPlane(
        ReadOnlySpan<Vector3> input,
        MapRenderWorldDpvsClipPlane plane,
        Span<Vector3> output,
        out int outputCount,
        out string? failure)
    {
        if (input.Length > MaximumVertexCount ||
            output.Length < MaximumVertexCount)
        {
            outputCount = 0;
            failure = "Portal winding exceeded the PS3 128-vertex clip scratch.";
            return ClipPlaneResult.Failed;
        }

        Span<float> adjustedDistances =
            stackalloc float[MaximumVertexCount];
        Span<sbyte> sides =
            stackalloc sbyte[MaximumVertexCount];
        int frontCount = 0;
        int backCount = 0;
        for (int index = 0; index < input.Length; index++)
        {
            float adjusted = SignedDistance(plane, input[index]) - EqualEpsilon;
            adjustedDistances[index] = adjusted;
            sides[index] = 0;
            if (adjusted < -EqualEpsilon)
            {
                sides[index] = -1;
                backCount++;
            }
            else if (adjusted > EqualEpsilon)
            {
                sides[index] = 1;
                frontCount++;
            }
        }

        if (frontCount == 0)
        {
            outputCount = 0;
            failure = null;
            return ClipPlaneResult.Empty;
        }
        if (backCount == 0)
        {
            outputCount = input.Length;
            failure = null;
            return ClipPlaneResult.Unchanged;
        }

        int clippedCount = 0;
        for (int index = 0;
             index < input.Length && clippedCount < MaximumVertexCount;
             index++)
        {
            int nextIndex = (index + 1) % input.Length;
            int side = sides[index];
            int nextSide = sides[nextIndex];
            if (side >= 0)
                output[clippedCount++] = input[index];
            if (clippedCount == MaximumVertexCount)
                break;
            if (nextSide != 0 && nextSide != side)
            {
                float currentDistance = adjustedDistances[index];
                float nextDistance = adjustedDistances[nextIndex];
                float denominator = currentDistance - nextDistance;
                if (denominator == 0f || !float.IsFinite(denominator))
                {
                    outputCount = 0;
                    failure = "Portal clip intersection has an invalid distance denominator.";
                    return ClipPlaneResult.Failed;
                }
                float amount = currentDistance / denominator;
                output[clippedCount++] =
                    Vector3.Lerp(
                        input[index],
                        input[nextIndex],
                        amount);
            }
        }
        if (clippedCount is > 0 and < 3)
        {
            outputCount = 0;
            failure = "Portal clipping produced fewer than three vertices.";
            return ClipPlaneResult.Failed;
        }

        outputCount = clippedCount;
        failure = null;
        return ClipPlaneResult.Changed;
    }

    private static float SignedDistance(
        MapRenderWorldDpvsClipPlane plane,
        Vector3 point) =>
        point.X * plane.NormalX +
        point.Y * plane.NormalY +
        point.Z * plane.NormalZ +
        plane.CoefficientW;

    private enum ClipPlaneResult
    {
        Failed,
        Empty,
        Unchanged,
        Changed
    }
}
