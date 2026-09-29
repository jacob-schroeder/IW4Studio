using Avalonia.Interactivity;
using Iw4Radiant.Editing;
using Iw4Radiant.Viewports.Camera;

namespace Iw4Radiant.Views;

public partial class MainWindow
{
    private async void CreateModelPlayerClip_Click(object? sender, RoutedEventArgs e) =>
        await CreateModelPlayerClipAsync();

    private async Task CreateModelPlayerClipAsync()
    {
        if (_dialogs.BlocksInput || _previewBspPath is not null) return;
        FinishGestures();
        try
        {
            _dialogs.SetBusy(true);
            SetStatus("Fitting player collision to the selected models…");
            if (!CameraWalkSimulation.FoundationAvailable)
                throw new InvalidOperationException("The physics library could not initialize. You can still draw player-clip brushes manually.");
            var result = await PlayerClipEditing.GenerateFromModelsAsync(_session);
            string simplified = result.SimplifiedCount > 0
                ? $" Simplified {result.SimplifiedCount} dense {(result.SimplifiedCount == 1 ? "hull" : "hulls")} to fit collision limits." : "";
            string fallback = result.FallbackCount > 0
                ? $" {result.FallbackCount} {(result.FallbackCount == 1 ? "model had" : "models had")} no native collision source; used an outer visual hull." : "";
            SetStatus($"Created {result.Count} editable player-clip {(result.Count == 1 ? "brush" : "brushes")}.{simplified}{fallback} Adjust the selected brushes as needed. Undo removes this edit.");
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception) ||
            exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            _dialogs.SetBusy(false);
            SetStatus($"Could not create model player collision: {exception.Message}");
            await _dialogs.MessageAsync("Cannot create player clip", exception.Message);
        }
        finally { _dialogs.SetBusy(false); }
    }

    private void DrawPlayerClip_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogs.BlocksInput) return;
        SetTool(EditorTool.Select);
        _session.Select(null);
        Workspace.ShowGrid(Workspace.ActivePlane);
        Workspace.Materials.UsePlayerClip(_session);
        SetStatus("Draw player clip in a grid view · Set Base and Depth, then shape the brush around the model · Invisible in game; blocks players");
        Workspace.FocusActiveView();
    }

    private async void ApplyPlayerClip_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogs.BlocksInput) return;
        FinishGestures();
        try
        {
            PlayerClipEditing.Apply(_session);
            Workspace.Materials.UsePlayerClip(_session);
            SetStatus("Selected brushes now block players and are invisible in game. Magenta outlines show the editable clip volumes.");
        }
        catch (ArgumentException exception)
        {
            await _dialogs.MessageAsync("Player clip", exception.Message);
        }
    }
}
