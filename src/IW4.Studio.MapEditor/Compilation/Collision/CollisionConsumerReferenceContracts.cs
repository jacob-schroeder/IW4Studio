using System.Collections.ObjectModel;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.Physics;

namespace IW4.Studio.MapEditor.Compilation.Collision;

/// <summary>
/// Allocation selected for a node/side plane pointer when the detached
/// ColMap candidate is emitted.
/// </summary>
public enum CollisionPlanePointerAllocationKind
{
    Null = 0,
    RootPlaneTableAlias = 1,
    NestedPlaneOwnedByReference = 2,
    NestedPlaneAlias = 3
}

/// <summary>
/// One deterministic semantic plane-pointer binding. Root aliases carry a
/// root-table ordinal. Nested aliases name the first node/side reference that
/// owns the nested plane allocation.
/// </summary>
public sealed record CollisionPlanePointerBinding(
    string Path,
    CollisionPlanePointerAllocationKind AllocationKind,
    int? RootPlaneOrdinal,
    string? NestedAllocationOwnerPath);

/// <summary>
/// Reproduces the current ColMap emitter's plane-allocation ownership without
/// exposing packed pointers as semantic identity. The root plane table owns
/// its rows; a non-root plane is allocated by its first deterministic
/// brush-side/node reference and later references alias that allocation.
/// </summary>
public sealed class CollisionPlanePointerOwnershipPlan
{
    private readonly IReadOnlyList<CollisionPlanePointerBinding> _bindings;

    private CollisionPlanePointerOwnershipPlan(
        IEnumerable<CollisionPlanePointerBinding> bindings) =>
        _bindings = new ReadOnlyCollection<CollisionPlanePointerBinding>(
            bindings.ToArray());

    public IReadOnlyList<CollisionPlanePointerBinding> Bindings => _bindings;

    public bool HasPreservationOnlyNullBindings => _bindings.Any(value =>
        value.AllocationKind == CollisionPlanePointerAllocationKind.Null);

    /// <summary>
    /// Null remains representable for imported inspection, but authored
    /// brush-side and BSP-node consumers dereference their plane. M3 emission
    /// must call this gate after every root/nested plane allocation has been
    /// included in its detached build plan.
    /// </summary>
    public void RequireAuthoredNonNullBindings()
    {
        string[] nullPaths = _bindings
            .Where(value =>
                value.AllocationKind ==
                CollisionPlanePointerAllocationKind.Null)
            .Select(value => value.Path)
            .ToArray();
        if (nullPaths.Length != 0)
        {
            throw new InvalidDataException(
                "Authored collision plane references cannot be null: " +
                string.Join(", ", nullPaths));
        }
    }

    public static CollisionPlanePointerOwnershipPlan Create(
        ClipMapAsset definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var rootOrdinals = new Dictionary<CPlane, int>(
            ReferenceEqualityComparer.Instance);
        for (int index = 0; index < definition.Planes.Count; index++)
        {
            if (!rootOrdinals.TryAdd(definition.Planes[index], index))
            {
                throw new InvalidDataException(
                    "The ColMap root plane table contains the same semantic " +
                    "plane object at multiple ordinals; pointer ownership " +
                    "would be ambiguous.");
            }
        }

        var nestedOwners = new Dictionary<CPlane, string>(
            ReferenceEqualityComparer.Instance);
        var bindings = new List<CollisionPlanePointerBinding>(
            checked(definition.BrushSides.Count + definition.Nodes.Count));

        // This order is the source allocation order in ClipMapBodyEmitter.
        for (int index = 0; index < definition.BrushSides.Count; index++)
        {
            Bind(
                definition.BrushSides[index].Plane,
                $"brushSides[{index}].plane",
                rootOrdinals,
                nestedOwners,
                bindings);
        }
        for (int index = 0; index < definition.Nodes.Count; index++)
        {
            Bind(
                definition.Nodes[index].Plane,
                $"nodes[{index}].plane",
                rootOrdinals,
                nestedOwners,
                bindings);
        }

        return new CollisionPlanePointerOwnershipPlan(bindings);
    }

    private static void Bind(
        CPlane? plane,
        string path,
        IReadOnlyDictionary<CPlane, int> rootOrdinals,
        IDictionary<CPlane, string> nestedOwners,
        ICollection<CollisionPlanePointerBinding> bindings)
    {
        if (plane is null)
        {
            bindings.Add(new(
                path,
                CollisionPlanePointerAllocationKind.Null,
                RootPlaneOrdinal: null,
                NestedAllocationOwnerPath: null));
            return;
        }
        if (rootOrdinals.TryGetValue(plane, out int rootOrdinal))
        {
            bindings.Add(new(
                path,
                CollisionPlanePointerAllocationKind.RootPlaneTableAlias,
                rootOrdinal,
                NestedAllocationOwnerPath: null));
            return;
        }
        if (nestedOwners.TryGetValue(plane, out string? ownerPath))
        {
            bindings.Add(new(
                path,
                CollisionPlanePointerAllocationKind.NestedPlaneAlias,
                RootPlaneOrdinal: null,
                ownerPath));
            return;
        }

        nestedOwners.Add(plane, path);
        bindings.Add(new(
            path,
            CollisionPlanePointerAllocationKind.NestedPlaneOwnedByReference,
            RootPlaneOrdinal: null,
            path));
    }
}

/// <summary>
/// Consumer-width and owner-local slice contract for one CBrush. The caller
/// supplies compiler-assigned starts; the contract never derives ownership
/// from packed pointer values.
/// </summary>
public readonly record struct CollisionBrushReferenceContract
{
    public const int AxialDirectionCount = 6;
    public const int MaximumMaterialCount = ushort.MaxValue + 1;
    public const int MaximumLocalAdjacencyEndExclusive =
        byte.MaxValue + byte.MaxValue;

    public CollisionBrushReferenceContract(
        CBrush brush,
        int materialCount,
        int sideSliceStart,
        int brushSideCount,
        int edgeSliceStart,
        int brushEdgeCount)
    {
        ArgumentNullException.ThrowIfNull(brush);
        if (materialCount < 0 || materialCount > MaximumMaterialCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(materialCount),
                $"The composite brush/AABB material namespace contains at " +
                $"most {MaximumMaterialCount} rows because every consumer " +
                "ordinal is UInt16.");
        }
        if (sideSliceStart < 0)
            throw new ArgumentOutOfRangeException(nameof(sideSliceStart));
        if (brushSideCount < 0)
            throw new ArgumentOutOfRangeException(nameof(brushSideCount));
        if (edgeSliceStart < 0)
            throw new ArgumentOutOfRangeException(nameof(edgeSliceStart));
        if (brushEdgeCount < 0)
            throw new ArgumentOutOfRangeException(nameof(brushEdgeCount));
        if (brush.Sides.Count != brush.NumSides)
        {
            throw new ArgumentException(
                $"CBrush.NumSides is {brush.NumSides}, but its owner-local " +
                $"side payload contains {brush.Sides.Count} rows.",
                nameof(brush));
        }
        if (brush.AxialMaterialNum.Count != AxialDirectionCount ||
            brush.FirstAdjacentSideOffsets.Count != AxialDirectionCount ||
            brush.EdgeCount.Count != AxialDirectionCount)
        {
            throw new ArgumentException(
                "CBrush axial material, first-adjacent-offset, and edge-count " +
                "fields each require exactly six entries.",
                nameof(brush));
        }

        int sideEndExclusive = checked(sideSliceStart + brush.NumSides);
        if (sideEndExclusive > brushSideCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sideSliceStart),
                $"Brush-side range [{sideSliceStart}, {sideEndExclusive}) " +
                $"is outside the {brushSideCount}-row root table.");
        }

        ValidateMaterials(brush, materialCount);
        int adjacencyByteCount = RequiredAdjacencyByteCount(brush);
        int edgeEndExclusive =
            checked(edgeSliceStart + adjacencyByteCount);
        if (edgeEndExclusive > brushEdgeCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(edgeSliceStart),
                $"Brush-edge range [{edgeSliceStart}, {edgeEndExclusive}) " +
                $"is outside the {brushEdgeCount}-byte root table.");
        }
        if (brush.BaseAdjacentSide.Count != adjacencyByteCount)
        {
            throw new ArgumentException(
                $"Brush adjacency offsets/counts require " +
                $"{adjacencyByteCount} owner-local bytes, but the payload " +
                $"contains {brush.BaseAdjacentSide.Count}.",
                nameof(brush));
        }

        SideSliceStart = sideSliceStart;
        SideCount = brush.NumSides;
        SideSliceEndExclusive = sideEndExclusive;
        EdgeSliceStart = edgeSliceStart;
        EdgeCount = adjacencyByteCount;
        EdgeSliceEndExclusive = edgeEndExclusive;
    }

    public int SideSliceStart { get; }
    public ushort SideCount { get; }
    public int SideSliceEndExclusive { get; }
    public int EdgeSliceStart { get; }
    public int EdgeCount { get; }
    public int EdgeSliceEndExclusive { get; }

    public static ushort DecodeAxialMaterialOrdinal(short serializedValue) =>
        unchecked((ushort)serializedValue);

    public static int RequiredAdjacencyByteCount(CBrush brush)
    {
        ArgumentNullException.ThrowIfNull(brush);
        if (brush.FirstAdjacentSideOffsets.Count != AxialDirectionCount ||
            brush.EdgeCount.Count != AxialDirectionCount)
        {
            throw new ArgumentException(
                "CBrush adjacency offsets and counts each require exactly " +
                "six entries.",
                nameof(brush));
        }

        int required = 0;
        for (int index = 0; index < AxialDirectionCount; index++)
        {
            required = Math.Max(
                required,
                brush.FirstAdjacentSideOffsets[index] +
                brush.EdgeCount[index]);
        }
        foreach (CBrushSide side in brush.Sides)
        {
            required = Math.Max(
                required,
                side.FirstAdjacentSideOffset + side.EdgeCount);
        }

        if (required > MaximumLocalAdjacencyEndExclusive)
        {
            throw new InvalidDataException(
                "CBrush adjacency offsets/counts exceed their composite " +
                "two-byte local namespace.");
        }
        return required;
    }

    private static void ValidateMaterials(CBrush brush, int materialCount)
    {
        for (int index = 0;
             index < brush.AxialMaterialNum.Count;
             index++)
        {
            ushort ordinal = DecodeAxialMaterialOrdinal(
                brush.AxialMaterialNum[index]);
            if (ordinal >= materialCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(brush),
                    $"Axial brush material {ordinal} at direction {index} " +
                    $"is outside the {materialCount}-row material catalog.");
            }
        }
        for (int index = 0; index < brush.Sides.Count; index++)
        {
            ushort ordinal = brush.Sides[index].MaterialNum;
            if (ordinal >= materialCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(brush),
                    $"Brush-side material {ordinal} at side {index} is " +
                    $"outside the {materialCount}-row material catalog.");
            }
        }
    }
}

/// <summary>
/// Complete owner-local brush slice plan in emitted brush ordinal order.
/// CBrush side and adjacency pointers are aliases into the root BrushSide and
/// BrushEdge arrays; nested payload ownership is not permitted for these two
/// pointers by the current ColMap emitter contract.
/// </summary>
public sealed class CollisionBrushReferencePlan
{
    private readonly IReadOnlyList<CollisionBrushReferenceContract> _brushes;

    private CollisionBrushReferencePlan(
        IEnumerable<CollisionBrushReferenceContract> brushes) =>
        _brushes =
            new ReadOnlyCollection<CollisionBrushReferenceContract>(
                brushes.ToArray());

    public IReadOnlyList<CollisionBrushReferenceContract> Brushes =>
        _brushes;

    public static CollisionBrushReferencePlan Create(ClipMapAsset definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        CollisionCompositeOrdinalNamespaceContracts.ValidateMaterialCount(
            definition.Materials.Count);

        var contracts =
            new CollisionBrushReferenceContract[definition.Brushes.Count];
        int sideStart = 0;
        int edgeStart = 0;
        for (int brushIndex = 0;
             brushIndex < definition.Brushes.Count;
             brushIndex++)
        {
            CBrush brush = definition.Brushes[brushIndex];
            var contract = new CollisionBrushReferenceContract(
                brush,
                definition.Materials.Count,
                sideStart,
                definition.BrushSides.Count,
                edgeStart,
                definition.BrushEdges.Count);

            for (int sideIndex = 0;
                 sideIndex < contract.SideCount;
                 sideIndex++)
            {
                if (ReferenceEquals(
                        brush.Sides[sideIndex],
                        definition.BrushSides[
                            contract.SideSliceStart + sideIndex]))
                {
                    continue;
                }

                throw new InvalidDataException(
                    $"brushes[{brushIndex}].sides[{sideIndex}] does not " +
                    "alias its compiler-assigned root BrushSide row.");
            }
            for (int edgeIndex = 0;
                 edgeIndex < contract.EdgeCount;
                 edgeIndex++)
            {
                if (brush.BaseAdjacentSide[edgeIndex] ==
                    definition.BrushEdges[
                        contract.EdgeSliceStart + edgeIndex])
                {
                    continue;
                }

                throw new InvalidDataException(
                    $"brushes[{brushIndex}].baseAdjacentSide[{edgeIndex}] " +
                    "does not match its compiler-assigned root BrushEdge " +
                    "byte.");
            }

            contracts[brushIndex] = contract;
            sideStart = contract.SideSliceEndExclusive;
            edgeStart = contract.EdgeSliceEndExclusive;
        }

        if (sideStart != definition.BrushSides.Count)
        {
            throw new InvalidDataException(
                $"Brush-owned side ranges cover {sideStart} rows, but the " +
                $"root BrushSide table contains " +
                $"{definition.BrushSides.Count}.");
        }
        if (edgeStart != definition.BrushEdges.Count)
        {
            throw new InvalidDataException(
                $"Brush-owned adjacency ranges cover {edgeStart} bytes, " +
                $"but the root BrushEdge table contains " +
                $"{definition.BrushEdges.Count}.");
        }

        return new CollisionBrushReferencePlan(contracts);
    }
}

/// <summary>
/// Current M0 authority for ColMap.leafSurfaces values. The root payload and
/// UInt32 element width are proven, but no audited consumer has yet proven
/// the referenced domain or any sentinel value.
/// </summary>
public static class CollisionLeafSurfaceReferencePolicy
{
    public const CollisionSerializedElementEncoding ElementEncoding =
        CollisionSerializedElementEncoding.UnsignedInt32;

    public static CollisionIndexDomain? TargetDomain => null;
    public static bool HasProvenSentinel => false;
    public static bool CanAuthorReferences => false;

    public static void RequireAuthoredReferenceContract() =>
        throw new InvalidOperationException(
            "ColMap.leafSurfaces remains preservation-only: its UInt32 " +
            "storage is proven, but the IW4 target domain and sentinel " +
            "semantics are unresolved.");
}

/// <summary>
/// Limits created by serialized fields that compose more than one index
/// namespace. These are stricter than the independent root-table counts.
/// </summary>
public static class CollisionCompositeOrdinalNamespaceContracts
{
    public const int MaximumUInt16AddressableElementCount =
        ushort.MaxValue + 1;

    public const int MaximumPartitionVertexOrdinal =
        byte.MaxValue *
        CollisionPartitionVertexSegmentContract.VertexOrdinalStride +
        ushort.MaxValue;

    public const int MaximumPartitionVertexAddressableElementCount =
        MaximumPartitionVertexOrdinal + 1;

    public static void ValidateMaterialCount(int materialCount)
    {
        if (materialCount < 0 ||
            materialCount > MaximumUInt16AddressableElementCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(materialCount),
                $"Brush-side, axial-brush, and collision-AABB material " +
                $"ordinals share a UInt16 namespace of at most " +
                $"{MaximumUInt16AddressableElementCount} rows.");
        }
    }

    public static void ValidateStaticModelAabbVirtualNamespace(
        int staticModelCount,
        int staticModelAabbNodeCount)
    {
        if (staticModelCount < 0)
            throw new ArgumentOutOfRangeException(nameof(staticModelCount));
        if (staticModelAabbNodeCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(staticModelAabbNodeCount));
        }

        long combinedCount =
            (long)staticModelCount + staticModelAabbNodeCount;
        if (combinedCount > MaximumUInt16AddressableElementCount)
        {
            throw new OverflowException(
                $"The static-model AABB FirstChild namespace combines " +
                $"{staticModelCount} static models and " +
                $"{staticModelAabbNodeCount} child nodes, exceeding its " +
                $"{MaximumUInt16AddressableElementCount}-ordinal UInt16 " +
                "capacity.");
        }
    }
}
