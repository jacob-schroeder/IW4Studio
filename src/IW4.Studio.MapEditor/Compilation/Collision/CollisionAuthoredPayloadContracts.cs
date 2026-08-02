using System.Collections.ObjectModel;
using IW4.Studio.MapEditor.Editing.Identity;

namespace IW4.Studio.MapEditor.Compilation.Collision;

/// <summary>
/// One source-owned ClipMaterial row and its consumer-addressable ordinal.
/// Equal material definitions in different sources intentionally retain
/// independent rows so ownership remains explicit and source ranges remain
/// contiguous.
/// </summary>
public sealed record CollisionAuthoredMaterialOrdinal(
    MapObjectId SourceObjectId,
    ushort Ordinal,
    AuthoredCollisionMaterialInput Material);

/// <summary>
/// Deterministic material catalog for canonical authored collision sources.
/// Within each deterministically ordered source, exact material definitions
/// are ordered by name and bit fields before ordinals are assigned.
/// </summary>
public sealed class CollisionAuthoredMaterialOrdinalPlan
{
    private const int MaximumConsumerAddressableMaterialCount =
        ushort.MaxValue + 1;

    private static readonly CollisionAuthoredMaterialOrdinalPlan EmptyPlan =
        new([], new Dictionary<MaterialLookupKey, ushort>());

    private readonly IReadOnlyList<CollisionAuthoredMaterialOrdinal> _entries;
    private readonly IReadOnlyDictionary<MaterialLookupKey, ushort> _ordinals;

    private CollisionAuthoredMaterialOrdinalPlan(
        IReadOnlyList<CollisionAuthoredMaterialOrdinal> entries,
        IReadOnlyDictionary<MaterialLookupKey, ushort> ordinals)
    {
        _entries = entries;
        _ordinals = ordinals;
    }

    public static CollisionAuthoredMaterialOrdinalPlan Empty => EmptyPlan;

    public IReadOnlyList<CollisionAuthoredMaterialOrdinal> Entries => _entries;

    public ushort GetRequiredOrdinal(
        MapObjectId sourceObjectId,
        AuthoredCollisionMaterialInput material)
    {
        if (sourceObjectId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(sourceObjectId));
        ArgumentNullException.ThrowIfNull(material);

        return _ordinals.TryGetValue(
            MaterialLookupKey.Create(sourceObjectId, material),
            out ushort ordinal)
            ? ordinal
            : throw new KeyNotFoundException(
                $"Collision source {sourceObjectId} has no authored material " +
                $"definition named '{material.ExactName}'.");
    }

    public int GetSourceMaterialCount(MapObjectId sourceObjectId)
    {
        if (sourceObjectId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(sourceObjectId));

        return _entries.Count(value =>
            value.SourceObjectId == sourceObjectId);
    }

    internal static CollisionAuthoredMaterialOrdinalPlan Create(
        IReadOnlyList<AuthoredCollisionSource> orderedSources)
    {
        ArgumentNullException.ThrowIfNull(orderedSources);

        var pending = new List<(
            MapObjectId SourceObjectId,
            AuthoredCollisionMaterialInput Material)>();
        foreach (AuthoredCollisionSource source in orderedSources)
        {
            foreach (AuthoredCollisionMaterialInput material in
                     Materials(source)
                         .Distinct(MaterialIdentityComparer.Instance)
                         .OrderBy(
                             value => value.ExactName,
                             StringComparer.Ordinal)
                         .ThenBy(value => unchecked((uint)value.SurfaceFlags))
                         .ThenBy(value => unchecked((uint)value.Contents)))
            {
                pending.Add((source.ObjectId, material));
            }
        }

        if (pending.Count > MaximumConsumerAddressableMaterialCount)
        {
            throw new OverflowException(
                $"Canonical authored collision requires {pending.Count} " +
                "ClipMaterial rows, but IW4 collision consumers address at " +
                $"most {MaximumConsumerAddressableMaterialCount} rows.");
        }

        var entries =
            new CollisionAuthoredMaterialOrdinal[pending.Count];
        var ordinals =
            new Dictionary<MaterialLookupKey, ushort>(pending.Count);
        for (int index = 0; index < pending.Count; index++)
        {
            (MapObjectId sourceObjectId, AuthoredCollisionMaterialInput material)
                = pending[index];
            ushort ordinal = checked((ushort)index);
            entries[index] = new CollisionAuthoredMaterialOrdinal(
                sourceObjectId,
                ordinal,
                material);
            ordinals.Add(
                MaterialLookupKey.Create(sourceObjectId, material),
                ordinal);
        }

        // Xbox IW4 MP consumer anchors:
        // - CM_TraceThroughAabbTree @ 0x82350648 loads the AABB
        //   MaterialIndex with lhz at 0x82350660, multiplies it by the
        //   0x0C ClipMaterial stride, and consumes that row.
        // - CM_TraceThroughBrush @ 0x82353F58 loads axial CBrush material
        //   ordinals with lhzx at 0x8235440C and CBrushSide.MaterialNum
        //   with lhz at 0x8235479C, then dereferences the same 0x0C catalog
        //   at 0x82354920-0x82354958.
        // These unsigned-16 consumers establish the 65,536-row authored
        // catalog limit. They do not establish triangle material grouping.
        return new CollisionAuthoredMaterialOrdinalPlan(
            Array.AsReadOnly(entries),
            new ReadOnlyDictionary<MaterialLookupKey, ushort>(ordinals));
    }

    private static IEnumerable<AuthoredCollisionMaterialInput> Materials(
        AuthoredCollisionSource source) =>
        source switch
        {
            AuthoredConvexBrushCollisionSource brush => brush.Materials,
            AuthoredIndexedTriangleMeshCollisionSource mesh => mesh.Materials,
            AuthoredPairedStaticModelCollisionSource => [],
            _ => throw new InvalidDataException(
                $"Unsupported canonical authored collision source " +
                $"{source.GetType().Name}.")
        };

    private readonly record struct MaterialLookupKey(
        MapObjectId SourceObjectId,
        string ExactName,
        int SurfaceFlags,
        int Contents)
    {
        public static MaterialLookupKey Create(
            MapObjectId sourceObjectId,
            AuthoredCollisionMaterialInput material) =>
            new(
                sourceObjectId,
                material.ExactName,
                material.SurfaceFlags,
                material.Contents);
    }

    private sealed class MaterialIdentityComparer :
        IEqualityComparer<AuthoredCollisionMaterialInput>
    {
        public static MaterialIdentityComparer Instance { get; } = new();

        public bool Equals(
            AuthoredCollisionMaterialInput? left,
            AuthoredCollisionMaterialInput? right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left is null || right is null)
                return false;

            return StringComparer.Ordinal.Equals(
                    left.ExactName,
                    right.ExactName) &&
                left.SurfaceFlags == right.SurfaceFlags &&
                left.Contents == right.Contents;
        }

        public int GetHashCode(AuthoredCollisionMaterialInput value)
        {
            ArgumentNullException.ThrowIfNull(value);

            return HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(value.ExactName),
                value.SurfaceFlags,
                value.Contents);
        }
    }
}

/// <summary>
/// Consumer-exact packing for the aggregate ColMap triEdgeIsWalkable stream.
/// Triangle order must match the emitted triangle-index stream; edge order is
/// vertex pairs 0-1, 1-2, then 2-0.
/// </summary>
public static class CollisionTriangleWalkabilityPacker
{
    public static IReadOnlyList<byte> Pack(
        IEnumerable<AuthoredTriangleEdgeWalkability> triangles)
    {
        ArgumentNullException.ThrowIfNull(triangles);

        AuthoredTriangleEdgeWalkability[] copy = triangles.ToArray();
        int edgeCount = checked(copy.Length * 3);
        int packedByteCount = checked(
            ((edgeCount + 0x1f) >> 5) << 2);
        var packed = new byte[packedByteCount];

        int edgeOrdinal = 0;
        foreach (AuthoredTriangleEdgeWalkability triangle in copy)
        {
            Set(packed, edgeOrdinal++, triangle.Edge01);
            Set(packed, edgeOrdinal++, triangle.Edge12);
            Set(packed, edgeOrdinal++, triangle.Edge20);
        }

        // Xbox IW4 MP CM_IsEdgeWalkable @ 0x8234DBE8 computes
        // triangleOrdinal * 3 + edgeOrdinal, selects byte ordinal >> 3,
        // and tests 1 << (ordinal & 7) at 0x8234DC00-0x8234DC14.
        // Therefore the first serialized edge is byte 0 bit 0 (LSB-first);
        // ClipMap's count formula pads the aggregate byte stream to 32 bits.
        return Array.AsReadOnly(packed);
    }

    private static void Set(
        IList<byte> packed,
        int edgeOrdinal,
        bool value)
    {
        if (!value)
            return;

        int byteOrdinal = edgeOrdinal >> 3;
        packed[byteOrdinal] = (byte)(
            packed[byteOrdinal] |
            (1 << (edgeOrdinal & 7)));
    }
}

/// <summary>
/// Consumer-proven conversion between a root TriangleVertex ordinal and one
/// partition-relative ushort triangle index. A partition assignment is
/// mandatory; applying a single global ushort rebase is not valid IW4 data.
/// </summary>
public static class CollisionTrianglePartitionIndexContract
{
    public const int VerticesPerSegment =
        CollisionPartitionVertexSegmentContract.VertexOrdinalStride;

    public static ushort EncodePartitionRelativeOrdinal(
        byte firstVertSegment,
        int globalVertexOrdinal,
        int triangleVertexCount)
    {
        ValidateGlobalOrdinal(globalVertexOrdinal, triangleVertexCount);

        var segment =
            new CollisionPartitionVertexSegmentContract(firstVertSegment);
        int segmentBase = segment.VertexBaseOrdinal;
        int partitionRelativeOrdinal = checked(
            globalVertexOrdinal - segmentBase);
        if (partitionRelativeOrdinal < 0 ||
            partitionRelativeOrdinal > ushort.MaxValue)
        {
            throw new OverflowException(
                $"Triangle vertex {globalVertexOrdinal} is outside the " +
                $"unsigned-16 window beginning at partition segment " +
                $"{firstVertSegment} (root vertex {segmentBase}).");
        }

        return checked((ushort)partitionRelativeOrdinal);
    }

    public static int ResolveGlobalVertexOrdinal(
        byte firstVertSegment,
        ushort partitionRelativeOrdinal,
        int triangleVertexCount)
    {
        if (triangleVertexCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(triangleVertexCount));
        }

        var segment =
            new CollisionPartitionVertexSegmentContract(firstVertSegment);
        int globalVertexOrdinal =
            segment.ResolveVertexOrdinal(partitionRelativeOrdinal);
        ValidateGlobalOrdinal(globalVertexOrdinal, triangleVertexCount);
        return globalVertexOrdinal;
    }

    private static void ValidateGlobalOrdinal(
        int globalVertexOrdinal,
        int triangleVertexCount)
    {
        if (triangleVertexCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(triangleVertexCount));
        }
        if (globalVertexOrdinal < 0 ||
            globalVertexOrdinal >= triangleVertexCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(globalVertexOrdinal),
                $"Triangle vertex ordinal {globalVertexOrdinal} is outside " +
                $"the {triangleVertexCount}-row root vertex table.");
        }
    }

    // Xbox IW4 MP CM_TraceThroughAabbTree_Hit @ 0x823500A0:
    // 0x82350118 reads CollisionPartition.FirstVertSegment at +0x02;
    // 0x82350120-0x82350138 computes segment * 0x3000 bytes and adds the
    // TriangleVertex root pointer. With a 0x0C Vec3 stride this is
    // segment * 1024 vertices. The triangle payload itself is read as
    // unsigned-16 elements. These operations establish the conversion above.
}
