using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Iw4Radiant.Editing;

namespace Iw4Radiant.Views;

public partial class MainWindow
{
    private LeakPath? _leakPath;
    private int _leakPointIndex;

    private async void ImportLeakPath_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogs.BlocksInput) return;
        var files = await _dialogs.ShowModalAsync(() => StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Radiant leak path (.lin)", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Radiant pointfile (.lin)") { Patterns = ["*.lin"] }]
        }));
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        if (!string.Equals(Path.GetExtension(path), ".lin", StringComparison.OrdinalIgnoreCase))
        {
            await _dialogs.MessageAsync("Unsupported leak path", "Import a Radiant .lin pointfile: one X Y Z point per line.");
            return;
        }
        try
        {
            _dialogs.SetBusy(true);
            LeakPath imported = await Task.Run(() => LeakPath.Read(path));
            _leakPath = imported;
            _leakPointIndex = 0;
            UpdateLeakPathViews();
            FrameLeakPoint();
            SetStatus($"Imported {imported.FileName}. This displays a supplied leak path; it does not detect leaks.");
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
        {
            _dialogs.SetBusy(false);
            await _dialogs.MessageAsync("Cannot import .lin leak path", exception.Message);
        }
        finally { _dialogs.SetBusy(false); }
    }

    private void LeakPrevious_Click(object? sender, RoutedEventArgs e) => StepLeakPoint(-1);
    private void LeakNext_Click(object? sender, RoutedEventArgs e) => StepLeakPoint(1);
    private void LeakCurrent_Click(object? sender, RoutedEventArgs e) => FrameLeakPoint();
    private void LeakClear_Click(object? sender, RoutedEventArgs e)
    {
        ClearLeakPath();
        SetStatus("Imported leak path cleared.");
    }

    private void StepLeakPoint(int direction)
    {
        if (_leakPath is null) return;
        _leakPointIndex = Math.Clamp(_leakPointIndex + direction, 0, _leakPath.Points.Count - 1);
        UpdateLeakPathViews();
        FrameLeakPoint();
    }

    private void FrameLeakPoint()
    {
        if (_leakPath is null) return;
        var point = _leakPath.Points[_leakPointIndex];
        Workspace.Camera.FramePoint(point);
        foreach (var view in Workspace.GridViews) view.FramePoint(point);
    }

    private void UpdateLeakPathViews()
    {
        Workspace.Camera.LeakPath = _leakPath;
        Workspace.Camera.LeakPointIndex = _leakPointIndex;
        foreach (var view in Workspace.GridViews)
        {
            view.LeakPath = _leakPath;
            view.LeakPointIndex = _leakPointIndex;
        }
        LeakPathControls.IsVisible = _leakPath is not null;
        if (_leakPath is not { } path) return;
        LeakPathLabel.Text = $"Leak path · {_leakPointIndex + 1} of {path.Points.Count}";
        Avalonia.Controls.ToolTip.SetTip(LeakPathLabel, path.FileName);
        LeakPreviousButton.IsEnabled = _leakPointIndex > 0;
        LeakNextButton.IsEnabled = _leakPointIndex < path.Points.Count - 1;
    }

    private void ClearLeakPath()
    {
        if (_leakPath is null) return;
        _leakPath = null;
        _leakPointIndex = 0;
        UpdateLeakPathViews();
    }
}
