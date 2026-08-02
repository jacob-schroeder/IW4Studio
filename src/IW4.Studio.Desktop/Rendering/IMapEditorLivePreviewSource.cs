using IW4.Render.EditorPreview;
using IW4.Render.Picking;
using IW4.Studio.MapEditor.Editing.Identity;

namespace IW4.Studio.Desktop.Rendering;

public sealed class MapEditorLivePreviewChangedEventArgs : EventArgs
{
    public MapEditorLivePreviewChangedEventArgs(
        MapRenderLiveSceneProjection projection)
    {
        Projection = projection ??
            throw new ArgumentNullException(nameof(projection));
    }

    public MapRenderLiveSceneProjection Projection { get; }
}

public sealed class MapEditorLivePreviewSelectionChangedEventArgs :
    EventArgs
{
    public MapEditorLivePreviewSelectionChangedEventArgs(
        MapObjectId? selection)
    {
        Selection = selection;
    }

    public MapObjectId? Selection { get; }
}

/// <summary>
/// Narrow renderer-facing seam for a live map-editor session. Consumers never
/// receive the mutable document or imported FastFile assets.
/// </summary>
public interface IMapEditorLivePreviewSource
{
    MapRenderLiveSceneProjection CurrentProjection { get; }

    event EventHandler<MapEditorLivePreviewChangedEventArgs> ProjectionChanged;
}

/// <summary>
/// Renderer-neutral shared selection for one map-editor session. The semantic
/// object ID is the only state exchanged with editor view models.
/// </summary>
public interface IMapEditorLivePreviewSelectionSource
{
    MapObjectId? CurrentSelection { get; }

    event EventHandler<MapEditorLivePreviewSelectionChangedEventArgs>?
        SelectionChanged;

    void SetSelection(MapObjectId? selection);
}

/// <summary>
/// Desktop-internal adapter for native viewport picks. Keeping the render hit
/// out of the public selection contract prevents renderer indices becoming
/// editor identities.
/// </summary>
internal interface IMapEditorLivePreviewPickSink
{
    bool PublishPick(MapRenderPickHit? hit);
}
