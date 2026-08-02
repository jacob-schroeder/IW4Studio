using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace IW4.Studio.Desktop.Workbench.Tools.MapEditor;

public sealed partial class MapEditorToolView : UserControl
{
    public MapEditorToolView() => AvaloniaXamlLoader.Load(this);

    private void OpenMapEditorButton_Click(
        object? sender,
        RoutedEventArgs args) =>
        (DataContext as MapEditorToolViewModel)?.RequestLaunch();
}
