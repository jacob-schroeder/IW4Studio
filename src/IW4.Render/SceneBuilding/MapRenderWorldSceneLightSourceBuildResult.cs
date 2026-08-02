namespace IW4.Render.SceneBuilding;

public sealed class MapRenderWorldSceneLightSourceBuildResult
{
    internal MapRenderWorldSceneLightSourceBuildResult(
        MapRenderWorldSceneLightSource? source,
        MapRenderWorldSceneLightSourceFailure? failure)
    {
        if ((source is null) == (failure is null))
        {
            throw new ArgumentException(
                "A scene-light source result requires exactly one source or failure.");
        }
        Source = source;
        Failure = failure;
    }

    public MapRenderWorldSceneLightSource? Source { get; }

    public MapRenderWorldSceneLightSourceFailure? Failure { get; }

    public bool IsReady => Source is not null;
}
