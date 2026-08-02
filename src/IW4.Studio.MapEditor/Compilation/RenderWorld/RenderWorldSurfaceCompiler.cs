using System.Buffers;
using IW4.Studio.MapEditor.Compilation.Collision;
using IW4.Studio.MapEditor.Editing.Identity;

namespace IW4.Studio.MapEditor.Compilation.RenderWorld;

internal sealed class RenderWorldSurfaceCompilation
{
    internal RenderWorldSurfaceCompilation(
        AuthoredIndexedRenderMeshSource[] orderedSources,
        byte[] packedPositionData,
        byte[] packedVertexLayerData,
        ushort[] indices,
        RenderWorldCompiledSurface[] surfaces,
        RenderWorldSourceSurfaceMapping[] sourceMappings)
    {
        OrderedSources = orderedSources;
        PackedPositionData = packedPositionData;
        PackedVertexLayerData = packedVertexLayerData;
        Indices = indices;
        Surfaces = surfaces;
        SourceMappings = sourceMappings;
    }

    internal AuthoredIndexedRenderMeshSource[] OrderedSources { get; }
    internal byte[] PackedPositionData { get; }
    internal byte[] PackedVertexLayerData { get; }
    internal ushort[] Indices { get; }
    internal RenderWorldCompiledSurface[] Surfaces { get; }
    internal RenderWorldSourceSurfaceMapping[] SourceMappings { get; }
}

/// <summary>
/// Deterministically splits canonical indexed sources into directly
/// UInt16-representable surface-local windows. Source enumeration order never
/// supplies an emitted ordinal.
/// </summary>
internal static class RenderWorldSurfaceCompiler
{
    internal static RenderWorldSurfaceCompilation Compile(
        IEnumerable<AuthoredIndexedRenderMeshSource> sources,
        CollisionInlineModelAllocationPlan inlineModelAllocationPlan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(inlineModelAllocationPlan);
        cancellationToken.ThrowIfCancellationRequested();

        AuthoredIndexedRenderMeshSource[] sourceCopy =
            sources.ToArray();
        if (sourceCopy.Length == 0)
        {
            throw new ArgumentException(
                "A render structural candidate requires at least one " +
                "canonical indexed source.",
                nameof(sources));
        }
        if (sourceCopy.Any(value => value is null))
        {
            throw new ArgumentException(
                "Render source collections cannot contain null rows.",
                nameof(sources));
        }

        IGrouping<MapObjectId, AuthoredIndexedRenderMeshSource>?
            duplicateSource = sourceCopy
                .GroupBy(value => value.ObjectId)
                .FirstOrDefault(group => group.Skip(1).Any());
        if (duplicateSource is not null)
        {
            throw new ArgumentException(
                $"Render source identity {duplicateSource.Key} occurs " +
                "more than once.",
                nameof(sources));
        }

        RequireSupportedInlineOwnership(
            sourceCopy,
            inlineModelAllocationPlan);
        AuthoredIndexedRenderMeshSource[] orderedSources =
            sourceCopy
                .OrderBy(value => value.Ownership.Kind)
                .ThenBy(value => ModelOrdinal(
                    value,
                    inlineModelAllocationPlan))
                .ThenBy(
                    value => value.SymbolicMaterialName,
                    StringComparer.Ordinal)
                .ThenBy(
                    value => StableKey(value.ObjectId),
                    StringComparer.Ordinal)
                .ToArray();

        var positions = new ArrayBufferWriter<byte>();
        var layers = new ArrayBufferWriter<byte>();
        var indices = new List<ushort>();
        var surfaces = new List<RenderWorldCompiledSurface>();
        var mappings =
            new List<RenderWorldSourceSurfaceMapping>(
                orderedSources.Length);

        foreach (AuthoredIndexedRenderMeshSource source in
                 orderedSources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CompileSource(
                source,
                ModelOrdinal(source, inlineModelAllocationPlan),
                positions,
                layers,
                indices,
                surfaces,
                mappings,
                cancellationToken);
        }

        return new RenderWorldSurfaceCompilation(
            orderedSources,
            positions.WrittenSpan.ToArray(),
            layers.WrittenSpan.ToArray(),
            indices.ToArray(),
            surfaces.ToArray(),
            mappings.ToArray());
    }

    private static void CompileSource(
        AuthoredIndexedRenderMeshSource source,
        ushort modelOrdinal,
        ArrayBufferWriter<byte> positions,
        ArrayBufferWriter<byte> layers,
        List<ushort> indices,
        List<RenderWorldCompiledSurface> surfaces,
        List<RenderWorldSourceSurfaceMapping> mappings,
        CancellationToken cancellationToken)
    {
        int firstSurface = surfaces.Count;
        int sourceSurfaceOrdinal = 0;
        int windowSourceTriangleStart = 0;
        var localIndexBySourceVertex = new Dictionary<int, ushort>();
        var sourceVertexByLocalIndex = new List<int>();
        var windowIndices = new List<ushort>();
        int windowTriangleCount = 0;

        for (int triangleOrdinal = 0;
             triangleOrdinal < source.Triangles.Count;
             triangleOrdinal++)
        {
            if ((triangleOrdinal & 0xFFF) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            AuthoredIndexedRenderTriangle triangle =
                source.Triangles[triangleOrdinal];
            int additionalVertices = CountAdditionalVertices(
                localIndexBySourceVertex,
                triangle);
            bool triangleLimitReached =
                windowTriangleCount >=
                RenderWorldStructuralProfile
                    .MaximumTrianglesPerSurface;
            bool vertexLimitExceeded =
                localIndexBySourceVertex.Count + additionalVertices >
                RenderWorldStructuralProfile
                    .MaximumVerticesPerSurface;
            if (windowTriangleCount != 0 &&
                (triangleLimitReached || vertexLimitExceeded))
            {
                FlushWindow();
                windowSourceTriangleStart = triangleOrdinal;
            }

            AddIndex(triangle.Index0);
            AddIndex(triangle.Index1);
            AddIndex(triangle.Index2);
            windowTriangleCount = checked(windowTriangleCount + 1);
        }

        FlushWindow();
        int surfaceCount = checked(surfaces.Count - firstSurface);
        if (surfaceCount <= 0)
        {
            throw new InvalidDataException(
                $"Render source {source.ObjectId} produced no structural " +
                "surface.");
        }

        mappings.Add(new RenderWorldSourceSurfaceMapping(
            source.ObjectId,
            source.Ownership.Kind,
            source.Ownership.InlineBrushModelObjectId,
            modelOrdinal,
            source.SymbolicMaterialName,
            source.TriangleWinding,
            new RenderWorldRange(firstSurface, surfaceCount),
            RenderWorldSourceBounds.From(source.Vertices)));
        return;

        void AddIndex(int sourceVertexIndex)
        {
            if (!localIndexBySourceVertex.TryGetValue(
                    sourceVertexIndex,
                    out ushort localIndex))
            {
                if (sourceVertexByLocalIndex.Count >=
                    RenderWorldStructuralProfile
                        .MaximumVerticesPerSurface)
                {
                    throw new InvalidDataException(
                        "A render surface exceeded the bounded UInt16 " +
                        "vertex window.");
                }

                localIndex = checked(
                    (ushort)sourceVertexByLocalIndex.Count);
                localIndexBySourceVertex.Add(
                    sourceVertexIndex,
                    localIndex);
                sourceVertexByLocalIndex.Add(sourceVertexIndex);
            }

            windowIndices.Add(localIndex);
        }

        void FlushWindow()
        {
            if (windowTriangleCount == 0)
                return;
            if (surfaces.Count >=
                RenderWorldStructuralProfile.MaximumSurfaceCount)
            {
                throw new NotSupportedException(
                    "The bounded render compiler cannot produce more than " +
                    "65,536 UInt16-addressable surface ordinals.");
            }

            int vertexCount = sourceVertexByLocalIndex.Count;
            if (vertexCount is <= 0 or >
                RenderWorldStructuralProfile
                    .MaximumVerticesPerSurface)
            {
                throw new InvalidDataException(
                    "A render window has an invalid local vertex count.");
            }
            if (windowIndices.Count !=
                checked(windowTriangleCount * 3))
            {
                throw new InvalidDataException(
                    "A render window has an inconsistent triangle index " +
                    "count.");
            }

            int baseVertex = checked(
                positions.WrittenCount /
                RenderWorldStructuralProfile.PositionStride);
            int vertexLayerData = layers.WrittenCount;
            int baseIndex = indices.Count;
            foreach (int sourceVertexIndex in sourceVertexByLocalIndex)
            {
                AuthoredRenderVertex vertex =
                    source.Vertices[sourceVertexIndex];
                Span<byte> positionRow = positions.GetSpan(
                    RenderWorldStructuralProfile.PositionStride);
                RenderWorldVertexPacker.WritePositionRow(
                    vertex,
                    positionRow);
                positions.Advance(
                    RenderWorldStructuralProfile.PositionStride);

                Span<byte> layerRow = layers.GetSpan(
                    RenderWorldStructuralProfile.VertexLayerStride);
                RenderWorldVertexPacker.WriteVertexLayerRow(
                    vertex,
                    layerRow);
                layers.Advance(
                    RenderWorldStructuralProfile.VertexLayerStride);
            }

            _ = checked(indices.Count + windowIndices.Count);
            indices.AddRange(windowIndices);
            surfaces.Add(new RenderWorldCompiledSurface(
                surfaces.Count,
                source.ObjectId,
                sourceSurfaceOrdinal,
                source.Ownership.Kind,
                source.Ownership.InlineBrushModelObjectId,
                modelOrdinal,
                source.SymbolicMaterialName,
                source.TriangleWinding,
                new RenderWorldRange(baseVertex, vertexCount),
                new RenderWorldRange(
                    vertexLayerData,
                    checked(
                        vertexCount *
                        RenderWorldStructuralProfile.VertexLayerStride)),
                new RenderWorldRange(
                    baseIndex,
                    windowIndices.Count),
                windowTriangleCount,
                sourceVertexByLocalIndex,
                new RenderWorldRange(
                    windowSourceTriangleStart,
                    windowTriangleCount)));
            sourceSurfaceOrdinal = checked(sourceSurfaceOrdinal + 1);

            localIndexBySourceVertex.Clear();
            sourceVertexByLocalIndex.Clear();
            windowIndices.Clear();
            windowTriangleCount = 0;
        }
    }

    private static int CountAdditionalVertices(
        IReadOnlyDictionary<int, ushort> localIndexBySourceVertex,
        AuthoredIndexedRenderTriangle triangle)
    {
        int count = 0;
        if (!localIndexBySourceVertex.ContainsKey(triangle.Index0))
            count++;
        if (!localIndexBySourceVertex.ContainsKey(triangle.Index1))
            count++;
        if (!localIndexBySourceVertex.ContainsKey(triangle.Index2))
            count++;
        return count;
    }

    private static void RequireSupportedInlineOwnership(
        IReadOnlyList<AuthoredIndexedRenderMeshSource> sources,
        CollisionInlineModelAllocationPlan allocationPlan)
    {
        CollisionInlineModelAllocation? dynamicOwner =
            allocationPlan.Rows.FirstOrDefault(value =>
                value.OwnerKind ==
                CollisionInlineModelOwnerKind.DynamicBrushDefinition);
        if (dynamicOwner is not null)
        {
            throw new NotSupportedException(
                "The bounded M3 render compiler does not admit dynamic " +
                $"brush-model allocation {dynamicOwner.ModelOrdinal}; " +
                "only world and MapEnt rows are structurally compiled.");
        }

        foreach (AuthoredIndexedRenderMeshSource source in sources)
        {
            if (source.Ownership.Kind !=
                RenderMeshOwnershipKind.InlineBrushModel)
            {
                continue;
            }

            MapObjectId owner =
                source.Ownership.InlineBrushModelObjectId!.Value;
            CollisionInlineModelAllocation allocation;
            try
            {
                allocation = allocationPlan.GetRequiredOwner(owner);
            }
            catch (KeyNotFoundException exception)
            {
                throw new InvalidDataException(
                    $"Inline render source {source.ObjectId} owner " +
                    $"{owner} has no shared model allocation.",
                    exception);
            }
            if (allocation.OwnerKind !=
                    CollisionInlineModelOwnerKind.MapEntityBrushModel ||
                allocation.OwnerObjectId != owner ||
                allocation.ModelOrdinal == 0)
            {
                throw new InvalidDataException(
                    $"Inline render source {source.ObjectId} owner " +
                    $"{owner} is not a valid shared MapEnt allocation.");
            }
        }
    }

    private static ushort ModelOrdinal(
        AuthoredIndexedRenderMeshSource source,
        CollisionInlineModelAllocationPlan allocationPlan) =>
        source.Ownership.Kind == RenderMeshOwnershipKind.StandaloneWorld
            ? (ushort)0
            : allocationPlan.GetRequiredOwner(
                    source.Ownership.InlineBrushModelObjectId!.Value)
                .ModelOrdinal;

    private static string StableKey(MapObjectId value) =>
        value.Value.ToString("D");
}
