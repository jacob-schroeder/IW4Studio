using Avalonia.Controls;
using Avalonia.Interactivity;

namespace IW4.Studio.Desktop.Views;

public sealed partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        Icon = AppIcon.Create();
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
