namespace IW4.Render.OpenGl.World;

internal readonly record struct MapRenderOpenGlWorldGeometryArenaSource(
    int MeshIndex,
    float[] Vertices,
    uint[] Indices);

internal readonly record struct MapRenderOpenGlWorldGeometryArenaPlacement(
    int MeshIndex,
    nuint IndexOffsetBytes,
    int BaseVertex);

internal sealed class MapRenderOpenGlWorldGeometryArenaPacking
{
    internal MapRenderOpenGlWorldGeometryArenaPacking(
        float[] vertices,
        uint[] indices,
        MapRenderOpenGlWorldGeometryArenaPlacement[] placements)
    {
        Vertices = vertices;
        Indices = indices;
        Placements = placements;
    }

    internal float[] Vertices { get; }

    internal uint[] Indices { get; }

    internal MapRenderOpenGlWorldGeometryArenaPlacement[] Placements
    {
        get;
    }

    internal int SourceCount => Placements.Length;

    internal int ImmutableBufferUploadOperationCount =>
        SourceCount == 0
            ? 0
            : 2;

    internal long ImmutableBufferUploadBytes => checked(
        ((long)Vertices.Length * sizeof(float)) +
        ((long)Indices.Length * sizeof(uint)));
}

internal static class MapRenderOpenGlWorldGeometryArenaPacker
{
    internal static MapRenderOpenGlWorldGeometryArenaPacking Pack(
        IReadOnlyList<MapRenderOpenGlWorldGeometryArenaSource> sources,
        int floatsPerVertex) =>
        PackCore(
            sources,
            floatsPerVertex,
            packedRsxLayout: null);

    internal static MapRenderOpenGlWorldGeometryArenaPacking PackTranslatedRsx(
        IReadOnlyList<MapRenderOpenGlWorldGeometryArenaSource> sources,
        OpenGlPackedRsxVertexLayout layout) =>
        PackCore(
            sources,
            OpenGlPackedRsxVertexLayout.SourceFloatStride,
            layout);

    private static MapRenderOpenGlWorldGeometryArenaPacking PackCore(
        IReadOnlyList<MapRenderOpenGlWorldGeometryArenaSource> sources,
        int sourceFloatsPerVertex,
        OpenGlPackedRsxVertexLayout? packedRsxLayout)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sourceFloatsPerVertex <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceFloatsPerVertex));
        }

        int destinationFloatsPerVertex =
            packedRsxLayout?.FloatStride ??
            sourceFloatsPerVertex;

        long totalVertexFloats = 0;
        long totalIndexCount = 0;
        for (int sourceIndex = 0;
             sourceIndex < sources.Count;
             sourceIndex++)
        {
            MapRenderOpenGlWorldGeometryArenaSource source =
                sources[sourceIndex];
            ArgumentNullException.ThrowIfNull(source.Vertices);
            ArgumentNullException.ThrowIfNull(source.Indices);
            if (source.Vertices.Length % sourceFloatsPerVertex != 0)
            {
                throw new InvalidOperationException(
                    $"World mesh {source.MeshIndex} does not match its selected arena vertex stride.");
            }

            totalVertexFloats = checked(
                totalVertexFloats +
                ((long)source.Vertices.Length /
                 sourceFloatsPerVertex) *
                destinationFloatsPerVertex);
            totalIndexCount = checked(
                totalIndexCount + source.Indices.Length);
        }

        float[] vertices = GC.AllocateUninitializedArray<float>(
            checked((int)totalVertexFloats));
        uint[] indices = GC.AllocateUninitializedArray<uint>(
            checked((int)totalIndexCount));
        var placements =
            new MapRenderOpenGlWorldGeometryArenaPlacement[sources.Count];

        int vertexFloatOffset = 0;
        int indexOffset = 0;
        for (int sourceIndex = 0;
             sourceIndex < sources.Count;
             sourceIndex++)
        {
            MapRenderOpenGlWorldGeometryArenaSource source =
                sources[sourceIndex];
            int sourceVertexCount =
                source.Vertices.Length / sourceFloatsPerVertex;
            int destinationVertexFloatCount = checked(
                sourceVertexCount * destinationFloatsPerVertex);
            if (packedRsxLayout is { } layout)
            {
                layout.Pack(
                    source.Vertices,
                    vertices.AsSpan(
                        vertexFloatOffset,
                        destinationVertexFloatCount));
            }
            else
            {
                source.Vertices.AsSpan().CopyTo(
                    vertices.AsSpan(
                        vertexFloatOffset,
                        source.Vertices.Length));
            }
            source.Indices.AsSpan().CopyTo(
                indices.AsSpan(indexOffset));
            placements[sourceIndex] =
                new MapRenderOpenGlWorldGeometryArenaPlacement(
                    source.MeshIndex,
                    checked((nuint)indexOffset * sizeof(uint)),
                    vertexFloatOffset / destinationFloatsPerVertex);
            vertexFloatOffset = checked(
                vertexFloatOffset + destinationVertexFloatCount);
            indexOffset = checked(indexOffset + source.Indices.Length);
        }

        return new MapRenderOpenGlWorldGeometryArenaPacking(
            vertices,
            indices,
            placements);
    }
}
