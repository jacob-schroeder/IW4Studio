namespace IW4.Render.EditorPreview;

public sealed class MapRenderEditorPreviewVisionResolution
{
    internal MapRenderEditorPreviewVisionResolution(
        MapRenderEditorPreviewVisionStatus status,
        MapRenderEditorPreviewVisionState? vision,
        string detail)
    {
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        if ((status == MapRenderEditorPreviewVisionStatus.Ready) !=
            (vision is not null))
        {
            throw new ArgumentException(
                "Only a ready vision resolution may own active vision state.",
                nameof(vision));
        }

        Status = status;
        Vision = vision;
        Detail = detail;
    }

    public MapRenderEditorPreviewVisionStatus Status { get; }

    public MapRenderEditorPreviewVisionState? Vision { get; }

    public string Detail { get; }

    public bool IsReady =>
        Status == MapRenderEditorPreviewVisionStatus.Ready;
}
