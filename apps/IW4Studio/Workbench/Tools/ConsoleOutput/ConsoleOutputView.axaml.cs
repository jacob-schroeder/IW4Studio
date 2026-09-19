using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace IW4.Studio.Desktop.Workbench.Tools.ConsoleOutput;

public sealed partial class ConsoleOutputView : UserControl
{
    public ConsoleOutputView() => AvaloniaXamlLoader.Load(this);

    private async void CopyDetailMenuItem_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string detail } ||
            TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        e.Handled = true;
        await clipboard.SetTextAsync(detail);
    }
}
