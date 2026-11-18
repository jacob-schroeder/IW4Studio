using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace IW4.Studio.Desktop.Editors.Image;

public sealed partial class ImageReplacementWindow : Window
{
    public ImageReplacementWindow() => AvaloniaXamlLoader.Load(this);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
    private void Stage_Click(object? sender, RoutedEventArgs e) => Close(true);
}
