namespace IW4.Render.SceneBuilding;

public sealed record MapRenderWorldSceneSourceBuildFailure(
    MapRenderWorldSceneSourceBuildFailureKind Kind,
    string Detail);
