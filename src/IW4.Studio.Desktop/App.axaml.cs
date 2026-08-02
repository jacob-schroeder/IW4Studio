using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using IW4.Studio.Desktop.Lifecycle;
using IW4.Studio.Desktop.Themes;
using IW4.Studio.Desktop.Views;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop;

public sealed partial class App : Application
{
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private ThemeService? _themeService;
    private readonly DestructiveNavigationCoordinator _navigationCoordinator = new();
    private bool _approvedShutdownRetry;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        var settingsStore = new AppSettingsStore(
            Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
        _themeService = new ThemeService(this, settingsStore);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.ShutdownRequested += Desktop_ShutdownRequested;
            desktop.Exit += Desktop_Exit;
            desktop.MainWindow = CreateWelcomeWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private WelcomeWindow CreateWelcomeWindow()
    {
        var window = new WelcomeWindow();
        window.WorkspaceOpened += OpenEditor;
        window.ThemeRequested += SelectTheme;
        window.SetThemeMode(GetCurrentThemeMode());
        return window;
    }

    private void OpenEditor(WelcomeWindow welcomeWindow, FastFileWorkspace workspace)
    {
        if (_desktop is null)
            return;

        var editorWindow = new EditorWindow(workspace, _navigationCoordinator);
        editorWindow.WelcomeRequested += ReturnToWelcome;
        editorWindow.ThemeRequested += SelectTheme;
        editorWindow.ApprovedCloseRequested += EditorWindow_ApprovedCloseRequested;
        editorWindow.SetThemeMode(GetCurrentThemeMode());
        _desktop.MainWindow = editorWindow;
        editorWindow.Show();
        welcomeWindow.Close();
    }

    private void ReturnToWelcome(EditorWindow editorWindow)
    {
        if (_desktop is null)
            return;

        WelcomeWindow welcomeWindow = CreateWelcomeWindow();
        _desktop.MainWindow = welcomeWindow;
        welcomeWindow.Show();
        editorWindow.CloseAfterApprovedNavigation(DestructiveNavigationAction.OpenAnother);
    }

    private ThemeMode GetCurrentThemeMode() =>
        _themeService?.CurrentTheme.Mode ?? ThemeMode.Dark;

    private void SelectTheme(ThemeMode mode)
    {
        if (_themeService is null)
            return;

        _themeService.SelectTheme(mode);

        switch (_desktop?.MainWindow)
        {
            case WelcomeWindow welcomeWindow:
                welcomeWindow.SetThemeMode(mode);
                break;
            case EditorWindow editorWindow:
                editorWindow.SetThemeMode(mode);
                break;
        }
    }

    private async void AppAboutMenuItem_Click(object? sender, EventArgs e)
    {
        if (_desktop?.MainWindow is not Window owner)
            return;

        var aboutWindow = new AboutWindow();
        await aboutWindow.ShowDialog(owner);
    }

    private void Desktop_ShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (_approvedShutdownRetry)
        {
            _approvedShutdownRetry = false;
            return;
        }

        if (_desktop?.MainWindow is not EditorWindow editorWindow)
            return;

        // The Avalonia shutdown boundary is synchronous. Stop this attempt,
        // then retry it only after the shared coordinator authorizes it.
        e.Cancel = true;
        _ = RequestShutdownAsync(editorWindow);
    }

    private static void Desktop_Exit(
        object? sender,
        ControlledApplicationLifetimeExitEventArgs e) =>
        SilkMapRenderOpenGlShareGroup.Shutdown();

    private async Task RequestShutdownAsync(EditorWindow editorWindow)
    {
        if (_desktop is null)
            return;

        await editorWindow.RequestApplicationShutdownAsync(() =>
        {
            _approvedShutdownRetry = true;
            editorWindow.PrepareApprovedCloseRetry();
            _desktop.TryShutdown();
            return Task.CompletedTask;
        });
    }

    private void EditorWindow_ApprovedCloseRequested(DestructiveNavigationAction action)
    {
        if (action is DestructiveNavigationAction.Exit or DestructiveNavigationAction.WindowClose)
            _approvedShutdownRetry = true;
    }
}
