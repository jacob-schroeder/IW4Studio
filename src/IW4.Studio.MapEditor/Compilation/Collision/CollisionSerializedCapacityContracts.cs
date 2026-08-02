using System.Collections.ObjectModel;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.Physics;
using IW4.FastFiles.Zone;

namespace IW4.Studio.MapEditor.Compilation.Collision;

/// <summary>
/// Ownership of bytes allocated for a serialized collision domain.
/// Runtime-derived arrays reserve native storage but never accept authored
/// payload values.
/// </summary>
public enum CollisionSerializedPayloadOwnership
{
    CompilerSerialized = 0,
    RuntimeDerivedZeroFill = 1
}

/// <summary>
/// Fixed-width allocation contract for one ColMap index domain. Relative
/// fixed-array order, stride, alignment, and block match
/// <c>ClipMapBodyEmitter</c>.
/// Variable-size strings and nested XAsset definitions remain linker-owned
/// and are intentionally outside this collision-owned capacity contract.
/// </summary>
public sealed record CollisionSerializedArrayLayout(
    CollisionIndexDomain Domain,
    XFileBlockType Block,
    int SerializedStride,
    int Alignment,
    CollisionSerializedPayloadOwnership Ownership);

public static class CollisionSerializedArrayLayouts
{
    private static readonly IReadOnlyList<
        CollisionSerializedArrayLayout> Layouts =
        Array.AsReadOnly(CreateLayouts());

    private static readonly IReadOnlyDictionary<
        CollisionIndexDomain,
        CollisionSerializedArrayLayout> ByDomain =
        new ReadOnlyDictionary<
            CollisionIndexDomain,
            CollisionSerializedArrayLayout>(
            Layouts.ToDictionary(value => value.Domain));

    /// <summary>
    /// Relative allocation order of ColMap-owned fixed arrays. Variable
    /// strings and linker-owned nested definitions may be interleaved by the
    /// body emitter, so the fixed plan is an early lower-bound preflight; the
    /// final <c>EmissionPlan</c> remains the complete block-capacity authority.
    /// </summary>
    public static IReadOnlyList<CollisionSerializedArrayLayout> All =>
        Layouts;

    public static CollisionSerializedArrayLayout GetRequired(
        CollisionIndexDomain domain)
    {
        if (!Enum.IsDefined(domain))
            throw new ArgumentOutOfRangeException(nameof(domain));

        return ByDomain.TryGetValue(domain, out var layout)
            ? layout
            : throw new InvalidOperationException(
                $"Collision domain {domain} has no serialized array layout.");
    }

    private static CollisionSerializedArrayLayout[] CreateLayouts()
    {
        CollisionSerializedArrayLayout[] layouts =
        [
            Serialized(
                CollisionIndexDomain.Plane,
                CPlane.SerializedSize,
                4),
            Serialized(
                CollisionIndexDomain.StaticModel,
                ClipStaticModel.SerializedSize,
                4),
            Serialized(
                CollisionIndexDomain.Material,
                ClipMaterial.SerializedSize,
                4),
            Serialized(
                CollisionIndexDomain.BrushSide,
                CBrushSide.SerializedSize,
                4),
            Serialized(CollisionIndexDomain.BrushEdge, 1, 1),
            Serialized(
                CollisionIndexDomain.BspNode,
                CNode.SerializedSize,
                4),
            Serialized(
                CollisionIndexDomain.Leaf,
                CLeaf.SerializedSize,
                4),
            // The emitter writes the global leaf-brush reference table before
            // leaf-brush node rows.
            Serialized(
                CollisionIndexDomain.LeafBrushReference,
                sizeof(ushort),
                2),
            Serialized(
                CollisionIndexDomain.LeafBrushNode,
                CLeafBrushNode.SerializedSize,
                4),
            Serialized(
                CollisionIndexDomain.LeafSurfaceReference,
                sizeof(uint),
                4),
            Serialized(
                CollisionIndexDomain.TriangleVertex,
                0x0c,
                4),
            Serialized(
                CollisionIndexDomain.TriangleIndex,
                sizeof(ushort),
                2),
            Serialized(
                CollisionIndexDomain.TriangleWalkabilityPackedByte,
                sizeof(byte),
                1),
            Serialized(
                CollisionIndexDomain.Border,
                CollisionBorder.SerializedSize,
                4),
            Serialized(
                CollisionIndexDomain.Partition,
                CollisionPartition.SerializedSize,
                4),
            Serialized(
                CollisionIndexDomain.AabbTreeNode,
                CollisionAabbTree.SerializedSize,
                16),
            Serialized(
                CollisionIndexDomain.CollisionModel,
                CModel.SerializedSize,
                4),
            Serialized(
                CollisionIndexDomain.Brush,
                CBrush.SerializedSize,
                128),
            Serialized(
                CollisionIndexDomain.BrushBounds,
                0x18,
                128),
            Serialized(
                CollisionIndexDomain.BrushContents,
                sizeof(uint),
                4),
            Serialized(
                CollisionIndexDomain.StaticModelAabbNode,
                SModelAabbNode.SerializedSize,
                4),
            Serialized(
                CollisionIndexDomain.DynamicEntityDefinitionSlot0,
                DynEntityDef.SerializedSize,
                4),
            Serialized(
                CollisionIndexDomain.DynamicEntityDefinitionSlot1,
                DynEntityDef.SerializedSize,
                4),
            Runtime(
                CollisionIndexDomain.DynamicEntityPoseSlot0,
                DynEntityPose.SerializedSize),
            Runtime(
                CollisionIndexDomain.DynamicEntityPoseSlot1,
                DynEntityPose.SerializedSize),
            Runtime(
                CollisionIndexDomain.DynamicEntityClientSlot0,
                DynEntityClient.SerializedSize),
            Runtime(
                CollisionIndexDomain.DynamicEntityClientSlot1,
                DynEntityClient.SerializedSize),
            Runtime(
                CollisionIndexDomain.DynamicEntityCollisionSlot0,
                DynEntityColl.SerializedSize),
            Runtime(
                CollisionIndexDomain.DynamicEntityCollisionSlot1,
                DynEntityColl.SerializedSize)
        ];

        CollisionIndexDomain[] duplicates = layouts
            .GroupBy(value => value.Domain)
            .Where(value => value.Count() != 1)
            .Select(value => value.Key)
            .ToArray();
        if (duplicates.Length != 0)
        {
            throw new InvalidOperationException(
                "Serialized collision array layouts contain duplicate " +
                $"domains: {string.Join(", ", duplicates)}.");
        }

        CollisionIndexDomain[] missing = Enum
            .GetValues<CollisionIndexDomain>()
            .Except(layouts.Select(value => value.Domain))
            .ToArray();
        if (missing.Length != 0)
        {
            throw new InvalidOperationException(
                "Serialized collision array layouts are incomplete: " +
                string.Join(", ", missing));
        }

        return layouts;
    }

    private static CollisionSerializedArrayLayout Serialized(
        CollisionIndexDomain domain,
        int stride,
        int alignment) =>
        Create(
            domain,
            XFileBlockType.LARGE,
            stride,
            alignment,
            CollisionSerializedPayloadOwnership.CompilerSerialized);

    private static CollisionSerializedArrayLayout Runtime(
        CollisionIndexDomain domain,
        int stride) =>
        Create(
            domain,
            XFileBlockType.RUNTIME,
            stride,
            4,
            CollisionSerializedPayloadOwnership.RuntimeDerivedZeroFill);

    private static CollisionSerializedArrayLayout Create(
        CollisionIndexDomain domain,
        XFileBlockType block,
        int stride,
        int alignment,
        CollisionSerializedPayloadOwnership ownership)
    {
        if (!Enum.IsDefined(domain))
            throw new ArgumentOutOfRangeException(nameof(domain));
        if (!Enum.IsDefined(block))
            throw new ArgumentOutOfRangeException(nameof(block));
        if (stride <= 0)
            throw new ArgumentOutOfRangeException(nameof(stride));
        if (alignment <= 0 || (alignment & (alignment - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(alignment));
        if (!Enum.IsDefined(ownership))
            throw new ArgumentOutOfRangeException(nameof(ownership));

        return new(
            domain,
            block,
            stride,
            alignment,
            ownership);
    }
}

public enum CollisionSerializedAllocationKind
{
    ColMapRoot = 0,
    DomainArray = 1
}

public sealed record CollisionSerializedAllocation(
    CollisionSerializedAllocationKind Kind,
    CollisionIndexDomain? Domain,
    XFileBlockType Block,
    int ElementCount,
    int SerializedStride,
    int Alignment,
    int MinimumStartOffset,
    int MinimumEndOffset,
    CollisionSerializedPayloadOwnership Ownership)
{
    public int PayloadByteCount => checked(ElementCount * SerializedStride);
}

public sealed record CollisionSerializedBlockCapacity(
    XFileBlockType Block,
    int StartingOffset,
    int MinimumEndOffset)
{
    public int MinimumConsumedByteCount =>
        checked(MinimumEndOffset - StartingOffset);
}

/// <summary>
/// Immutable cumulative lower-bound capacity preflight for the fixed ColMap
/// root and owned arrays. A packed IW4 block offset has 28 bits; every aligned
/// allocation and its exclusive end must remain below 0x10000000, matching
/// <c>EmissionPlan.Allocate</c>. The final emitter plan additionally accounts
/// for variable strings and linker-owned nested definitions.
/// </summary>
public sealed class CollisionFixedPayloadCapacityPlan
{
    public const int PackedBlockOffsetExclusiveLimit = 0x10000000;

    private readonly IReadOnlyList<CollisionSerializedAllocation> _allocations;
    private readonly IReadOnlyDictionary<
        XFileBlockType,
        CollisionSerializedBlockCapacity> _blocks;
    private readonly IReadOnlyDictionary<
        CollisionIndexDomain,
        CollisionSerializedAllocation> _domainAllocations;

    private CollisionFixedPayloadCapacityPlan(
        IReadOnlyList<CollisionSerializedAllocation> allocations,
        IReadOnlyDictionary<
            XFileBlockType,
            CollisionSerializedBlockCapacity> blocks,
        IReadOnlyDictionary<
            CollisionIndexDomain,
            CollisionSerializedAllocation> domainAllocations)
    {
        _allocations = allocations;
        _blocks = blocks;
        _domainAllocations = domainAllocations;
    }

    public IReadOnlyList<CollisionSerializedAllocation> Allocations =>
        _allocations;

    public IReadOnlyCollection<CollisionSerializedBlockCapacity> Blocks =>
        Array.AsReadOnly(_blocks.Values
            .OrderBy(value => value.Block)
            .ToArray());

    public static CollisionFixedPayloadCapacityPlan Create(
        ICollisionDomainCardinalityPlan indexPlan,
        IReadOnlyDictionary<XFileBlockType, int>? startingBlockOffsets = null)
    {
        ArgumentNullException.ThrowIfNull(indexPlan);

        Dictionary<XFileBlockType, int> starts =
            NormalizeStartingOffsets(startingBlockOffsets);
        Dictionary<XFileBlockType, int> cursors =
            starts.ToDictionary(value => value.Key, value => value.Value);
        var allocations = new List<CollisionSerializedAllocation>();
        var byDomain = new Dictionary<
            CollisionIndexDomain,
            CollisionSerializedAllocation>();

        Allocate(
            CollisionSerializedAllocationKind.ColMapRoot,
            domain: null,
            XFileBlockType.TEMP,
            elementCount: 1,
            ClipMapAsset.SerializedSize,
            alignment: 4,
            CollisionSerializedPayloadOwnership.CompilerSerialized);

        foreach (CollisionSerializedArrayLayout layout in
                 CollisionSerializedArrayLayouts.All)
        {
            int elementCount = indexPlan.GetDomainCount(layout.Domain);
            if (elementCount == 0)
                continue;

            Allocate(
                CollisionSerializedAllocationKind.DomainArray,
                layout.Domain,
                layout.Block,
                elementCount,
                layout.SerializedStride,
                layout.Alignment,
                layout.Ownership);
        }

        var blocks = new Dictionary<
            XFileBlockType,
            CollisionSerializedBlockCapacity>();
        foreach (XFileBlockType block in starts.Keys)
        {
            blocks.Add(
                block,
                new(
                    block,
                    starts[block],
                    cursors[block]));
        }

        return new(
            Array.AsReadOnly(allocations.ToArray()),
            new ReadOnlyDictionary<
                XFileBlockType,
                CollisionSerializedBlockCapacity>(blocks),
            new ReadOnlyDictionary<
                CollisionIndexDomain,
                CollisionSerializedAllocation>(byDomain));

        void Allocate(
            CollisionSerializedAllocationKind kind,
            CollisionIndexDomain? domain,
            XFileBlockType block,
            int elementCount,
            int stride,
            int alignment,
            CollisionSerializedPayloadOwnership ownership)
        {
            int byteCount = checked(elementCount * stride);
            int start = AlignDownstreamOffset(
                cursors.GetValueOrDefault(block),
                alignment);
            long end = (long)start + byteCount;
            if (start >= PackedBlockOffsetExclusiveLimit ||
                end >= PackedBlockOffsetExclusiveLimit)
            {
                string owner = domain?.ToString() ?? "ColMap root";
                throw new OverflowException(
                    $"{owner} would end {block} at 0x{end:X}, outside the " +
                    "28-bit packed-address range.");
            }

            var allocation = new CollisionSerializedAllocation(
                kind,
                domain,
                block,
                elementCount,
                stride,
                alignment,
                start,
                checked((int)end),
                ownership);
            allocations.Add(allocation);
            cursors[block] = allocation.MinimumEndOffset;
            if (domain is { } value)
                byDomain.Add(value, allocation);
        }
    }

    public bool TryGetDomainAllocation(
        CollisionIndexDomain domain,
        out CollisionSerializedAllocation? allocation)
    {
        if (!Enum.IsDefined(domain))
            throw new ArgumentOutOfRangeException(nameof(domain));

        return _domainAllocations.TryGetValue(domain, out allocation);
    }

    public CollisionSerializedBlockCapacity GetRequiredBlock(
        XFileBlockType block)
    {
        if (!Enum.IsDefined(block))
            throw new ArgumentOutOfRangeException(nameof(block));

        return _blocks.TryGetValue(block, out var capacity)
            ? capacity
            : throw new KeyNotFoundException(
                $"Collision capacity plan does not use {block}.");
    }

    internal static int AlignDownstreamOffset(
        int offset,
        int alignment)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (alignment <= 0 || (alignment & (alignment - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(alignment));

        long aligned =
            ((long)offset + alignment - 1) & ~(long)(alignment - 1);
        if (aligned >= PackedBlockOffsetExclusiveLimit)
        {
            throw new OverflowException(
                $"Aligned block offset 0x{aligned:X} is outside the 28-bit " +
                "packed-address range.");
        }
        return checked((int)aligned);
    }

    private static Dictionary<XFileBlockType, int> NormalizeStartingOffsets(
        IReadOnlyDictionary<XFileBlockType, int>? supplied)
    {
        var starts = new Dictionary<XFileBlockType, int>
        {
            [XFileBlockType.TEMP] = 0,
            [XFileBlockType.LARGE] = 0,
            [XFileBlockType.RUNTIME] = 0
        };
        if (supplied is null)
            return starts;

        foreach ((XFileBlockType block, int offset) in supplied)
        {
            if (!Enum.IsDefined(block) ||
                block is < XFileBlockType.TEMP or >= XFileBlockType.COUNT)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(supplied),
                    $"Unknown XFile block {block}.");
            }
            if (!starts.ContainsKey(block))
            {
                throw new ArgumentException(
                    $"{block} is not allocated by the fixed ColMap payload.",
                    nameof(supplied));
            }
            if (offset < 0 ||
                offset >= PackedBlockOffsetExclusiveLimit)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(supplied),
                    $"{block} starting offset 0x{offset:X} is outside the " +
                    "28-bit packed-address range.");
            }
            starts[block] = offset;
        }

        return starts;
    }
}
