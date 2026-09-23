namespace IW4.Render.SceneBuilding;

public sealed record MapRenderWorldSceneLightSourceFailure(
    MapRenderWorldSceneLightSourceFailureKind Kind,
    string Detail);
