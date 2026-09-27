using Avalonia.Interactivity;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;

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
            if (kind == BrushKind.BreakableGlass)
            {
                MapDocument document = _session.Document;
                if (brushes.Any(brush => !document.World.Brushes.Contains(brush)))
                    throw new InvalidOperationException("Breakable glass requires world brushes. Remove brush entities from the selection.");
                string[] intactMaterials = brushes.Select(brush => BrushGlass.ReadPane(brush).Face.Face.Material)
                    .Distinct(StringComparer.Ordinal).ToArray();
                var existing = brushes.Select(brush => BrushGlass.IsGlass(brush) ? BrushGlass.Read(brush) : null)
                    .Distinct().ToArray();
                var initial = existing.Length == 1 ? existing[0] : null;
                MaterialPickerOption[] materials = Workspace.Materials.AvailableMaterialOptions;
                string? libraryRoot = Workspace.Materials.LibraryRoot ?? _buildEmitterAssetsPath ??
                    (_session.FilePath is { } mapPath ? FindEmitterAssetDirectory(mapPath) : null);
                string? bootstrapRoot = FindBootstrapAssets();
                var names = await _dialogs.ShowModalAsync(async () =>
                {
                    string[] physicsPresets = await Task.Run(() =>
                        GlassPhysicsPresets.ReadAvailable(libraryRoot, bootstrapRoot));
                    if (!ReferenceEquals(_session.Document, document)) return null;
                    return await new BreakableGlassWindow(brushes.Length, intactMaterials, materials, physicsPresets,
                        initial?.ShatteredMaterial, initial?.PhysPreset)
                        .ShowDialog<(string ShatteredMaterial, string PhysPreset)?>(this);
                });
                Workspace.FocusActiveView();
                if (names is null || !ReferenceEquals(_session.Document, document)) return;
                if (brushes.Any(brush => !document.World.Brushes.Contains(brush)))
                    throw new InvalidOperationException("Breakable glass requires world brushes.");
                foreach (MapBrush brush in brushes) BrushGlass.ReadPane(brush);
                _session.Edit(() =>
                {
                    foreach (MapBrush brush in brushes)
                    {
                        BrushContents.Set(brush, kind);
                        BrushGlass.Set(brush, names.Value.ShatteredMaterial, names.Value.PhysPreset);
                    }
                });
                SetStatus($"Set {brushes.Length} brush(es) to breakable glass.");
            }
            else
            {
                _session.Edit(() => { foreach (MapBrush brush in brushes) BrushContents.Set(brush, kind); });
                SetStatus($"Set {brushes.Length} brush(es) to {kind}. Saved as native .map contents.");
            }
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
        { await _dialogs.MessageAsync("Brush contents", exception.Message); }
    }
}
