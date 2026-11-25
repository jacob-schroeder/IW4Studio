using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace IW4.Studio.Desktop.Workbench.Tools.ZoneDetails;

public sealed partial class ZoneDetailsToolView : UserControl
{
    public ZoneDetailsToolView() => AvaloniaXamlLoader.Load(this);

    private async void EditScriptStrings_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ZoneDetailsToolViewModel details && TopLevel.GetTopLevel(this) is Window owner)
        {
            var editor = new ScriptStringsWindow(details);
            await editor.ShowDialog(owner);
        }
    }
}
