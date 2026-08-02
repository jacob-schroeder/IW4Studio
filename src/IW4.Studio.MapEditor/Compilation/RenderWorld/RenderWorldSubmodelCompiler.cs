using IW4.Studio.MapEditor.Compilation.Collision;
using IW4.Studio.MapEditor.Editing.Identity;

namespace IW4.Studio.MapEditor.Compilation.RenderWorld;

internal sealed class RenderWorldSubmodelPlan
{
    internal RenderWorldSubmodelPlan(
        RenderWorldRange standaloneWorldSurfaceRange,
        ushort[] sortedWorldSurfaceOrdinals,
        RenderWorldWorldModelSurfaceRange worldModel,
        RenderWorldInlineModelSurfaceRange[] inlineModels)
    {
        StandaloneWorldSurfaceRange = standaloneWorldSurfaceRange;
        SortedWorldSurfaceOrdinals = sortedWorldSurfaceOrdinals;
        WorldModel = worldModel;
        InlineModels = inlineModels;
    }

    internal RenderWorldRange StandaloneWorldSurfaceRange { get; }
    internal ushort[] SortedWorldSurfaceOrdinals { get; }
    internal RenderWorldWorldModelSurfaceRange WorldModel { get; }
    internal RenderWorldInlineModelSurfaceRange[] InlineModels { get; }
}

/// <summary>
/// Projects symbolic-material-ordered surfaces into GfxBrushModel-shaped
/// world and MapEnt rows using the shared Col/Gfx allocation plan. Empty
/// MapEnt rows remain explicit; dynamic brush definitions are outside the
/// bounded M3 render profile.
/// </summary>
internal static class RenderWorldSubmodelCompiler
{
    internal static RenderWorldSubmodelPlan Compile(
        IReadOnlyList<RenderWorldCompiledSurface> surfaces,
        IReadOnlyList<RenderWorldSourceSurfaceMapping> sourceMappings,
        CollisionInlineModelAllocationPlan allocationPlan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(surfaces);
        ArgumentNullException.ThrowIfNull(sourceMappings);
        ArgumentNullException.ThrowIfNull(allocationPlan);
        cancellationToken.ThrowIfCancellationRequested();

        RenderWorldSourceSurfaceMapping[] worldMappings =
            sourceMappings
                .Where(value =>
                    value.OwnershipKind ==
                    RenderMeshOwnershipKind.StandaloneWorld)
                .ToArray();
        int worldSurfaceCount = worldMappings.Sum(
            value => value.SurfaceRange.Count);
        if (worldSurfaceCount > ushort.MaxValue)
        {
            throw new NotSupportedException(
                "The standalone world-model surface prefix exceeds the " +
                "UInt16 GfxBrushModel surface-count field.");
        }

        var worldRange = new RenderWorldRange(
            start: 0,
            count: worldSurfaceCount);
        RequireContiguousMappings(
            worldMappings,
            expectedStart: 0,
            ownerDescription: "world model");
        RenderWorldSourceBounds worldBounds =
            UnionBoundsOrLocalOrigin(worldMappings);
        var worldModel = new RenderWorldWorldModelSurfaceRange(
            worldRange,
            worldBounds);
        ushort[] sortedWorldOrdinals = surfaces
            .Where(value => value.ModelOrdinal == 0)
            .OrderBy(
                value => value.SymbolicMaterialName,
                StringComparer.Ordinal)
            .ThenBy(
                value => StableKey(value.SourceObjectId),
                StringComparer.Ordinal)
            .ThenBy(value => value.SourceSurfaceOrdinal)
            .Select(value => checked((ushort)value.Ordinal))
            .ToArray();

        var inlineModels =
            new List<RenderWorldInlineModelSurfaceRange>(
                allocationPlan.ModelCount - 1);
        int expectedStart = worldRange.EndExclusive;
        foreach (CollisionInlineModelAllocation allocation in
                 allocationPlan.Rows.Skip(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (allocation.OwnerKind ==
                CollisionInlineModelOwnerKind.DynamicBrushDefinition)
            {
                throw new NotSupportedException(
                    "The bounded M3 render compiler cannot emit dynamic " +
                    $"brush-model row {allocation.ModelOrdinal}.");
            }
            if (allocation.OwnerKind !=
                    CollisionInlineModelOwnerKind.MapEntityBrushModel ||
                allocation.OwnerObjectId is not { } ownerObjectId ||
                allocation.ModelOrdinal == 0)
            {
                throw new InvalidDataException(
                    $"Shared model row {allocation.ModelOrdinal} is not " +
                    "a valid MapEnt allocation.");
            }

            RenderWorldSourceSurfaceMapping[] mappings =
                sourceMappings
                    .Where(value =>
                        value.OwnershipKind ==
                            RenderMeshOwnershipKind.InlineBrushModel &&
                        value.InlineBrushModelObjectId == ownerObjectId)
                    .OrderBy(
                        value => value.SymbolicMaterialName,
                        StringComparer.Ordinal)
                    .ThenBy(
                        value => StableKey(value.SourceObjectId),
                        StringComparer.Ordinal)
                    .ToArray();
            RequireContiguousMappings(
                mappings,
                expectedStart,
                $"inline model {ownerObjectId}");
            int surfaceCount = mappings.Sum(
                value => value.SurfaceRange.Count);
            if (expectedStart > ushort.MaxValue ||
                surfaceCount > ushort.MaxValue)
            {
                throw new NotSupportedException(
                    $"Inline render model {ownerObjectId} cannot fit the " +
                    "UInt16 GfxBrushModel surface range.");
            }
            if (mappings.Any(value =>
                    value.ModelOrdinal != allocation.ModelOrdinal))
            {
                throw new InvalidDataException(
                    $"Inline render model {ownerObjectId} contradicts " +
                    $"shared model ordinal {allocation.ModelOrdinal}.");
            }

            var range = new RenderWorldRange(
                expectedStart,
                surfaceCount);
            inlineModels.Add(
                new RenderWorldInlineModelSurfaceRange(
                    allocation.ModelOrdinal,
                    ownerObjectId,
                    range,
                    mappings.Select(value => value.SourceObjectId),
                    UnionBoundsOrLocalOrigin(mappings)));
            expectedStart = range.EndExclusive;
        }

        MapObjectId[] unallocatedOwners = sourceMappings
            .Where(value =>
                value.OwnershipKind ==
                RenderMeshOwnershipKind.InlineBrushModel)
            .Select(value => value.InlineBrushModelObjectId!.Value)
            .Distinct()
            .Where(owner =>
                !inlineModels.Any(value =>
                    value.InlineBrushModelObjectId == owner))
            .ToArray();
        if (unallocatedOwners.Length != 0)
        {
            throw new InvalidDataException(
                $"Inline render owner {unallocatedOwners[0]} is absent from " +
                "the shared MapEnt model plan.");
        }
        if (expectedStart != surfaces.Count)
        {
            throw new InvalidDataException(
                "World and inline render-model ranges do not cover the " +
                "complete surface domain.");
        }

        return new RenderWorldSubmodelPlan(
            worldRange,
            sortedWorldOrdinals,
            worldModel,
            inlineModels.ToArray());
    }

    private static void RequireContiguousMappings(
        IReadOnlyList<RenderWorldSourceSurfaceMapping> mappings,
        int expectedStart,
        string ownerDescription)
    {
        int cursor = expectedStart;
        foreach (RenderWorldSourceSurfaceMapping mapping in mappings)
        {
            if (mapping.SurfaceRange.Start != cursor)
            {
                throw new InvalidDataException(
                    $"The {ownerDescription} source mappings are not " +
                    $"contiguous at surface {cursor}.");
            }
            cursor = mapping.SurfaceRange.EndExclusive;
        }
    }

    private static RenderWorldSourceBounds UnionBoundsOrLocalOrigin(
        IReadOnlyList<RenderWorldSourceSurfaceMapping> mappings)
    {
        if (mappings.Count == 0)
            return RenderWorldSourceBounds.EmptyAtLocalOrigin;

        RenderWorldSourceBounds bounds = mappings[0].SourceBounds;
        for (int index = 1; index < mappings.Count; index++)
            bounds = bounds.Include(mappings[index].SourceBounds);
        return bounds;
    }

    private static string StableKey(MapObjectId value) =>
        value.Value.ToString("D");
}
