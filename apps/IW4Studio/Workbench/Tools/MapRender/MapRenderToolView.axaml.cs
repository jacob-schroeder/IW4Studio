using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace IW4.Studio.Desktop.Workbench.Tools.MapRender;

public sealed partial class MapRenderToolView : UserControl
{
    public MapRenderToolView() => AvaloniaXamlLoader.Load(this);

    private void OpenRendererButton_Click(object? sender, RoutedEventArgs args) =>
        (DataContext as MapRenderToolViewModel)?.RequestLaunch();
}
