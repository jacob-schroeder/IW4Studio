namespace IW4.Render.OpenGl.Targets;

/// <summary>Outcome of the target-local once-per-frame clear gate.</summary>
public enum MapRenderOpenGlNormalCameraSceneTargetReplayResult
{
    BoundAndCleared = 0,
    AlreadyClearedThisFrame = 1
}
