namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Camera traversal products consumed by the EditorPreview visibility path.
/// </summary>
public sealed record MapRenderWorldDpvsCameraTraversal(
    int CameraCellIndex,
    MapRenderWorldDpvsNormalCameraFrame CameraFrame,
    MapRenderWorldDpvsViewCommandSet Commands,
    MapRenderWorldDpvsCameraSkyCullInput SkyCullInput);
