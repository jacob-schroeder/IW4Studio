using Avalonia.Controls;
using IW4.Studio.Desktop.Rendering;
using IW4.Studio.Desktop.Rendering.WorldViewport;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.MapEditor;
using IW4.Studio.MapEditor.Compilation.Import;
using IW4.Studio.Rendering;

namespace IW4.Studio.Desktop.Views;

public sealed partial class MapEditorWindow : Window
{
    public MapEditorWindow()
    {
        InitializeComponent();
        Icon = AppIcon.Create();
        Closed += MapEditorWindow_Closed;
    }

    public MapEditorWindow(ExistingMapImportResult session)
        : this()
    {
        ArgumentNullException.ThrowIfNull(session);
        DataContext = new MapEditorWindowViewModel(session);
        Title =
            $"{session.Bundle.MapIdentity} — Map Editor — IW4 Studio";
    }

    public MapEditorWindow(MapEditorOpenResult result)
        : this()
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Succeeded || result.Session is not { } session)
        {
            throw new ArgumentException(
                "A ready map-editor result is required.",
                nameof(result));
        }

        DataContext = new MapEditorWindowViewModel(result);
        Title =
            $"{session.Bundle.MapIdentity} — Map Editor — IW4 Studio";
    }

    public MapEditorWindow(
        MapEditorOpenResult result,
        MapEditorLivePreviewBridge livePreview)
        : this()
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(livePreview);
        if (!result.Succeeded || result.Session is not { } session)
        {
            throw new ArgumentException(
                "A ready map-editor result is required.",
                nameof(result));
        }

        DataContext = new MapEditorWindowViewModel(result, livePreview);
        Title =
            $"{session.Bundle.MapIdentity} — Map Editor — IW4 Studio";
    }

    public MapEditorWindow(
        MapEditorOpenResult result,
        MapEditorLivePreviewBridge livePreview,
        MapEditorEditingContext editingContext)
        : this()
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(livePreview);
        ArgumentNullException.ThrowIfNull(editingContext);
        if (!result.Succeeded || result.Session is not { } session)
        {
            throw new ArgumentException(
                "A ready map-editor result is required.",
                nameof(result));
        }

        DataContext = new MapEditorWindowViewModel(
            result,
            livePreview,
            editingContext);
        Title =
            $"{session.Bundle.MapIdentity} — Map Editor — IW4 Studio";
    }

    internal MapEditorWindow(
        MapEditorOpenResult result,
        MapEditorLivePreviewBridge livePreview,
        MapEditorEditingContext editingContext,
        Task<RenderViewSceneBuildResult> worldSceneWarmup)
        : this(result, livePreview, editingContext)
    {
        ArgumentNullException.ThrowIfNull(worldSceneWarmup);
        ExistingMapImportResult session =
            result.Session ??
            throw new ArgumentException(
                "A ready map-editor result is required.",
                nameof(result));
        WorldViewport.Attach(
            worldSceneWarmup,
            livePreview,
            WorldViewportSceneAuthority.From(session.Bundle));
    }

    private void MapEditorWindow_Closed(object? sender, EventArgs e)
    {
        Closed -= MapEditorWindow_Closed;
        WorldViewport.Dispose();
        if (DataContext is IDisposable disposable)
            disposable.Dispose();
    }
}
