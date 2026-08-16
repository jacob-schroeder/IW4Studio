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
        var placementsByPayload = new Dictionary<
            (float[] Vertices, uint[] Indices),
            MapRenderOpenGlWorldGeometryArenaPlacement>();
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

            if (placementsByPayload.ContainsKey(
                    (source.Vertices, source.Indices)))
            {
                continue;
            }

            // Arrays from the immutable geometry pool are both immutable and
            // byte-identical when they are the same object. A placement is
            // therefore reusable only for this exact reference pair.
            placementsByPayload.Add(
                (source.Vertices, source.Indices),
                default);
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
        var packedPayloads = new HashSet<(float[] Vertices, uint[] Indices)>();
        for (int sourceIndex = 0;
             sourceIndex < sources.Count;
             sourceIndex++)
        {
            MapRenderOpenGlWorldGeometryArenaSource source =
                sources[sourceIndex];
            if (packedPayloads.Contains((source.Vertices, source.Indices)))
            {
                MapRenderOpenGlWorldGeometryArenaPlacement existingPlacement =
                    placementsByPayload[(source.Vertices, source.Indices)];
                placements[sourceIndex] = existingPlacement with
                {
                    MeshIndex = source.MeshIndex
                };
                continue;
            }

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
            var placement =
                new MapRenderOpenGlWorldGeometryArenaPlacement(
                    source.MeshIndex,
                    checked((nuint)indexOffset * sizeof(uint)),
                    vertexFloatOffset / destinationFloatsPerVertex);
            placements[sourceIndex] = placement;
            placementsByPayload[(source.Vertices, source.Indices)] =
                placement;
            packedPayloads.Add((source.Vertices, source.Indices));
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
