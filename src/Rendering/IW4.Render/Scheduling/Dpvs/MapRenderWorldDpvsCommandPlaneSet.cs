namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Plane storage shared by every cell command produced from the same camera
/// aperture or sun-shadow frustum. Published/public sets are immutable.
/// Portal traversal may instead borrow one distinct world-retained slot per
/// child aperture; those commands are synchronously consumed before the
/// serialized working set begins another frame.
/// </summary>
internal sealed class MapRenderWorldDpvsCommandPlaneSet
{
    private readonly MapRenderWorldDpvsClipPlane[] _planes;
    private readonly bool _isReusableScratch;
    private int _count;

    private MapRenderWorldDpvsCommandPlaneSet(
        MapRenderWorldDpvsClipPlane[] ownedPlanes,
        int count,
        bool isReusableScratch)
    {
        ArgumentNullException.ThrowIfNull(ownedPlanes);
        if ((uint)count > (uint)ownedPlanes.Length)
            throw new ArgumentOutOfRangeException(nameof(count));
        _planes = ownedPlanes;
        _count = count;
        _isReusableScratch = isReusableScratch;
        Planes = new ActivePlaneList(this);
    }

    public int Count => _count;

    public IReadOnlyList<MapRenderWorldDpvsClipPlane> Planes { get; }

    public ReadOnlySpan<MapRenderWorldDpvsClipPlane> Span =>
        _planes.AsSpan(0, _count);

    public static MapRenderWorldDpvsCommandPlaneSet CopyOf(
        IReadOnlyList<MapRenderWorldDpvsClipPlane> planes)
    {
        ArgumentNullException.ThrowIfNull(planes);
        MapRenderWorldDpvsClipPlane[] copy = planes.ToArray();
        return new(
            copy,
            copy.Length,
            isReusableScratch: false);
    }

    public static MapRenderWorldDpvsCommandPlaneSet CopyOf(
        ReadOnlySpan<MapRenderWorldDpvsClipPlane> planes)
    {
        MapRenderWorldDpvsClipPlane[] copy = planes.ToArray();
        return new(
            copy,
            copy.Length,
            isReusableScratch: false);
    }

    public static MapRenderWorldDpvsCommandPlaneSet TakeOwnership(
        MapRenderWorldDpvsClipPlane[] ownedPlanes)
    {
        ArgumentNullException.ThrowIfNull(ownedPlanes);
        return new(
            ownedPlanes,
            ownedPlanes.Length,
            isReusableScratch: false);
    }

    public MapRenderWorldDpvsCommandPlaneSet CopyPrefix(int count)
    {
        if ((uint)count > (uint)Count)
            throw new ArgumentOutOfRangeException(nameof(count));
        return count == Count
            ? this
            : CopyOf(_planes.AsSpan(0, count));
    }

    /// <summary>
    /// Creates one world-retained portal-plane slot. The slot is mutable only
    /// while its owning portal traversal workspace is active; command sets
    /// consume it synchronously before that workspace can begin another
    /// frame.
    /// </summary>
    internal static MapRenderWorldDpvsCommandPlaneSet CreateReusableScratch(
        int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        return new(
            new MapRenderWorldDpvsClipPlane[capacity],
            count: 0,
            isReusableScratch: true);
    }

    internal Span<MapRenderWorldDpvsClipPlane> WritableCapacitySpan
    {
        get
        {
            if (!_isReusableScratch)
            {
                throw new InvalidOperationException(
                    "Only a traversal-owned plane slot exposes writable capacity.");
            }
            return _planes;
        }
    }

    internal void PublishScratchCount(int count)
    {
        if (!_isReusableScratch)
        {
            throw new InvalidOperationException(
                "Only a traversal-owned plane slot can publish a scratch prefix.");
        }
        if ((uint)count > (uint)_planes.Length)
            throw new ArgumentOutOfRangeException(nameof(count));
        _count = count;
    }

    private sealed class ActivePlaneList :
        IReadOnlyList<MapRenderWorldDpvsClipPlane>
    {
        private readonly MapRenderWorldDpvsCommandPlaneSet _owner;

        public ActivePlaneList(
            MapRenderWorldDpvsCommandPlaneSet owner)
        {
            _owner = owner;
        }

        public int Count => _owner.Count;

        public MapRenderWorldDpvsClipPlane this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_owner.Count)
                    throw new ArgumentOutOfRangeException(nameof(index));
                return _owner._planes[index];
            }
        }

        public IEnumerator<MapRenderWorldDpvsClipPlane> GetEnumerator()
        {
            for (int index = 0; index < _owner.Count; index++)
                yield return _owner._planes[index];
        }

        System.Collections.IEnumerator
            System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }
}

/// <summary>
/// Compact immutable command consumed by the hot Event 0x0D cull path. Public
/// command objects are materialized only if diagnostic callers request them.
/// </summary>
internal readonly struct MapRenderWorldDpvsCellCullCommandData
{
    public MapRenderWorldDpvsCellCullCommandData(
        int cellIndex,
        MapRenderWorldDpvsCommandPlaneSet planeSet,
        int frustumPlaneCount)
    {
        if (cellIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(cellIndex));
        ArgumentNullException.ThrowIfNull(planeSet);
        if (planeSet.Count > MapRenderWorldDpvsCellCullCommand.MaximumPlaneCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(planeSet),
                "The PS3 child-plane scratch supports zero through 0x800 planes.");
        }
        if ((uint)frustumPlaneCount > (uint)planeSet.Count)
            throw new ArgumentOutOfRangeException(nameof(frustumPlaneCount));

        CellIndex = cellIndex;
        PlaneSet = planeSet;
        FrustumPlaneCount = frustumPlaneCount;
    }

    public int CellIndex { get; }

    public MapRenderWorldDpvsCommandPlaneSet PlaneSet { get; }

    public int FrustumPlaneCount { get; }

    public int Event0DPlaneCount => unchecked((byte)PlaneSet.Count);

    public ReadOnlySpan<MapRenderWorldDpvsClipPlane> Event0DPlaneSpan =>
        PlaneSet.Span[..Event0DPlaneCount];
}
