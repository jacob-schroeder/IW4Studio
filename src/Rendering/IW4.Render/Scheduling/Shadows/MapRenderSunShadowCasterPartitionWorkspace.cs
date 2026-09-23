namespace IW4.Render.Scheduling.Shadows;

/// <summary>
/// Reusable storage for one sun-shadow partition. Published partitions copy
/// only the active ranges, so a later frame may safely reuse these arrays.
/// </summary>
internal sealed class MapRenderSunShadowCasterPartitionWorkspace
{
    private readonly int[] _worldSurfaceIndices;
    private readonly MapRenderSunShadowStaticCasterIdentity[]
        _staticDrawInstances;
    private int _isActive;
    private int _worldSurfaceCount;
    private int _staticDrawInstanceCount;

    public MapRenderSunShadowCasterPartitionWorkspace(
        int worldCasterCapacity,
        int staticCasterCapacity)
    {
        if (worldCasterCapacity < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(worldCasterCapacity));
        }
        if (staticCasterCapacity < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(staticCasterCapacity));
        }

        _worldSurfaceIndices = new int[worldCasterCapacity];
        _staticDrawInstances =
            new MapRenderSunShadowStaticCasterIdentity[
                staticCasterCapacity];
    }

    public ReadOnlySpan<int> ActiveWorldSurfaceIndices =>
        _worldSurfaceIndices.AsSpan(0, _worldSurfaceCount);

    public ReadOnlySpan<MapRenderSunShadowStaticCasterIdentity>
        ActiveStaticDrawInstances =>
            _staticDrawInstances.AsSpan(0, _staticDrawInstanceCount);

    public void Begin()
    {
        if (Interlocked.CompareExchange(ref _isActive, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "A sun-shadow caster partition workspace cannot service overlapping frames.");
        }

        _worldSurfaceCount = 0;
        _staticDrawInstanceCount = 0;
    }

    public void AddWorldSurface(int surfaceIndex)
    {
        if ((uint)_worldSurfaceCount >=
            (uint)_worldSurfaceIndices.Length)
        {
            throw new InvalidOperationException(
                "World-caster admission exceeded its prevalidated topology capacity.");
        }
        _worldSurfaceIndices[_worldSurfaceCount++] = surfaceIndex;
    }

    public void AddStaticDrawInstance(int staticModelIndex)
    {
        if ((uint)_staticDrawInstanceCount >=
            (uint)_staticDrawInstances.Length)
        {
            throw new InvalidOperationException(
                "Static-caster admission exceeded its prevalidated topology capacity.");
        }

        // The DPVS bit, cull row, and draw-inst row are index-parallel.
        _staticDrawInstances[_staticDrawInstanceCount++] = new(
            staticModelIndex,
            DrawInstanceIndex: staticModelIndex,
            ObjectIndex: staticModelIndex);
    }

    public void Exit()
    {
        if (Interlocked.Exchange(ref _isActive, 0) != 1)
        {
            throw new InvalidOperationException(
                "A sun-shadow caster partition workspace was released without being active.");
        }
    }
}
