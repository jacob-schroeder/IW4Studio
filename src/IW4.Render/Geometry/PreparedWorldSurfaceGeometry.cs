using System.Numerics;
using IW4.Assets.Assets.GfxMap;

namespace IW4.Render.Geometry;

/// <summary>
/// Immutable position/topology preparation for one GfxWorld surface. Slots
/// remain in the serialized source-index domain; consumers compact them in
/// first triangle-reference order for their own vertex formats.
/// </summary>
internal sealed class PreparedWorldSurfaceGeometry
{
    private readonly Vector3[] _positions;
    private readonly bool[] _positionReady;
    private readonly PreparedWorldSurfaceTriangle[] _triangles;

    internal PreparedWorldSurfaceGeometry(
        int surfaceOrdinal,
        SrfTriangles source,
        Vector3[] positions,
        bool[] positionReady,
        PreparedWorldSurfaceTriangle[] triangles,
        int sourceTopologyReadFailureTriangleCount,
        int positionReadFailureTriangleCount,
        int skyboxTriangleCount,
        RenderBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(positionReady);
        ArgumentNullException.ThrowIfNull(triangles);
        if (surfaceOrdinal < -1)
            throw new ArgumentOutOfRangeException(nameof(surfaceOrdinal));
        if (positions.Length != source.VertexCount ||
            positionReady.Length != source.VertexCount)
        {
            throw new ArgumentException(
                "Prepared world positions must cover the exact declared surface vertex range.");
        }
        bool triangleOutcomesMatch = source.VertexCount == 0
            ? triangles.Length == 0 &&
              sourceTopologyReadFailureTriangleCount == 0 &&
              positionReadFailureTriangleCount == 0 &&
              skyboxTriangleCount == 0
            : triangles.Length + sourceTopologyReadFailureTriangleCount +
                positionReadFailureTriangleCount == source.TriCount;
        if (sourceTopologyReadFailureTriangleCount < 0 ||
            positionReadFailureTriangleCount < 0 ||
            skyboxTriangleCount < 0 ||
            !triangleOutcomesMatch)
        {
            throw new ArgumentException(
                "Prepared world triangle outcomes must partition the declared surface triangle range.");
        }

        SurfaceOrdinal = surfaceOrdinal;
        BaseVertex = source.BaseVertex;
        FirstSourceVertexIndex = source.MinVertexIndex;
        SourceVertexCount = source.VertexCount;
        SourceTriangleCount = source.TriCount;
        BaseIndex = source.BaseIndex;
        _positions = positions;
        _positionReady = positionReady;
        _triangles = triangles;
        SourceTopologyReadFailureTriangleCount =
            sourceTopologyReadFailureTriangleCount;
        PositionReadFailureTriangleCount = positionReadFailureTriangleCount;
        SkyboxTriangleCount = skyboxTriangleCount;
        Bounds = bounds;
    }

    internal int SurfaceOrdinal { get; }

    internal int BaseVertex { get; }

    internal uint FirstSourceVertexIndex { get; }

    internal int SourceVertexCount { get; }

    internal int SourceTriangleCount { get; }

    internal int BaseIndex { get; }

    internal int SourceTopologyReadFailureTriangleCount { get; }

    internal int PositionReadFailureTriangleCount { get; }

    internal int SolidSkippedTriangleCount =>
        SourceTopologyReadFailureTriangleCount + PositionReadFailureTriangleCount;

    internal int SolidReadFailureTriangleCount => SolidSkippedTriangleCount;

    internal int SolidTriangleCount => _triangles.Length;

    internal int SkyboxTriangleCount { get; }

    internal RenderBounds Bounds { get; }

    internal ReadOnlySpan<PreparedWorldSurfaceTriangle> Triangles => _triangles;

    internal bool Matches(GfxSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        SrfTriangles source = surface.Triangles;
        return source.BaseVertex == BaseVertex &&
               source.MinVertexIndex == FirstSourceVertexIndex &&
               source.VertexCount == SourceVertexCount &&
               source.TriCount == SourceTriangleCount &&
               source.BaseIndex == BaseIndex;
    }

    internal bool TryGetPosition(int sourceVertexSlot, out Vector3 position)
    {
        if ((uint)sourceVertexSlot >= (uint)_positions.Length ||
            !_positionReady[sourceVertexSlot])
        {
            position = default;
            return false;
        }

        position = _positions[sourceVertexSlot];
        return true;
    }

    internal Vector3 GetPosition(int sourceVertexSlot)
    {
        if (!TryGetPosition(sourceVertexSlot, out Vector3 position))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceVertexSlot),
                sourceVertexSlot,
                "World surface position slot is unavailable.");
        }

        return position;
    }

    internal int GetSourceVertexIndex(int sourceVertexSlot)
    {
        if ((uint)sourceVertexSlot >= (uint)SourceVertexCount)
            throw new ArgumentOutOfRangeException(nameof(sourceVertexSlot));

        long sourceVertexIndex = FirstSourceVertexIndex + (long)sourceVertexSlot;
        return checked((int)sourceVertexIndex);
    }
}

internal readonly record struct PreparedWorldSurfaceTriangle(
    int SourceTriangleOrdinal,
    int VertexSlot0,
    int VertexSlot1,
    int VertexSlot2,
    bool IsSkyboxScale);
