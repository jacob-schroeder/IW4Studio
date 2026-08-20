using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Views;

public sealed partial class AboutWindow : Window
{
    private readonly AboutViewModel _viewModel = new();
    private string RepositoryUrl => AssemblyConst.RepositoryUrl;
    
    public AboutWindow()
    {
        InitializeComponent();
        Icon = AppIcon.Create();
        DataContext = _viewModel;
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();

    private void OnRepoUrlClick(object? sender, RoutedEventArgs e)
    {
        OpenUrl(RepositoryUrl);
    }
    
    private void OpenUrl(string url)
    {
        // Cross-platform process launcher for default browser
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Process.Start("xdg-open", url);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start("open", url);
        }
    }
}
