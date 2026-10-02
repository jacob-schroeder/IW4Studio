using Avalonia.Controls;
using Avalonia.Interactivity;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Views;

public partial class MainWindow
{
    private MapScriptsWindow? _mapScriptsWindow;

    private void MapScripts_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogs.BlocksInput) return;
        FinishGestures();
        OpenMapScripts();
    }

    private MapScriptsWindow OpenMapScripts()
    {
        if (_mapScriptsWindow is { } open)
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return open;
        }

        var window = new MapScriptsWindow(_session,
            () => _buildEmitterAssetsPath ??
                (_session.FilePath is { } path ? FindEmitterAssetDirectory(path) : null),
            FindBootstrapAssets, ShowScriptEntitiesInMap);
        _mapScriptsWindow = window;
        window.Closed += (_, _) => _mapScriptsWindow = null;
        window.Show(this);
        return window;
    }

    private MapEntity? SelectedScriptEntity() => _session.Selection.Active is { } active
        ? MapOrganization.Entity(_session.Document, active) : null;

    private void ShowScript_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedScriptEntity() is { } entity) ShowEntityScript(entity);
    }

    private async void ShowEntityScript(MapEntity entity)
    {
        if (_dialogs.BlocksInput) return;
        FinishGestures();
        if (!_session.Document.Entities.Contains(entity)) return;
        await OpenMapScripts().ShowEntityAsync(entity);
    }

    private string? ShowScriptEntitiesInMap(IReadOnlyList<MapEntity> entities)
    {
        if (_dialogs.BlocksInput) return "Close the active dialog before navigating to the map.";
        long revision = _session.ContentRevision;
        FinishGestures();
        if (revision != _session.ContentRevision || entities.Any(entity => !_session.Document.Entities.Contains(entity)))
            return "The map changed. Refresh its scripts before navigating.";
        if (entities.Any(entity => !ReferenceEquals(entity, _session.Document.World) &&
            !_session.Visibility.CanSelect(_session.Document, entity)))
            return "A linked entity is hidden or locked. Make it selectable in the map, then choose Show in map again.";
        _session.SelectRange(entities);
        const float focusPadding = 128;
        Workspace.Camera.FrameSelection(focusPadding);
        foreach (var view in Workspace.GridViews) view.FrameSelection(focusPadding);
        Activate();
        SetStatus(entities.Count == 1 ? "Selected the script's source entity in the map."
            : $"Selected {entities.Count} source entities in the map.");
        return null;
    }
}
