using Avalonia.Controls;
using Avalonia.Interactivity;
using IW4.Game.Zone;
using IW4.Studio.Desktop.Editors.AssetReferences;
using IW4.Studio.Desktop.Editors.Inspector;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.Tracer;

public sealed partial class TracerEditorView : UserControl
{
    private bool _stacked;

    public TracerEditorView()
    {
        InitializeComponent();
        Panes.SizeChanged += (_, args) =>
        {
            bool stacked = args.NewSize.Width < 695;
            if (stacked == _stacked) return;
            _stacked = stacked;
            Panes.ColumnDefinitions = new ColumnDefinitions(stacked ? "*" : "*,275");
            Panes.RowDefinitions = new RowDefinitions(stacked ? "Auto,Auto" : "Auto");
            Grid.SetColumn(PropertiesPane, stacked ? 0 : 1);
            Grid.SetRow(PropertiesPane, stacked ? 1 : 0);
        };
    }

    internal AssetReferencePickerService? MaterialPicker
    {
        get;
        init
        {
            field = value;
            BrowseMaterial.IsEnabled = value is not null;
        }
    }

    private async void ChooseMaterial_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TracerEditorViewModel { IsEditable: true } model ||
            MaterialPicker is not { } picker || TopLevel.GetTopLevel(this) is not Window owner)
            return;
        string? name = await picker.SelectNameAsync(owner, XAssetType.Material, model.MaterialName);
        if (name is not null) model.SelectMaterial(name);
    }

    private void ClearMaterial_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as TracerEditorViewModel)?.SelectMaterial(null);

    private async void Color_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: InspectorColorPropertyRowViewModel { IsEditable: true } row } ||
            TopLevel.GetTopLevel(this) is not Window owner)
            return;
        var original = row.CurrentValue;
        var picker = new InspectorColorPickerWindow(row.Label, original);
        var displayed = picker.SelectedColor;
        if (await picker.ShowDialog<InspectorColorValue?>(owner) is not { } picked || picked == displayed)
            return;

        // Keep full native precision in channels the 8-bit picker didn't change.
        row.SetValue(new InspectorColorValue(
            picked.Red == displayed.Red ? original.Red : picked.Red,
            picked.Green == displayed.Green ? original.Green : picked.Green,
            picked.Blue == displayed.Blue ? original.Blue : picked.Blue,
            picked.Alpha == displayed.Alpha ? original.Alpha : picked.Alpha));
    }

    private void Apply_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as TracerEditorViewModel)?.Apply();

    private void Revert_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as TracerEditorViewModel)?.Revert();
}
