using System.Collections.ObjectModel;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.Physics;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;
using AssetVector3 = IW4.Assets.Math.Vec3;

namespace IW4.Studio.MapEditor.Compilation.Collision;

/// <summary>
/// Explicit semantic input that binds one MapEnt brush-model owner to its
/// physical entity-string ordinal. The row deliberately performs no eager
/// validation so malformed external input can be reported as structured,
/// fail-closed assessment issues before any inline-model rows are allocated.
/// </summary>
public readonly record struct MapEntBrushModelAllocationSource
{
    public MapEntBrushModelAllocationSource(
        MapObjectId mapEntityObjectId,
        int physicalEntityOrdinal)
    {
        MapEntityObjectId = mapEntityObjectId;
        PhysicalEntityOrdinal = physicalEntityOrdinal;
    }

    public MapObjectId MapEntityObjectId { get; }
    public int PhysicalEntityOrdinal { get; }
}

public enum CollisionMapEntBrushModelAllocationIssueKind
{
    MissingOwnerMapping = 0,
    DuplicateOwnerMapping = 1,
    InvalidOwnerMapping = 2
}

public sealed record CollisionMapEntBrushModelAllocationIssue(
    CollisionMapEntBrushModelAllocationIssueKind Kind,
    MapObjectId? MapEntityObjectId,
    int? PhysicalEntityOrdinal,
    string Detail);

/// <summary>
/// Immutable validation result for the authored MapEnt brush-model allocation
/// input. A valid result proves only one-to-one semantic ownership and
/// physical-order authority; it grants no emitter or persistence authority.
/// </summary>
public sealed class CollisionMapEntBrushModelAllocationAssessment
{
    private readonly IReadOnlyList<MapEntBrushModelAllocationSource>
        _sources;
    private readonly IReadOnlyList<
        CollisionMapEntBrushModelAllocationIssue> _issues;

    internal CollisionMapEntBrushModelAllocationAssessment(
        IEnumerable<MapEntBrushModelAllocationSource> sources,
        IEnumerable<CollisionMapEntBrushModelAllocationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(issues);

        _sources = new ReadOnlyCollection<
            MapEntBrushModelAllocationSource>(
                sources
                    .OrderBy(value => value.PhysicalEntityOrdinal)
                    .ThenBy(
                        value => value.MapEntityObjectId.Value.ToString("N"),
                        StringComparer.Ordinal)
                    .ToArray());
        _issues = new ReadOnlyCollection<
            CollisionMapEntBrushModelAllocationIssue>(
                issues
                    .OrderBy(value => value.Kind)
                    .ThenBy(value =>
                        value.MapEntityObjectId?.Value.ToString("N") ??
                        string.Empty,
                        StringComparer.Ordinal)
                    .ThenBy(value => value.PhysicalEntityOrdinal)
                    .ThenBy(value => value.Detail, StringComparer.Ordinal)
                    .ToArray());
    }

    public IReadOnlyList<MapEntBrushModelAllocationSource> Sources =>
        _sources;

    public IReadOnlyList<CollisionMapEntBrushModelAllocationIssue> Issues =>
        _issues;

    public bool IsValid => _issues.Count == 0;
}

/// <summary>
/// Structured fail-closed rejection raised before an invalid MapEnt owner map
/// can reach the shared inline-model allocator.
/// </summary>
public sealed class CollisionMapEntBrushModelAllocationException
    : InvalidOperationException
{
    public CollisionMapEntBrushModelAllocationException(
        CollisionMapEntBrushModelAllocationAssessment assessment)
        : base(CreateMessage(assessment))
    {
        Assessment = assessment ??
            throw new ArgumentNullException(nameof(assessment));
        if (assessment.IsValid)
        {
            throw new ArgumentException(
                "A valid MapEnt brush-model allocation assessment cannot " +
                "produce a rejection.",
                nameof(assessment));
        }
    }

    public CollisionMapEntBrushModelAllocationAssessment Assessment
    {
        get;
    }

    private static string CreateMessage(
        CollisionMapEntBrushModelAllocationAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        return "MapEnt brush-model allocation input is invalid: " +
            string.Join(
                "; ",
                assessment.Issues.Select(value =>
                    $"{value.Kind}: {value.Detail}"));
    }
}

/// <summary>
/// Validates the complete owner inventory independently from source
/// enumeration order. Physical MapEnt ordinals are the sole model-order
/// authority; no geometry, name, bounds, GUID, or authored-source order is
/// used to infer them.
/// </summary>
public static class CollisionMapEntBrushModelAllocationValidator
{
    public static CollisionMapEntBrushModelAllocationAssessment Assess(
        IEnumerable<AuthoredCollisionSource> authoredSources,
        IEnumerable<MapEntBrushModelAllocationSource> allocationSources)
    {
        ArgumentNullException.ThrowIfNull(authoredSources);
        ArgumentNullException.ThrowIfNull(allocationSources);

        AuthoredCollisionSource[] authoredCopy =
            authoredSources.ToArray();
        if (authoredCopy.Any(value => value is null))
        {
            throw new ArgumentException(
                "Authored collision sources cannot contain null entries.",
                nameof(authoredSources));
        }
        MapEntBrushModelAllocationSource[] allocationCopy =
            allocationSources.ToArray();
        MapObjectId[] requiredOwners = authoredCopy
            .OfType<AuthoredConvexBrushCollisionSource>()
            .Where(value =>
                value.Ownership.Category ==
                CollisionOwnershipCategory.BrushModelEntity)
            .Select(value =>
                value.Ownership.Counterpart!.Value.ObjectId)
            .Distinct()
            .OrderBy(
                value => value.Value.ToString("N"),
                StringComparer.Ordinal)
            .ToArray();
        var requiredOwnerSet = requiredOwners.ToHashSet();
        var issues =
            new List<CollisionMapEntBrushModelAllocationIssue>();

        foreach (MapObjectId owner in requiredOwners)
        {
            MapEntBrushModelAllocationSource[] matches = allocationCopy
                .Where(value => value.MapEntityObjectId == owner)
                .ToArray();
            if (matches.Length == 0)
            {
                issues.Add(
                    new CollisionMapEntBrushModelAllocationIssue(
                        CollisionMapEntBrushModelAllocationIssueKind
                            .MissingOwnerMapping,
                        owner,
                        PhysicalEntityOrdinal: null,
                        $"Brush-model owner {owner} has no explicit " +
                        "physical MapEnt entity ordinal."));
            }
            else if (matches.Length > 1)
            {
                issues.Add(
                    new CollisionMapEntBrushModelAllocationIssue(
                        CollisionMapEntBrushModelAllocationIssueKind
                            .DuplicateOwnerMapping,
                        owner,
                        PhysicalEntityOrdinal: null,
                        $"Brush-model owner {owner} is mapped " +
                        $"{matches.Length} times; exactly one physical " +
                        "MapEnt entity ordinal is required."));
            }
        }

        foreach (IGrouping<MapObjectId, MapEntBrushModelAllocationSource>
                 group in allocationCopy
                     .GroupBy(value => value.MapEntityObjectId)
                     .OrderBy(
                         value => value.Key.Value.ToString("N"),
                         StringComparer.Ordinal))
        {
            if (group.Key.Value == Guid.Empty)
            {
                foreach (MapEntBrushModelAllocationSource value in group)
                {
                    issues.Add(
                        Invalid(
                            value,
                            "A MapEnt brush-model allocation row has an " +
                            "empty owner identity."));
                }
                continue;
            }
            if (!requiredOwnerSet.Contains(group.Key))
            {
                foreach (MapEntBrushModelAllocationSource value in group)
                {
                    issues.Add(
                        Invalid(
                            value,
                            $"MapEnt owner {group.Key} has no authored " +
                            "BrushModelEntity convex brush."));
                }
            }
        }

        foreach (MapEntBrushModelAllocationSource value in allocationCopy
                     .Where(value => value.PhysicalEntityOrdinal < 0))
        {
            issues.Add(
                Invalid(
                    value,
                    $"Physical MapEnt entity ordinal " +
                    $"{value.PhysicalEntityOrdinal} is negative."));
        }

        MapEntBrushModelAllocationSource[] uniqueValidKnownRows =
            allocationCopy
                .Where(value =>
                    value.MapEntityObjectId.Value != Guid.Empty &&
                    value.PhysicalEntityOrdinal >= 0 &&
                    requiredOwnerSet.Contains(value.MapEntityObjectId))
                .GroupBy(value => value.MapEntityObjectId)
                .Where(value => value.Count() == 1)
                .Select(value => value.Single())
                .ToArray();
        foreach (IGrouping<int, MapEntBrushModelAllocationSource> group in
                 uniqueValidKnownRows
                     .GroupBy(value => value.PhysicalEntityOrdinal)
                     .Where(value => value.Count() > 1)
                     .OrderBy(value => value.Key))
        {
            MapObjectId[] owners = group
                .Select(value => value.MapEntityObjectId)
                .OrderBy(
                    value => value.Value.ToString("N"),
                    StringComparer.Ordinal)
                .ToArray();
            issues.Add(
                new CollisionMapEntBrushModelAllocationIssue(
                    CollisionMapEntBrushModelAllocationIssueKind
                        .InvalidOwnerMapping,
                    MapEntityObjectId: null,
                    group.Key,
                    $"Physical MapEnt entity ordinal {group.Key} is claimed " +
                    $"by multiple brush-model owners: " +
                    $"{string.Join(", ", owners)}."));
        }

        return new CollisionMapEntBrushModelAllocationAssessment(
            allocationCopy,
            issues);
    }

    private static CollisionMapEntBrushModelAllocationIssue Invalid(
        MapEntBrushModelAllocationSource value,
        string detail) =>
        new(
            CollisionMapEntBrushModelAllocationIssueKind.InvalidOwnerMapping,
            value.MapEntityObjectId.Value == Guid.Empty
                ? null
                : value.MapEntityObjectId,
            value.PhysicalEntityOrdinal,
            detail);
}

internal readonly record struct CollisionMapEntBrushModelSpatialInput
{
    public CollisionMapEntBrushModelSpatialInput(
        MapObjectId mapEntityObjectId,
        MapObjectId sourceObjectId,
        ushort brushOrdinal,
        MapBounds bounds,
        uint contents)
    {
        if (mapEntityObjectId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(mapEntityObjectId));
        if (sourceObjectId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(sourceObjectId));
        if (mapEntityObjectId == sourceObjectId)
        {
            throw new ArgumentException(
                "A brush-model collision source cannot be its own MapEnt " +
                "owner.",
                nameof(mapEntityObjectId));
        }
        if (!bounds.IsFinite ||
            bounds.HalfSize.X < 0 ||
            bounds.HalfSize.Y < 0 ||
            bounds.HalfSize.Z < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bounds));
        }

        MapEntityObjectId = mapEntityObjectId;
        SourceObjectId = sourceObjectId;
        BrushOrdinal = brushOrdinal;
        Bounds = bounds;
        Contents = contents;
    }

    public MapObjectId MapEntityObjectId { get; }
    public MapObjectId SourceObjectId { get; }
    public ushort BrushOrdinal { get; }
    public MapBounds Bounds { get; }
    public uint Contents { get; }
}

/// <summary>
/// Extends the conservative world payload with one CModel leaf per validated
/// MapEnt owner. Every global brush ordinal is reachable exactly once from
/// either the world leaf-brush node or its explicitly allocated submodel.
/// </summary>
internal static class CollisionMapEntBrushModelSpatialCompiler
{
    private const float LeafBrushBoundsExpansion = 0.125f;
    private const float CollisionModelBoundsExpansion = 1f;

    public static CollisionCompiledConservativeWorldSpatialPayload Extend(
        CollisionCompiledConservativeWorldSpatialPayload world,
        CollisionInlineModelAllocationPlan inlineModelPlan,
        IEnumerable<CollisionMapEntBrushModelSpatialInput>
            submodelBrushes,
        int totalBrushCount)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(inlineModelPlan);
        ArgumentNullException.ThrowIfNull(submodelBrushes);
        if (totalBrushCount < 0 ||
            totalBrushCount > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(totalBrushCount));
        }
        if (world.CollisionModels.Count != 1 ||
            inlineModelPlan.Rows.Count == 0 ||
            inlineModelPlan.Rows[0].OwnerKind !=
                CollisionInlineModelOwnerKind.World ||
            inlineModelPlan.Rows[0].ModelOrdinal != 0)
        {
            throw new InvalidDataException(
                "Brush-model submodel compilation requires one conservative " +
                "world cmodel at inline-model row 0.");
        }
        if (inlineModelPlan.Rows.Any(value =>
                value.OwnerKind ==
                CollisionInlineModelOwnerKind.DynamicBrushDefinition))
        {
            throw new NotSupportedException(
                "The M3 MapEnt brush-model seam does not compile dynamic " +
                "brush-definition submodels.");
        }

        CollisionMapEntBrushModelSpatialInput[] submodelCopy =
            submodelBrushes
                .OrderBy(value => value.BrushOrdinal)
                .ThenBy(
                    value => value.SourceObjectId.Value.ToString("N"),
                    StringComparer.Ordinal)
                .ToArray();
        ushort? duplicateBrushOrdinal = submodelCopy
            .GroupBy(value => value.BrushOrdinal)
            .FirstOrDefault(value => value.Count() > 1)
            ?.Key;
        if (duplicateBrushOrdinal is not null)
        {
            throw new InvalidDataException(
                $"Brush ordinal {duplicateBrushOrdinal} is assigned to " +
                "multiple MapEnt submodels.");
        }

        var brushesByOwner = submodelCopy
            .GroupBy(value => value.MapEntityObjectId)
            .ToDictionary(
                value => value.Key,
                value => value
                    .OrderBy(brush => brush.BrushOrdinal)
                    .ToArray());
        CollisionInlineModelAllocation[] modelAllocations =
            inlineModelPlan.Rows
                .Where(value =>
                    value.OwnerKind ==
                    CollisionInlineModelOwnerKind.MapEntityBrushModel)
                .OrderBy(value => value.ModelOrdinal)
                .ToArray();
        foreach (CollisionInlineModelAllocation allocation in
                 modelAllocations)
        {
            MapObjectId owner = allocation.OwnerObjectId!.Value;
            if (!brushesByOwner.ContainsKey(owner))
            {
                throw new InvalidDataException(
                    $"Allocated MapEnt submodel owner {owner} has no " +
                    "authored convex brush payload.");
            }
        }
        MapObjectId? unallocatedOwner = brushesByOwner.Keys
            .FirstOrDefault(owner =>
                modelAllocations.All(value =>
                    value.OwnerObjectId != owner));
        if (unallocatedOwner is { } missingOwner &&
            missingOwner.Value != Guid.Empty)
        {
            throw new InvalidDataException(
                $"Authored MapEnt brush-model owner {missingOwner} has no " +
                "inline-model allocation.");
        }

        ushort[] reachableBrushes =
        [
            .. world.LeafBrushReferences,
            .. submodelCopy.Select(value => value.BrushOrdinal)
        ];
        ushort[] expectedBrushes = Enumerable
            .Range(0, totalBrushCount)
            .Select(value => checked((ushort)value))
            .ToArray();
        if (!reachableBrushes.Order().SequenceEqual(expectedBrushes))
        {
            throw new InvalidDataException(
                "World and MapEnt submodel leaf-brush ownership does not " +
                "cover every final Brush-domain ordinal exactly once.");
        }

        var leafBrushNodes =
            new List<CLeafBrushNode>(world.LeafBrushNodes);
        var leafBrushReferences =
            new List<ushort>(world.LeafBrushReferences);
        var collisionModels =
            new List<CModel>(world.CollisionModels);

        foreach (CollisionInlineModelAllocation allocation in
                 modelAllocations)
        {
            if (allocation.ModelOrdinal != collisionModels.Count)
            {
                throw new InvalidDataException(
                    "MapEnt cmodel construction lost physical-entity " +
                    "allocation order.");
            }

            MapObjectId owner = allocation.OwnerObjectId!.Value;
            CollisionMapEntBrushModelSpatialInput[] ownerBrushes =
                brushesByOwner[owner];
            if (ownerBrushes.Length > short.MaxValue)
            {
                throw new OverflowException(
                    $"MapEnt brush-model owner {owner} has " +
                    $"{ownerBrushes.Length} brushes, but one positive " +
                    "leaf-brush node supports at most 32,767 references.");
            }

            ushort[] ordinals =
                ownerBrushes.Select(value => value.BrushOrdinal).ToArray();
            uint contents = ownerBrushes.Aggregate(
                0u,
                (current, value) => current | value.Contents);
            MapBounds brushBounds = CollisionOutwardBounds.Include(
                ownerBrushes.Select(value => value.Bounds));
            MapBounds leafBounds = CollisionOutwardBounds.Expand(
                brushBounds,
                LeafBrushBoundsExpansion);
            int leafBrushNodeOrdinal = leafBrushNodes.Count;
            leafBrushNodes.Add(
                new CLeafBrushNode
                {
                    Axis = 0,
                    LeafBrushCount = checked((short)ordinals.Length),
                    Contents = unchecked((int)contents),
                    Data = new CLeafBrushNodeData
                    {
                        Brushes = Array.AsReadOnly(ordinals),
                        LeafUnionPad =
                            Array.AsReadOnly(new byte[8])
                    }
                });
            leafBrushReferences.AddRange(ordinals);

            MapBounds modelBounds = CollisionOutwardBounds.Expand(
                brushBounds,
                CollisionModelBoundsExpansion);
            collisionModels.Add(
                new CModel
                {
                    Mins = ToAsset(
                        CollisionOutwardBounds.Minimum(modelBounds)),
                    Maxs = ToAsset(
                        CollisionOutwardBounds.Maximum(modelBounds)),
                    Radius = RadiusFromOrigin(modelBounds),
                    Leaf = new CLeaf
                    {
                        BrushContents = unchecked((int)contents),
                        TerrainContents = 0,
                        Mins = ToAsset(
                            CollisionOutwardBounds.Minimum(leafBounds)),
                        Maxs = ToAsset(
                            CollisionOutwardBounds.Maximum(leafBounds)),
                        LeafBrushNode = leafBrushNodeOrdinal
                    }
                });
        }

        if (collisionModels.Count != inlineModelPlan.ModelCount ||
            leafBrushReferences.Count != totalBrushCount)
        {
            throw new InvalidDataException(
                "MapEnt submodel construction did not match its inline-model " +
                "or final Brush-domain cardinality.");
        }

        return new CollisionCompiledConservativeWorldSpatialPayload(
            world.WorldBounds,
            world.BspPlanes,
            world.Nodes,
            world.Leaves,
            leafBrushNodes,
            leafBrushReferences,
            world.CollisionAabbNodes,
            collisionModels);
    }

    private static AssetVector3 ToAsset(MapVector3 value) =>
        new() { X = value.X, Y = value.Y, Z = value.Z };

    private static float RadiusFromOrigin(MapBounds bounds)
    {
        MapVector3 minimum = CollisionOutwardBounds.Minimum(bounds);
        MapVector3 maximum = CollisionOutwardBounds.Maximum(bounds);
        double x = Math.Max(Math.Abs(minimum.X), Math.Abs(maximum.X));
        double y = Math.Max(Math.Abs(minimum.Y), Math.Abs(maximum.Y));
        double z = Math.Max(Math.Abs(minimum.Z), Math.Abs(maximum.Z));
        double radius = Math.Sqrt(x * x + y * y + z * z);
        if (!double.IsFinite(radius) || radius > float.MaxValue)
        {
            throw new OverflowException(
                "Collision-model radius exceeds the finite float range.");
        }
        float compiled = (float)radius;
        if ((double)compiled < radius)
            compiled = MathF.BitIncrement(compiled);
        if (!float.IsFinite(compiled))
        {
            throw new OverflowException(
                "Collision-model radius cannot be rounded outward to a " +
                "finite float.");
        }
        return compiled;
    }
}
