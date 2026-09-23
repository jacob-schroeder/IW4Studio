namespace IW4.Render.Scheduling.Dpvs;

public sealed class MapRenderWorldDpvsCameraSkyCullResult
{
    private MapRenderWorldDpvsCameraSkyCullResult(
        MapRenderWorldDpvsCameraSkyVisibility? visibility,
        MapRenderWorldDpvsCameraSkyCullFailure? failure)
    {
        if ((visibility is null) == (failure is null))
            throw new ArgumentException("Camera-sky culling requires exactly one value or failure.");
        Visibility = visibility;
        Failure = failure;
    }

    public MapRenderWorldDpvsCameraSkyVisibility? Visibility { get; }

    public MapRenderWorldDpvsCameraSkyCullFailure? Failure { get; }

    public bool IsSuccess => Visibility is not null;

    internal static MapRenderWorldDpvsCameraSkyCullResult Succeeded(
        MapRenderWorldDpvsCameraSkyVisibility visibility) => new(visibility, null);

    internal static MapRenderWorldDpvsCameraSkyCullResult Failed(
        MapRenderWorldDpvsCameraSkyCullFailure failure) => new(null, failure);
}
