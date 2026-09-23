namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Camera-view-only DPVS result for EditorPreview scheduling. Unlike the native
/// normal-camera result, it intentionally carries no sun-shadow visibility or
/// projection constants.
/// </summary>
public sealed class MapRenderWorldDpvsCameraOnlyVisibilityBuildResult
{
    private MapRenderWorldDpvsCameraOnlyVisibilityBuildResult(
        MapRenderWorldDpvsViewVisibility? visibility,
        int? cameraCellIndex,
        MapRenderWorldDpvsCameraOnlyVisibilityFailure? failure)
    {
        if ((visibility is null) == (failure is null))
        {
            throw new ArgumentException(
                "A camera-only DPVS result requires exactly one visibility value or failure.");
        }
        Visibility = visibility;
        CameraCellIndex = cameraCellIndex;
        Failure = failure;
    }

    public MapRenderWorldDpvsViewVisibility? Visibility { get; }

    public int? CameraCellIndex { get; }

    public MapRenderWorldDpvsCameraOnlyVisibilityFailure? Failure { get; }

    public bool IsSuccess => Visibility is not null;

    internal static MapRenderWorldDpvsCameraOnlyVisibilityBuildResult
        Succeeded(
            MapRenderWorldDpvsViewVisibility visibility,
            int cameraCellIndex) =>
        new(visibility, cameraCellIndex, null);

    internal static MapRenderWorldDpvsCameraOnlyVisibilityBuildResult Failed(
        MapRenderWorldDpvsCameraOnlyVisibilityFailure failure) =>
        new(null, null, failure);
}
