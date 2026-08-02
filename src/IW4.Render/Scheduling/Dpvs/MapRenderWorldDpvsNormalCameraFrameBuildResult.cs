namespace IW4.Render.Scheduling.Dpvs;

public sealed class MapRenderWorldDpvsNormalCameraFrameBuildResult
{
    private MapRenderWorldDpvsNormalCameraFrameBuildResult(
        MapRenderWorldDpvsNormalCameraFrame? frame,
        MapRenderWorldDpvsNormalCameraFrameFailure? failure)
    {
        Frame = frame;
        Failure = failure;
    }

    public MapRenderWorldDpvsNormalCameraFrame? Frame { get; }

    public MapRenderWorldDpvsNormalCameraFrameFailure? Failure { get; }

    public bool IsSuccess => Frame is not null && Failure is null;

    public static MapRenderWorldDpvsNormalCameraFrameBuildResult Succeeded(
        MapRenderWorldDpvsNormalCameraFrame frame) => new(frame, null);

    public static MapRenderWorldDpvsNormalCameraFrameBuildResult Failed(
        MapRenderWorldDpvsNormalCameraFrameFailure failure) => new(null, failure);
}
