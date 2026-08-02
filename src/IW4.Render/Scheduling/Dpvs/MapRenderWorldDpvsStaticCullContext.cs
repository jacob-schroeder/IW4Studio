using IW4.Assets.Assets.GfxMap;

namespace IW4.Render.Scheduling.Dpvs;

internal sealed class MapRenderWorldDpvsStaticCullContext
{
    private readonly GfxWorldAsset _world;
    private readonly HashSet<int> _activeTreePath = [];
    private IReadOnlyList<GfxAabbTree> _trees = [];
    private int _cellIndex;

    public MapRenderWorldDpvsStaticCullContext(
        GfxWorldAsset world,
        uint[] surfaceBits,
        uint[] staticModelBits)
    {
        _world = world;
        SurfaceBits = surfaceBits;
        StaticModelBits = staticModelBits;
    }

    public uint[] SurfaceBits { get; }

    public uint[] StaticModelBits { get; }

    public MapRenderWorldDpvsStaticCullFailure? Failure { get; private set; }

    public void BeginFrame()
    {
        Array.Clear(SurfaceBits);
        Array.Clear(StaticModelBits);
        _activeTreePath.Clear();
        _trees = [];
        _cellIndex = 0;
        Failure = null;
    }

    public void BeginCommand(
        int cellIndex,
        IReadOnlyList<GfxAabbTree> trees)
    {
        _cellIndex = cellIndex;
        _trees = trees;
        _activeTreePath.Clear();
        Failure = null;
    }

    public bool CullTree(
        int treeIndex,
        ReadOnlySpan<MapRenderWorldDpvsClipPlane> planes)
    {
        if ((uint)treeIndex >= (uint)_trees.Count ||
            !_activeTreePath.Add(treeIndex))
        {
            return Fail(
                MapRenderWorldDpvsStaticCullFailureKind.InvalidAabbTreeTopology,
                $"Cell {_cellIndex} AABB topology reaches invalid or cyclic row {treeIndex}.",
                treeIndex);
        }

        try
        {
            GfxAabbTree tree = _trees[treeIndex];
            if (!MapRenderWorldDpvsAabbPlaneTester.TryGetBounds(
                    tree.Bounds,
                    out MapRenderWorldDpvsBounds bounds))
            {
                return Fail(
                    MapRenderWorldDpvsStaticCullFailureKind.InvalidAabbTreeBounds,
                    $"Cell {_cellIndex} AABB row {treeIndex} has malformed midpoint/half-size bounds.",
                    treeIndex);
            }

            Span<MapRenderWorldDpvsClipPlane> childPlanes =
                stackalloc MapRenderWorldDpvsClipPlane[16];
            int childPlaneCount = 0;
            foreach (MapRenderWorldDpvsClipPlane plane in planes)
            {
                if (MapRenderWorldDpvsAabbPlaneTester.PositiveVertexDistance(bounds, plane) <= 0f)
                    return true;
                if (MapRenderWorldDpvsAabbPlaneTester.NegativeVertexDistance(bounds, plane) < 0f)
                {
                    if (childPlaneCount == childPlanes.Length)
                    {
                        return Fail(
                            MapRenderWorldDpvsStaticCullFailureKind.ActivePlaneCapacityExceeded,
                            $"Cell {_cellIndex} AABB row {treeIndex} retains more than sixteen active Event 0x0D planes.",
                            treeIndex);
                    }
                    childPlanes[childPlaneCount++] = plane;
                }
            }

            // PS3 Event 0x0D branches on +0x18 before the leaf
            // +0x1E/+0x20 content path (0x00350AB4 -> 0x00350AC0 or
            // 0x00350B20). Internal rows recurse even after every plane was
            // discarded; their aggregate content metadata is not consumed.
            if (tree.ChildCount != 0)
            {
                if (tree.ChildrenOffset <= 0 ||
                    tree.ChildrenOffset % GfxAabbTree.SerializedSize != 0)
                {
                    return Fail(
                        MapRenderWorldDpvsStaticCullFailureKind.InvalidAabbTreeTopology,
                        $"Cell {_cellIndex} AABB row {treeIndex} has invalid relative child offset {tree.ChildrenOffset}.",
                        treeIndex);
                }

                int firstChildIndex;
                int childEnd;
                try
                {
                    firstChildIndex = checked(
                        treeIndex + tree.ChildrenOffset / GfxAabbTree.SerializedSize);
                    childEnd = checked(firstChildIndex + tree.ChildCount);
                }
                catch (OverflowException)
                {
                    return Fail(
                        MapRenderWorldDpvsStaticCullFailureKind.InvalidAabbTreeTopology,
                        $"Cell {_cellIndex} AABB row {treeIndex} child range overflows host indexing.",
                        treeIndex);
                }

                if (firstChildIndex < 0 || childEnd > _trees.Count)
                {
                    return Fail(
                        MapRenderWorldDpvsStaticCullFailureKind.InvalidAabbTreeTopology,
                        $"Cell {_cellIndex} AABB row {treeIndex} child range [{firstChildIndex}, {childEnd}) escapes {_trees.Count} rows.",
                        treeIndex);
                }

                for (int childIndex = firstChildIndex;
                     childIndex < childEnd;
                     childIndex++)
                {
                    if (!CullTree(childIndex, childPlanes[..childPlaneCount]))
                        return false;
                }
                return true;
            }

            if (childPlaneCount == 0)
                return MarkTreeContents(tree, treeIndex);

            return CullLeafContents(
                tree,
                treeIndex,
                childPlanes[..childPlaneCount]);
        }
        finally
        {
            _activeTreePath.Remove(treeIndex);
        }
    }

    private bool MarkTreeContents(GfxAabbTree tree, int treeIndex)
    {
        if (!TryVisitStaticModelIndices(tree, treeIndex, markWithoutCull: true, []))
            return false;
        return TryVisitSurfaceIndices(tree, treeIndex, markWithoutCull: true, []);
    }

    private bool CullLeafContents(
        GfxAabbTree tree,
        int treeIndex,
        ReadOnlySpan<MapRenderWorldDpvsClipPlane> planes)
    {
        if (!TryVisitStaticModelIndices(tree, treeIndex, markWithoutCull: false, planes))
            return false;
        return TryVisitSurfaceIndices(tree, treeIndex, markWithoutCull: false, planes);
    }

    private bool TryVisitStaticModelIndices(
        GfxAabbTree tree,
        int treeIndex,
        bool markWithoutCull,
        ReadOnlySpan<MapRenderWorldDpvsClipPlane> planes)
    {
        if (tree.SModelIndexes.Count < tree.SModelIndexCount)
        {
            return Fail(
                MapRenderWorldDpvsStaticCullFailureKind.InvalidStaticModelIndex,
                $"Cell {_cellIndex} AABB row {treeIndex} lacks its declared static-model indices.",
                treeIndex);
        }

        for (int ordinal = 0; ordinal < tree.SModelIndexCount; ordinal++)
        {
            int staticModelIndex = tree.SModelIndexes[ordinal];
            if ((uint)staticModelIndex >= (uint)_world.Dpvs.SModelInsts.Count)
            {
                return Fail(
                    MapRenderWorldDpvsStaticCullFailureKind.InvalidStaticModelIndex,
                    $"Cell {_cellIndex} AABB row {treeIndex} references static model {staticModelIndex} outside {_world.Dpvs.SModelInsts.Count} rows.",
                    treeIndex,
                    staticModelIndex);
            }

            if (TestBit(StaticModelBits, staticModelIndex))
                continue;
            if (!markWithoutCull)
            {
                if (!MapRenderWorldDpvsAabbPlaneTester.TryGetBounds(
                        _world.Dpvs.SModelInsts[staticModelIndex].Bounds,
                        out MapRenderWorldDpvsBounds bounds))
                {
                    return Fail(
                        MapRenderWorldDpvsStaticCullFailureKind.InvalidStaticModelBounds,
                        $"Static model {staticModelIndex} has malformed midpoint/half-size bounds.",
                        treeIndex,
                        staticModelIndex);
                }
                if (MapRenderWorldDpvsAabbPlaneTester.IsOutside(bounds, planes))
                    continue;
            }

            SetBit(StaticModelBits, staticModelIndex);
        }
        return true;
    }

    private bool TryVisitSurfaceIndices(
        GfxAabbTree tree,
        int treeIndex,
        bool markWithoutCull,
        ReadOnlySpan<MapRenderWorldDpvsClipPlane> planes)
    {
        int start = tree.StartSurfIndex;
        int end = start + tree.SurfaceCount;
        if (end > _world.Dpvs.SortedSurfIndex.Count)
        {
            return Fail(
                MapRenderWorldDpvsStaticCullFailureKind.InvalidSurfaceRange,
                $"Cell {_cellIndex} AABB row {treeIndex} sorted-surface range [{start}, {end}) escapes {_world.Dpvs.SortedSurfIndex.Count} entries.",
                treeIndex);
        }

        for (int sortedIndex = start; sortedIndex < end; sortedIndex++)
        {
            int surfaceIndex = _world.Dpvs.SortedSurfIndex[sortedIndex];
            if ((uint)surfaceIndex >= (uint)_world.SurfaceCount)
            {
                return Fail(
                    MapRenderWorldDpvsStaticCullFailureKind.InvalidSurfaceIndex,
                    $"Sorted-surface entry {sortedIndex} references surface {surfaceIndex} outside {_world.SurfaceCount} rows.",
                    treeIndex,
                    surfaceIndex);
            }

            if (TestBit(SurfaceBits, surfaceIndex))
                continue;
            if (!markWithoutCull)
            {
                GfxSurfaceBounds surfaceBounds = _world.Dpvs.SurfaceBounds[surfaceIndex];
                if (!MapRenderWorldDpvsAabbPlaneTester.TryGetBounds(
                        surfaceBounds.Bounds,
                        out MapRenderWorldDpvsBounds bounds))
                {
                    return Fail(
                        MapRenderWorldDpvsStaticCullFailureKind.InvalidSurfaceBounds,
                        $"Surface {surfaceIndex} has malformed DPVS midpoint/half-size cull bounds.",
                        treeIndex,
                        surfaceIndex);
                }
                if (MapRenderWorldDpvsAabbPlaneTester.IsOutside(bounds, planes))
                    continue;
            }

            SetBit(SurfaceBits, surfaceIndex);
        }
        return true;
    }

    private bool Fail(
        MapRenderWorldDpvsStaticCullFailureKind kind,
        string detail,
        int? treeIndex,
        int? elementIndex = null)
    {
        Failure = new(kind, detail, _cellIndex, treeIndex, elementIndex);
        return false;
    }

    private static bool TestBit(uint[] words, int index) =>
        (words[index >> 5] & (0x8000_0000u >> (index & 31))) != 0;

    private static void SetBit(uint[] words, int index) =>
        words[index >> 5] |= 0x8000_0000u >> (index & 31);
}
