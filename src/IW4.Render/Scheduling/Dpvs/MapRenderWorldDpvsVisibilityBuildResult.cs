namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Atomic three-view visibility result. CompletedViews retains independently
/// completed cull work when a different view producer is still unavailable.
/// </summary>
public sealed class MapRenderWorldDpvsVisibilityBuildResult
{
    private readonly MapRenderWorldDpvsViewVisibility[] _completedViews;
    private readonly MapRenderWorldDpvsVisibilityFailure[] _failures;

    internal MapRenderWorldDpvsVisibilityBuildResult(
        MapRenderWorldSurfaceVisibilityState? visibility,
        int? cameraCellIndex,
        IReadOnlyList<MapRenderWorldDpvsViewVisibility> completedViews,
        IReadOnlyList<MapRenderWorldDpvsVisibilityFailure> failures,
        MapRenderWorldDpvsSunShadowFullProjectionState?
            sunShadowProjection = null)
    {
        ArgumentNullException.ThrowIfNull(completedViews);
        ArgumentNullException.ThrowIfNull(failures);
        _completedViews = completedViews.ToArray();
        _failures = failures.ToArray();
        if (visibility is null)
        {
            if (_failures.Length == 0)
                throw new ArgumentException("An incomplete DPVS result requires typed failures.");
            if (sunShadowProjection is not null)
            {
                throw new ArgumentException(
                    "An incomplete DPVS result cannot publish operational sun-shadow projection constants.");
            }
        }
        else if (_failures.Length != 0 ||
                 _completedViews.Length != 3 ||
                 cameraCellIndex is null)
        {
            throw new ArgumentException(
                "A final DPVS visibility state requires one successful camera cell and all three views.");
        }

        Visibility = visibility;
        CameraCellIndex = cameraCellIndex;
        SunShadowProjection = sunShadowProjection;
        CompletedViews = Array.AsReadOnly(_completedViews);
        Failures = Array.AsReadOnly(_failures);
    }

    public MapRenderWorldSurfaceVisibilityState? Visibility { get; }

    public int? CameraCellIndex { get; }

    /// <summary>
    /// Full operational projection payload produced with views one and two.
    /// </summary>
    public MapRenderWorldDpvsSunShadowFullProjectionState?
        SunShadowProjection { get; }

    /// <summary>Projection-owned PS3 rows 0x1E/0x1F, when available.</summary>
    public MapRenderWorldDpvsSunShadowProjectionCodeConstants?
        SunShadowProjectionCodeConstants => SunShadowProjection?.CodeConstants;

    public IReadOnlyList<MapRenderWorldDpvsViewVisibility> CompletedViews { get; }

    public IReadOnlyList<MapRenderWorldDpvsVisibilityFailure> Failures { get; }

    public bool IsSuccess => Visibility is not null;
}
