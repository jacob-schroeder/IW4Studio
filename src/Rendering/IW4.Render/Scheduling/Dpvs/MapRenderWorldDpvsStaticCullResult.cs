namespace IW4.Render.Scheduling.Dpvs;

public sealed class MapRenderWorldDpvsStaticCullResult
{
    private MapRenderWorldDpvsStaticCullResult(
        MapRenderWorldDpvsViewVisibility? visibility,
        MapRenderWorldDpvsStaticCullFailure? failure)
    {
        if ((visibility is null) == (failure is null))
            throw new ArgumentException("A static-cull result requires exactly one value or failure.");
        Visibility = visibility;
        Failure = failure;
    }

    public MapRenderWorldDpvsViewVisibility? Visibility { get; }

    public MapRenderWorldDpvsStaticCullFailure? Failure { get; }

    public bool IsSuccess => Visibility is not null;

    internal static MapRenderWorldDpvsStaticCullResult Succeeded(
        MapRenderWorldDpvsViewVisibility visibility) => new(visibility, null);

    internal static MapRenderWorldDpvsStaticCullResult Failed(
        MapRenderWorldDpvsStaticCullFailure failure) => new(null, failure);
}
