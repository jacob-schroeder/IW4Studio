namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Operational values read by the PS3 camera portal walker. The defaults are
/// the values registered by default_mp.elf in the renderer dvar initializer.
/// </summary>
public sealed class MapRenderWorldDpvsPortalTraversalSettings
{
    public MapRenderWorldDpvsPortalTraversalSettings(
        bool skipPvs,
        bool singleCell,
        float portalBevels,
        bool portalBevelsOnly,
        int portalWalkLimit,
        float portalMinClipArea,
        int portalMinRecurseDepth)
    {
        if (!float.IsFinite(portalBevels) || portalBevels < 0f)
            throw new ArgumentOutOfRangeException(nameof(portalBevels));
        if (portalWalkLimit is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(portalWalkLimit));
        if (!float.IsFinite(portalMinClipArea) || portalMinClipArea < 0f)
            throw new ArgumentOutOfRangeException(nameof(portalMinClipArea));
        if (portalMinRecurseDepth is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(portalMinRecurseDepth));

        SkipPvs = skipPvs;
        SingleCell = singleCell;
        PortalBevels = portalBevels;
        PortalBevelsOnly = portalBevelsOnly;
        PortalWalkLimit = portalWalkLimit;
        PortalMinClipArea = portalMinClipArea;
        PortalMinRecurseDepth = portalMinRecurseDepth;
    }

    public bool SkipPvs { get; }

    public bool SingleCell { get; }

    public float PortalBevels { get; }

    public bool PortalBevelsOnly { get; }

    public int PortalWalkLimit { get; }

    public float PortalMinClipArea { get; }

    public int PortalMinRecurseDepth { get; }

    public static MapRenderWorldDpvsPortalTraversalSettings Ps3Default { get; } =
        new(
            skipPvs: false,
            singleCell: false,
            portalBevels: 0.7f,
            portalBevelsOnly: false,
            portalWalkLimit: 0,
            portalMinClipArea: 0.02f,
            portalMinRecurseDepth: 2);
}
