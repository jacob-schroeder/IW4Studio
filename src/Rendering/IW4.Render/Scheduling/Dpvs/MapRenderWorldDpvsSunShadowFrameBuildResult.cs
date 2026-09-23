namespace IW4.Render.Scheduling.Dpvs;

public sealed class MapRenderWorldDpvsSunShadowFrameBuildResult
{
    private MapRenderWorldDpvsSunShadowFrameBuildResult(
        MapRenderWorldDpvsSunShadowFrame? frame,
        MapRenderWorldDpvsSunShadowFrameFailure? failure)
    {
        if ((frame is null) == (failure is null))
        {
            throw new ArgumentException(
                "A sun-shadow frame result requires exactly one frame or failure.");
        }

        Frame = frame;
        Failure = failure;
    }

    public MapRenderWorldDpvsSunShadowFrame? Frame { get; }

    public MapRenderWorldDpvsSunShadowFrameFailure? Failure { get; }

    public bool IsSuccess => Frame is not null;

    public static MapRenderWorldDpvsSunShadowFrameBuildResult Succeeded(
        MapRenderWorldDpvsSunShadowFrame frame) => new(frame, null);

    public static MapRenderWorldDpvsSunShadowFrameBuildResult Failed(
        MapRenderWorldDpvsSunShadowFrameFailure failure) => new(null, failure);
}

