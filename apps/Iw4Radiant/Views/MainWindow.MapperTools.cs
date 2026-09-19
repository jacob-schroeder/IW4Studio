using Avalonia.Interactivity;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Views;

public partial class MainWindow
{
    private static readonly string[] GridSizes = ["0.25", "0.5", "1", "2", "4", "8", "16", "32", "64", "128", "256", "512"];

    private async void Filters_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogs.BlocksInput) return;
        FinishGestures();
        await _dialogs.ShowModalAsync(() => new MapFiltersWindow(_session).ShowDialog<object?>(this));
        Workspace.FocusActiveView();
    }

    private async void Statistics_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogs.BlocksInput) return;
        FinishGestures();
        await _dialogs.ShowModalAsync(() => new MapStatisticsWindow(_session).ShowDialog<object?>(this));
        Workspace.FocusActiveView();
    }

    private void ChangeGrid(int direction) =>
        GridCombo.SelectedIndex = Math.Clamp(GridCombo.SelectedIndex + direction, 0, GridSizes.Length - 1);

    private async void ApplyBrushKind(BrushKind kind)
    {
        if (_dialogs.BlocksInput) return;
        FinishGestures();
        MapBrush[] brushes = _session.Selection.Items.Select(EditorSelection.Owner)
            .SelectMany(item => item is MapEntity entity ? entity.Brushes : item is MapBrush brush ? [brush] : Enumerable.Empty<MapBrush>())
            .Distinct().Where(brush => _session.Visibility.CanSelect(_session.Document, brush)).ToArray();
        if (brushes.Length == 0) return;
        try
        {
            _session.Edit(() => { foreach (MapBrush brush in brushes) BrushContents.Set(brush, kind); });
            SetStatus($"Set {brushes.Length} brush(es) to {kind}. Saved as native .map contents.");
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
        { await _dialogs.MessageAsync("Brush contents", exception.Message); }
    }
}
