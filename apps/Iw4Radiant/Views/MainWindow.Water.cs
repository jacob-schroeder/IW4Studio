using Avalonia.Interactivity;
using Iw4Radiant.Editing;
using Iw4Radiant.Materials;
using Iw4Radiant.MapSource;
using Iw4Radiant.Viewports.Orthographic;

namespace Iw4Radiant.Views;

public partial class MainWindow
{
    private async void DrawWater_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogs.BlocksInput || _previewBspPath is not null) return;
        FinishGestures();
        MapDocument document = _session.Document;
        try
        {
            MaterialSource? material = await ChooseWaterAsync("Create water volume", _session.Material);
            if (material is null || !ReferenceEquals(_session.Document, document)) return;
            if (!Workspace.Materials.UseMaterial(_session, material))
                throw new InvalidOperationException("This water appearance could not be loaded. Choose another appearance.");
            _session.SelectionVolumeMode = SelectionVolumeMode.None;
            SetTool(EditorTool.Select);
            _session.Select(null);
            Workspace.ShowGrid(OrthoPlane.Top);
            SetStatus("Drag in the top view to draw water · Base sets the bottom; Depth sets the water level above it.");
            Workspace.FocusActiveView();
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
        { await _dialogs.MessageAsync("Create water volume", exception.Message); }
    }

    private async void ApplyWaterVolume() => await ApplyWaterVolumeAsync(completeExisting: false);
    private async void CompleteWaterVolume() => await ApplyWaterVolumeAsync(completeExisting: true);

    private async Task ApplyWaterVolumeAsync(bool completeExisting)
    {
        if (_dialogs.BlocksInput || _previewBspPath is not null) return;
        FinishGestures();
        MapBrush[] brushes = WaterEditing.GetBrushes(_session);
        if (brushes.Length == 0) return;
        MapDocument document = _session.Document;
        try
        {
            string[] waterNames = brushes.SelectMany(brush => brush.Faces)
                .Select(face => face.Material).Distinct(StringComparer.Ordinal)
                .Where(name => ResolveMaterial(name)?.IsWater == true).ToArray();
            MaterialSource? material = completeExisting && waterNames.Length == 1
                ? ResolveMaterial(waterNames[0])
                : await ChooseWaterAsync("Water appearance", waterNames.FirstOrDefault() ?? _session.Material);
            if (material is null || !ReferenceEquals(_session.Document, document)) return;
            WaterEditing.Apply(_session, brushes, material);
            ShowInspectorSection(Inspector.ShowWater);
            SetStatus(brushes.Length == 1 ? "Water volume ready · Change its appearance and motion in the inspector."
                : $"{brushes.Length} water volumes ready · Change their appearance and motion in the inspector.");
            Workspace.FocusActiveView();
        }
        catch (Exception exception) when (FileOperationErrors.IsExpected(exception))
        { await _dialogs.MessageAsync("Water volume", exception.Message); }
    }

    private async Task<MaterialSource?> ChooseWaterAsync(string title, string? initialName)
    {
        MaterialPickerOption[] options = WaterOptions();
        if (options.Length == 0)
        {
            if (await _dialogs.ChoiceAsync("Water appearances", "Choose an asset library containing water to get started.",
                    ["Choose asset library…", "Cancel"]) != "Choose asset library…") return null;
            if (!await Workspace.Materials.BrowseAsync(this, _session, _dialogs, FinishGestures, SetStatus)) return null;
            options = WaterOptions();
            if (options.Length == 0)
            {
                await _dialogs.MessageAsync("Water appearances", "No water appearances are available in this library.");
                return null;
            }
        }
        if (initialName is not null && !options.Any(option => option.Name == initialName) &&
            ResolveMaterial(initialName) is { IsWater: true } initial)
            options = [new(initialName, initial, null), .. options];
        string selected = options.FirstOrDefault(option => option.Name == initialName)?.Name ?? options[0].Name;
        string? name = await _dialogs.ShowModalAsync(() =>
            MaterialPickerDialog.ShowAsync(this, title, options, selected, waterAppearances: true));
        return name is null ? null : options.First(option => option.Name == name).Material;

        MaterialPickerOption[] WaterOptions() => Workspace.Materials.AvailableMaterialOptions
            .Where(option => option.Material?.IsWater == true)
            .OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
