using Avalonia.Controls;
using Avalonia.Interactivity;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.PhysPreset;

public sealed partial class PhysPresetEditorView : UserControl
{
    private bool _stacked;

    public PhysPresetEditorView()
    {
        InitializeComponent();
        Panes.SizeChanged += (_, args) =>
        {
            bool stacked = args.NewSize.Width < 640;
            if (stacked == _stacked) return;
            _stacked = stacked;
            Panes.ColumnDefinitions = new ColumnDefinitions(stacked ? "*" : "*,320");
            Panes.RowDefinitions = new RowDefinitions(stacked ? "Auto,*" : "*");
            Panes.RowSpacing = stacked ? 8 : 0;
            Grid.SetRow(PropertiesPane, stacked ? 1 : 0);
            Grid.SetColumn(PropertiesPane, stacked ? 0 : 1);
        };
    }

    private void Apply_Click(object? sender, RoutedEventArgs args) =>
        (DataContext as PhysPresetEditorViewModel)?.ApplyChanges();

    private void Revert_Click(object? sender, RoutedEventArgs args) =>
        (DataContext as PhysPresetEditorViewModel)?.Revert();
}
