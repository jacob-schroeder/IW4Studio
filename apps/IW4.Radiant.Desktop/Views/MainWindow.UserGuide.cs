using Avalonia.Controls;
using Avalonia.Interactivity;
using IW4.Radiant.Desktop.UserGuide;

namespace IW4.Radiant.Desktop.Views;

public partial class MainWindow
{
    private UserGuideWindow? _userGuideWindow;
    private GuideLibrary? _guideLibrary;

    private async void UserGuide_Click(object? sender, RoutedEventArgs e)
    {
        if (_dialogs.BlocksInput) return;
        FinishGestures();
        if (_userGuideWindow is { } open)
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }
        try
        {
            _guideLibrary ??= new GuideLibrary();
            var window = new UserGuideWindow(_guideLibrary, _settings.UserGuide, _settings.Save,
                () => Help_Click(this, new RoutedEventArgs()));
            _userGuideWindow = window;
            window.Closed += (_, _) => _userGuideWindow = null;
            window.Show(this);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or
            System.Text.Json.JsonException or ArgumentException)
        {
            await _dialogs.MessageAsync("User Guide", $"The bundled guide could not be opened.\n\n{exception.Message}");
        }
    }
}
