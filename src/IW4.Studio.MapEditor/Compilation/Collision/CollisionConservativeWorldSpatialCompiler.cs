using System.Collections.ObjectModel;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.Physics;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;
using AssetVector3 = IW4.Assets.Math.Vec3;

namespace IW4.Studio.MapEditor.Compilation.Collision;

/// <summary>
/// One compiled brush made available to the world spatial builder. Ordinals
/// are final root Brush-domain ordinals, not source or imported ordinals.
/// </summary>
public sealed record CollisionWorldBrushSpatialInput
{
    public CollisionWorldBrushSpatialInput(
        MapObjectId sourceObjectId,
        ushort brushOrdinal,
        MapBounds bounds,
        uint contents)
    {
        if (sourceObjectId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(sourceObjectId));
        ValidateBounds(bounds, nameof(bounds));

        SourceObjectId = sourceObjectId;
        BrushOrdinal = brushOrdinal;
        Bounds = bounds;
        Contents = contents;
    }

    public MapObjectId SourceObjectId { get; }
    public ushort BrushOrdinal { get; }
    public MapBounds Bounds { get; }
    public uint Contents { get; }

    private static void ValidateBounds(MapBounds value, string parameterName)
    {
        if (!value.IsFinite ||
            value.HalfSize.X < 0 ||
            value.HalfSize.Y < 0 ||
            value.HalfSize.Z < 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "World brush bounds must be finite and non-negative.");
        }
    }
}

/// <summary>
/// One final triangle partition and the semantic data needed to create its
/// collision-AABB leaf.
/// </summary>
public sealed record CollisionWorldPartitionSpatialInput
{
    public CollisionWorldPartitionSpatialInput(
        int partitionOrdinal,
        ushort materialOrdinal,
        int materialContents,
        MapBounds bounds)
    {
        if (partitionOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(partitionOrdinal));
        if (!bounds.IsFinite ||
            bounds.HalfSize.X < 0 ||
            bounds.HalfSize.Y < 0 ||
            bounds.HalfSize.Z < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bounds),
                "Partition bounds must be finite and non-negative.");
        }

        PartitionOrdinal = partitionOrdinal;
        MaterialOrdinal = materialOrdinal;
        MaterialContents = materialContents;
        Bounds = bounds;
    }

    public int PartitionOrdinal { get; }
    public ushort MaterialOrdinal { get; }
    public int MaterialContents { get; }
    public MapBounds Bounds { get; }
}

/// <summary>
/// Detached conservative world topology. It intentionally carries no
/// ClipMapAsset root, build-data interface, packed pointer, save plan, or
/// persistence authority.
/// </summary>
public sealed class CollisionCompiledConservativeWorldSpatialPayload
{
    internal CollisionCompiledConservativeWorldSpatialPayload(
        MapBounds worldBounds,
        IEnumerable<CPlane> bspPlanes,
        IEnumerable<CNode> nodes,
        IEnumerable<CLeaf> leaves,
        IEnumerable<CLeafBrushNode> leafBrushNodes,
        IEnumerable<ushort> leafBrushReferences,
        IEnumerable<CollisionAabbTree> collisionAabbNodes,
        IEnumerable<CModel> collisionModels)
    {
        WorldBounds = worldBounds;
        BspPlanes = ReadOnly(bspPlanes);
        Nodes = ReadOnly(nodes);
        Leaves = ReadOnly(leaves);
        LeafBrushNodes = ReadOnly(leafBrushNodes);
        LeafBrushReferences = ReadOnly(leafBrushReferences);
        CollisionAabbNodes = ReadOnly(collisionAabbNodes);
        CollisionModels = ReadOnly(collisionModels);
    }

    public MapBounds WorldBounds { get; }
    public IReadOnlyList<CPlane> BspPlanes { get; }
    public IReadOnlyList<CNode> Nodes { get; }
    public IReadOnlyList<CLeaf> Leaves { get; }
    public IReadOnlyList<CLeafBrushNode> LeafBrushNodes { get; }
    public IReadOnlyList<ushort> LeafBrushReferences { get; }
    public IReadOnlyList<CollisionAabbTree> CollisionAabbNodes { get; }
    public IReadOnlyList<CModel> CollisionModels { get; }

    public IReadOnlyList<CollisionCompilerAggregateCardinality>
        CreateAggregateCardinalities(
            int borderCount,
            int partitionCount,
            int staticModelAabbNodeCount = 0)
    {
        if (borderCount < 0)
            throw new ArgumentOutOfRangeException(nameof(borderCount));
        if (partitionCount < 0)
            throw new ArgumentOutOfRangeException(nameof(partitionCount));
        if (partitionCount != CollisionAabbNodes.Count)
        {
            throw new ArgumentException(
                "The conservative world payload owns exactly one collision " +
                "AABB leaf per triangle partition.",
                nameof(partitionCount));
        }
        if (staticModelAabbNodeCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(staticModelAabbNodeCount));
        }

        return Array.AsReadOnly(
        new CollisionCompilerAggregateCardinality[]
        {
            new(CollisionIndexDomain.BspNode, Nodes.Count),
            new(CollisionIndexDomain.Leaf, Leaves.Count),
            new(
                CollisionIndexDomain.LeafBrushNode,
                LeafBrushNodes.Count),
            new(
                CollisionIndexDomain.LeafBrushReference,
                LeafBrushReferences.Count),
            // Leaf-surface targets remain unproven and authoring stays
            // disabled. M3 emits no speculative values.
            new(CollisionIndexDomain.LeafSurfaceReference, 0),
            new(CollisionIndexDomain.Border, borderCount),
            new(CollisionIndexDomain.Partition, partitionCount),
            new(
                CollisionIndexDomain.AabbTreeNode,
                CollisionAabbNodes.Count),
            new(
                CollisionIndexDomain.CollisionModel,
                CollisionModels.Count),
            new(
                CollisionIndexDomain.StaticModelAabbNode,
                staticModelAabbNodeCount)
        });
    }

    private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        T[] copy = values.ToArray();
        if (copy.Any(value => value is null))
        {
            throw new ArgumentException(
                "Compiled spatial payloads cannot contain null rows.",
                nameof(values));
        }
        return new ReadOnlyCollection<T>(copy);
    }
}

/// <summary>
/// Studio canonical M3 spatial policy for isolated validation. It places all
/// world collision in one conservative authored leaf, preserves the
/// retail-native zero-valued leaf-zero sentinel, supplies the mandatory BSP
/// root with its positive branch targeting a distinct empty leaf beyond the
/// authored world, uses one positive brush-list node, and creates one root
/// collision-AABB leaf per triangle partition.
///
/// This topology is deterministic and consumer-shaped, but deliberately not
/// consumer-accepted map visibility. M4 must replace or accept the spatial
/// representation before any FastFile persistence path may consume it.
/// </summary>
public static class CollisionConservativeWorldSpatialCompiler
{
    public const string PolicyIdentity =
        "iw4-studio.colmap.conservative-authored-leaf@2";

    private const float LeafBrushBoundsExpansion = 0.125f;
    private const float CollisionModelBoundsExpansion = 1f;

    public static CollisionCompiledConservativeWorldSpatialPayload Compile(
        IEnumerable<CollisionWorldBrushSpatialInput> brushes,
        IEnumerable<CollisionWorldPartitionSpatialInput> partitions,
        int materialCount,
        MapBounds? emptyWorldFallbackBounds = null)
    {
        ArgumentNullException.ThrowIfNull(brushes);
        ArgumentNullException.ThrowIfNull(partitions);
        CollisionCompositeOrdinalNamespaceContracts.ValidateMaterialCount(
            materialCount);

        CollisionWorldBrushSpatialInput[] orderedBrushes = brushes
            .OrderBy(value => value.BrushOrdinal)
            .ThenBy(
                value => value.SourceObjectId.Value.ToString("N"),
                StringComparer.Ordinal)
            .ToArray();
        CollisionWorldPartitionSpatialInput[] orderedPartitions = partitions
            .OrderBy(value => value.PartitionOrdinal)
            .ToArray();
        ValidateDenseOrdinals(orderedBrushes, orderedPartitions);
        MapBounds[] worldGeometryBounds =
        [
            .. orderedBrushes.Select(value => value.Bounds),
            .. orderedPartitions.Select(value => value.Bounds)
        ];
        MapBounds worldBounds = worldGeometryBounds.Length == 0
            ? emptyWorldFallbackBounds is { } fallback
                ? CollisionOutwardBounds.Include([fallback])
                : throw new ArgumentException(
                    "A conservative world candidate requires world " +
                    "collision geometry or explicit fallback bounds.")
            : CollisionOutwardBounds.Include(worldGeometryBounds);
        if (orderedBrushes.Length > short.MaxValue)
        {
            throw new OverflowException(
                "The single-leaf M3 policy supports at most 32,767 world " +
                "brush references. A partitioned leaf-brush tree is required " +
                "for larger candidates.");
        }
        if (orderedPartitions.Length > ushort.MaxValue)
        {
            throw new OverflowException(
                "The single-leaf M3 policy supports at most 65,535 root " +
                "collision-AABB entries.");
        }
        foreach (CollisionWorldPartitionSpatialInput partition in
                 orderedPartitions)
        {
            if (partition.MaterialOrdinal >= materialCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(partitions),
                    $"Partition {partition.PartitionOrdinal} material " +
                    $"{partition.MaterialOrdinal} is outside the " +
                    $"{materialCount}-row catalog.");
            }
        }

        float authoredMaximumX =
            CollisionOutwardBounds.Maximum(worldBounds).X;
        float outsideDistance = MathF.BitIncrement(authoredMaximumX);
        if (!float.IsFinite(outsideDistance) ||
            outsideDistance <= authoredMaximumX)
        {
            throw new OverflowException(
                "The conservative BSP cannot place its empty positive leaf " +
                "strictly beyond the authored world bounds.");
        }
        CPlane bspPlane = CreateBspPlane(outsideDistance);
        var node = new CNode
        {
            Plane = bspPlane,
            Children = Array.AsReadOnly(
                new short[]
                {
                    CollisionBspChildReference.Encode(
                            CollisionBspChildTargetKind.Leaf,
                            targetOrdinal: 2)
                        .SerializedValue,
                    CollisionBspChildReference.Encode(
                            CollisionBspChildTargetKind.Leaf,
                            targetOrdinal: 1)
                        .SerializedValue
                })
        };

        ushort[] leafBrushReferences =
            orderedBrushes.Select(value => value.BrushOrdinal).ToArray();
        CLeafBrushNode[] leafBrushNodes =
            CreateLeafBrushNodes(orderedBrushes, leafBrushReferences);
        CollisionAabbTree[] collisionAabbNodes =
            orderedPartitions.Select(CreatePartitionAabbLeaf).ToArray();

        uint brushContentsMask = orderedBrushes.Aggregate(
            0u,
            (current, value) => current | value.Contents);
        int brushContents = unchecked((int)brushContentsMask);
        int terrainContents = orderedPartitions.Aggregate(
            0,
            (current, value) => current | value.MaterialContents);
        MapBounds leafBrushBounds = orderedBrushes.Length == 0
            ? worldBounds
            : CollisionOutwardBounds.Expand(
                CollisionOutwardBounds.Include(
                    orderedBrushes.Select(value => value.Bounds)),
                LeafBrushBoundsExpansion);
        // Retail multiplayer ColMaps reserve leaf row zero as an all-zero
        // sentinel. Native BSP nodes never target it; real world leaves start
        // at ordinal one.
        var reservedLeaf = new CLeaf();
        var worldLeaf = new CLeaf
        {
            FirstCollAabbIndex = 0,
            CollAabbCount = checked((ushort)collisionAabbNodes.Length),
            BrushContents = brushContents,
            TerrainContents = terrainContents,
            Mins = ToAsset(CollisionOutwardBounds.Minimum(leafBrushBounds)),
            Maxs = ToAsset(CollisionOutwardBounds.Maximum(leafBrushBounds)),
            LeafBrushNode = orderedBrushes.Length == 0 ? 0 : 1
        };
        var emptyPositiveLeaf = new CLeaf();

        MapBounds cmodelBounds = CollisionOutwardBounds.Expand(
            worldBounds,
            CollisionModelBoundsExpansion);
        var worldModel = new CModel
        {
            Mins = ToAsset(CollisionOutwardBounds.Minimum(cmodelBounds)),
            Maxs = ToAsset(CollisionOutwardBounds.Maximum(cmodelBounds)),
            Radius = RadiusFromOrigin(cmodelBounds),
            // Model handle zero traverses the BSP rather than this embedded
            // leaf; preserve an inert world-model leaf.
            Leaf = new CLeaf()
        };

        return new CollisionCompiledConservativeWorldSpatialPayload(
            worldBounds,
            [bspPlane],
            [node],
            [reservedLeaf, worldLeaf, emptyPositiveLeaf],
            leafBrushNodes,
            leafBrushReferences,
            collisionAabbNodes,
            [worldModel]);
    }

    private static void ValidateDenseOrdinals(
        IReadOnlyList<CollisionWorldBrushSpatialInput> brushes,
        IReadOnlyList<CollisionWorldPartitionSpatialInput> partitions)
    {
        for (int index = 0; index < brushes.Count; index++)
        {
            if (brushes[index].BrushOrdinal != index)
            {
                throw new ArgumentException(
                    "World brushes must cover one dense final Brush-domain " +
                    $"prefix; expected ordinal {index}, observed " +
                    $"{brushes[index].BrushOrdinal}.",
                    nameof(brushes));
            }
        }
        for (int index = 0; index < partitions.Count; index++)
        {
            if (partitions[index].PartitionOrdinal != index)
            {
                throw new ArgumentException(
                    "Triangle partitions must cover one dense final " +
                    $"Partition domain; expected ordinal {index}, observed " +
                    $"{partitions[index].PartitionOrdinal}.",
                    nameof(partitions));
            }
        }
    }

    private static CLeafBrushNode[] CreateLeafBrushNodes(
        IReadOnlyList<CollisionWorldBrushSpatialInput> brushes,
        IReadOnlyList<ushort> leafBrushReferences)
    {
        var dummy = new CLeafBrushNode
        {
            Axis = 0,
            LeafBrushCount = 0,
            Contents = 0,
            Data = new CLeafBrushNodeData
            {
                Children = new CLeafBrushNodeChildren
                {
                    Dist = 0,
                    Range = 0,
                    ChildOffsets = Array.AsReadOnly(
                        new ushort[] { 0, 0 })
                }
            }
        };
        if (brushes.Count == 0)
            return [dummy];

        uint contents = brushes.Aggregate(
            0u,
            (current, value) => current | value.Contents);
        var leaf = new CLeafBrushNode
        {
            Axis = 0,
            LeafBrushCount = checked((short)brushes.Count),
            Contents = unchecked((int)contents),
            Data = new CLeafBrushNodeData
            {
                Brushes = Array.AsReadOnly(
                    leafBrushReferences.ToArray()),
                LeafUnionPad = Array.AsReadOnly(new byte[8])
            }
        };
        return [dummy, leaf];
    }

    private static CollisionAabbTree CreatePartitionAabbLeaf(
        CollisionWorldPartitionSpatialInput partition) =>
        new()
        {
            Origin = ToAsset(partition.Bounds.MidPoint),
            MaterialIndex = partition.MaterialOrdinal,
            ChildCount = 0,
            HalfSize = ToAsset(partition.Bounds.HalfSize),
            FirstChildOrPartitionIndex = partition.PartitionOrdinal
        };

    private static CPlane CreateBspPlane(float distance) =>
        new()
        {
            Normal = new AssetVector3 { X = 1, Y = 0, Z = 0 },
            Dist = distance == 0 ? 0 : distance,
            Type = 0,
            SignBits = 0,
            Pad12 = Array.AsReadOnly(new byte[] { 0, 0 })
        };

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
