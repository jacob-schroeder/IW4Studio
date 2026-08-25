using System.Buffers.Binary;
using System.Numerics;
using IW4.Assets.Assets.GfxMap;
using IW4.Render.Transforms;

namespace IW4.Render.Geometry;

internal static class PreparedWorldSurfaceGeometryFactory
{
    private const float MaxReasonableCoordinate = 1_000_000f;
    private const float SkyboxEdgeThreshold = 32768f;

    internal static PreparedWorldSurfaceGeometry Create(
        int surfaceOrdinal,
        GfxSurface surface,
        ReadOnlySpan<byte> vertexBytes,
        IReadOnlyList<ushort> sourceIndices)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(sourceIndices);

        SrfTriangles source = surface.Triangles;
        int sourceVertexCount = source.VertexCount;
        var positions = new Vector3[sourceVertexCount];
        var positionReady = new bool[sourceVertexCount];
        if (sourceVertexCount == 0)
        {
            return new PreparedWorldSurfaceGeometry(
                surfaceOrdinal,
                source,
                positions,
                positionReady,
                [],
                0,
                0,
                0,
                RenderBounds.Empty);
        }

        for (int sourceVertexSlot = 0;
             sourceVertexSlot < sourceVertexCount;
             sourceVertexSlot++)
        {
            long sourceVertexIndex =
                source.MinVertexIndex + (long)sourceVertexSlot;
            if (sourceVertexIndex > int.MaxValue)
                continue;

            int worldVertexIndex = checked(
                source.BaseVertex + (int)sourceVertexIndex);
            positionReady[sourceVertexSlot] = TryReadPosition(
                vertexBytes,
                worldVertexIndex,
                out positions[sourceVertexSlot]);
        }

        var triangles = new List<PreparedWorldSurfaceTriangle>(source.TriCount);
        int sourceTopologyReadFailures = 0;
        int positionReadFailures = 0;
        int skyboxTriangles = 0;
        RenderBounds bounds = RenderBounds.Empty;
        for (int triangle = 0; triangle < source.TriCount; triangle++)
        {
            int indexOffset = source.BaseIndex + triangle * 3;
            if (indexOffset < 0 || indexOffset + 2 >= sourceIndices.Count)
            {
                sourceTopologyReadFailures++;
                continue;
            }

            int sourceIndex0 = sourceIndices[indexOffset];
            int sourceIndex1 = sourceIndices[indexOffset + 1];
            int sourceIndex2 = sourceIndices[indexOffset + 2];
            if (!TryGetSourceVertexSlot(
                    sourceIndex0,
                    source.MinVertexIndex,
                    sourceVertexCount,
                    out int vertexSlot0) ||
                !TryGetSourceVertexSlot(
                    sourceIndex1,
                    source.MinVertexIndex,
                    sourceVertexCount,
                    out int vertexSlot1) ||
                !TryGetSourceVertexSlot(
                    sourceIndex2,
                    source.MinVertexIndex,
                    sourceVertexCount,
                    out int vertexSlot2))
            {
                sourceTopologyReadFailures++;
                continue;
            }

            if (!positionReady[vertexSlot0] ||
                !positionReady[vertexSlot1] ||
                !positionReady[vertexSlot2])
            {
                positionReadFailures++;
                continue;
            }

            Vector3 p0 = positions[vertexSlot0];
            Vector3 p1 = positions[vertexSlot1];
            Vector3 p2 = positions[vertexSlot2];
            bool isSkyboxScale = IsSkyboxScaleTriangle(p0, p1, p2);
            if (isSkyboxScale)
                skyboxTriangles++;
            bounds = bounds.Include(p0).Include(p1).Include(p2);
            triangles.Add(new PreparedWorldSurfaceTriangle(
                triangle,
                vertexSlot0,
                vertexSlot1,
                vertexSlot2,
                isSkyboxScale));
        }

        return new PreparedWorldSurfaceGeometry(
            surfaceOrdinal,
            source,
            positions,
            positionReady,
            triangles.ToArray(),
            sourceTopologyReadFailures,
            positionReadFailures,
            skyboxTriangles,
            bounds);
    }

    private static bool TryGetSourceVertexSlot(
        int sourceVertexIndex,
        uint firstSourceVertexIndex,
        int sourceVertexCount,
        out int sourceVertexSlot)
    {
        long slot = sourceVertexIndex - (long)firstSourceVertexIndex;
        if (slot < 0 || slot >= sourceVertexCount)
        {
            sourceVertexSlot = -1;
            return false;
        }

        sourceVertexSlot = (int)slot;
        return true;
    }

    private static bool TryReadPosition(
        ReadOnlySpan<byte> vertexBytes,
        int vertexIndex,
        out Vector3 position)
    {
        position = default;
        int offset = vertexIndex * WorldVertexLayout.WorldVertexStride;
        if (vertexIndex < 0 ||
            offset < 0 ||
            offset + 12 > vertexBytes.Length)
        {
            return false;
        }

        var gamePosition = new Vector3(
            BinaryPrimitives.ReadSingleBigEndian(vertexBytes[offset..]),
            BinaryPrimitives.ReadSingleBigEndian(vertexBytes[(offset + 4)..]),
            BinaryPrimitives.ReadSingleBigEndian(vertexBytes[(offset + 8)..]));
        position = RenderCoordinateConverter.GameToRenderPosition(gamePosition);
        return IsReasonable(position);
    }

    private static bool IsSkyboxScaleTriangle(
        Vector3 p0,
        Vector3 p1,
        Vector3 p2)
    {
        float maxEdge = MathF.Max(
            Vector3.Distance(p0, p1),
            MathF.Max(Vector3.Distance(p1, p2), Vector3.Distance(p2, p0)));
        return maxEdge > SkyboxEdgeThreshold;
    }

    private static bool IsReasonable(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        MathF.Abs(value.X) < MaxReasonableCoordinate &&
        MathF.Abs(value.Y) < MaxReasonableCoordinate &&
        MathF.Abs(value.Z) < MaxReasonableCoordinate;
}
