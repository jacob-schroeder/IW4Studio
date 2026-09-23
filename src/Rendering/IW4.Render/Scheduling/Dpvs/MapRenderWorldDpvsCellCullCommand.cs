namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Managed form of the PS3 Event 0x0D cell record. FrustumPlaneCount is
/// retained because the sibling DPVS events consume it. Plane storage can use
/// the 0x800 child scratch; Event 0x0D consumes the byte-truncated count that
/// 0x0034A490 places in its own record.
/// </summary>
public sealed class MapRenderWorldDpvsCellCullCommand
{
    public const int MaximumPlaneCount = 0x800;

    private readonly MapRenderWorldDpvsCellCullCommandData _data;

    public MapRenderWorldDpvsCellCullCommand(
        int cellIndex,
        IReadOnlyList<MapRenderWorldDpvsClipPlane> planes,
        int frustumPlaneCount)
        : this(CreateData(cellIndex, planes, frustumPlaneCount))
    {
    }

    internal MapRenderWorldDpvsCellCullCommand(
        MapRenderWorldDpvsCellCullCommandData data)
    {
        _data = data;
    }

    public int CellIndex => _data.CellIndex;

    public IReadOnlyList<MapRenderWorldDpvsClipPlane> Planes =>
        _data.PlaneSet.Planes;

    public int FrustumPlaneCount => _data.FrustumPlaneCount;

    /// <summary>
    /// 0x0034A490 stores the source count as a byte in the Event 0x0D record.
    /// The sibling event records retain the wider count.
    /// </summary>
    internal int Event0DPlaneCount => _data.Event0DPlaneCount;

    internal ReadOnlySpan<MapRenderWorldDpvsClipPlane> Event0DPlaneSpan =>
        _data.Event0DPlaneSpan;

    internal MapRenderWorldDpvsCellCullCommandData Data => _data;

    private static MapRenderWorldDpvsCellCullCommandData CreateData(
        int cellIndex,
        IReadOnlyList<MapRenderWorldDpvsClipPlane> planes,
        int frustumPlaneCount)
    {
        ArgumentNullException.ThrowIfNull(planes);
        if (planes.Count > MaximumPlaneCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(planes),
                "The PS3 child-plane scratch supports zero through 0x800 planes.");
        }
        return new(
            cellIndex,
            MapRenderWorldDpvsCommandPlaneSet.CopyOf(planes),
            frustumPlaneCount);
    }
}
