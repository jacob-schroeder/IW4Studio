namespace IW4.Render.Scheduling.Lighting;

public enum MapRenderWorldEvent20SceneLightFrameInputFailureKind
{
    EyeOffsetInvalid = 0,
    SceneLightCountMismatch,
    ShadowAllocationSceneLightCountMismatch,
    AssetPoolRevisionMismatch,
    PrimaryLightUnavailable,
    PrimaryLightValueInvalid,
    LightDefNameInvalid,
    CanonicalLightDefUnavailable
}

public sealed record MapRenderWorldEvent20SceneLightFrameInputFailure(
    MapRenderWorldEvent20SceneLightFrameInputFailureKind Kind,
    string Detail,
    int? SceneLightIndex = null,
    string? LightDefName = null);

public sealed class MapRenderWorldEvent20SceneLightFrameInputBuildResult
{
    internal MapRenderWorldEvent20SceneLightFrameInputBuildResult(
        MapRenderWorldEvent20SceneLightFrameInput? input,
        MapRenderWorldEvent20SceneLightFrameInputFailure? failure)
    {
        if ((input is null) == (failure is null))
        {
            throw new ArgumentException(
                "Scene-light frame production requires exactly one input or failure.");
        }

        Input = input;
        Failure = failure;
    }

    public MapRenderWorldEvent20SceneLightFrameInput? Input { get; }

    public MapRenderWorldEvent20SceneLightFrameInputFailure? Failure { get; }

    public bool IsSuccess => Input is not null;
}
