using IW4.Render.Execution.Fog;

namespace IW4.Render.EditorPreview;

public sealed class MapRenderEditorPreviewCreateArtFogResolution
{
    internal MapRenderEditorPreviewCreateArtFogResolution(
        MapRenderEditorPreviewCreateArtFogStatus status,
        MapRenderActiveFogState? activeFog,
        string detail)
    {
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        if ((status == MapRenderEditorPreviewCreateArtFogStatus.Ready) !=
            (activeFog is not null))
        {
            throw new ArgumentException(
                "Only a ready createart fog resolution may own active fog.",
                nameof(activeFog));
        }

        Status = status;
        ActiveFog = activeFog;
        Detail = detail;
    }

    public MapRenderEditorPreviewCreateArtFogStatus Status { get; }

    public MapRenderActiveFogState? ActiveFog { get; }

    public string Detail { get; }

    public bool IsReady =>
        Status == MapRenderEditorPreviewCreateArtFogStatus.Ready;

    public bool CanonicalSourceAvailable => Status is not (
        MapRenderEditorPreviewCreateArtFogStatus.InvalidMapIdentity or
        MapRenderEditorPreviewCreateArtFogStatus.AssetPoolRevisionMismatch or
        MapRenderEditorPreviewCreateArtFogStatus.CanonicalRawFileAbsent);
}
