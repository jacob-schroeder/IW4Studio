using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using IW4.Studio.Desktop.ViewModels.Menu;
using IW4.Studio.Documents.MenuEditing;

namespace IW4.Studio.Desktop.Editors.Menu;

public sealed partial class MenuPlayWindow : Window
{
    private readonly MenuPlaySessionViewModel? _session;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(33)
    };
    private IStorageFile? _pickedConfig;
    private bool _isClosed;

    public MenuPlayWindow()
    {
        InitializeComponent();
        Icon = AppIcon.Create();
    }

    internal MenuPlayWindow(MenuDesignerViewModel designer, MenuEditorSnapshot snapshot)
        : this()
    {
        Designer = designer;
        Snapshot = snapshot;
        _session = new MenuPlaySessionViewModel(designer, snapshot);
        DataContext = _session;
        Stage.AddHandler(
            PointerPressedEvent,
            Stage_PointerPressed,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        _timer.Tick += (_, _) => _session.Advance();
        designer.Disposed += Designer_Disposed;
    }

    internal MenuDesignerViewModel? Designer { get; }
    internal MenuEditorSnapshot? Snapshot { get; }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Stage.Focus();
        if (_session is not null)
            _timer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosed = true;
        _timer.Stop();
        _pickedConfig?.Dispose();
        _pickedConfig = null;
        if (Designer is { } designer)
            designer.Disposed -= Designer_Disposed;
        base.OnClosed(e);
    }

    private void Designer_Disposed(object? sender, EventArgs e) => Close();

    private void SettingsToggle_Click(object? sender, RoutedEventArgs e)
    {
        if (SettingsToggle.IsChecked != true && _session?.IsMenuClosed == false)
            Stage.Focus();
    }

    private void Stage_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_session is { } session)
        {
            session.HandlePointerMove(Stage.HitTestNode(
                e.GetPosition(Stage), session.CanTargetItem));
        }
    }

    private void Stage_PointerExited(object? sender, PointerEventArgs e) =>
        _session?.HandlePointerMove(null);

    private void Stage_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_session is not { } session ||
            e.GetCurrentPoint(Stage).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
            return;
        Stage.Focus();
        if (Stage.HitTestNode(e.GetPosition(Stage), session.CanTargetItem) is { } item)
        {
            session.HandlePointerClick(item, e.ClickCount > 1);
        }
    }

    private void Stage_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_session is not { } session ||
            (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0)
            return;

        // The PS3 Menu handler remaps authored face buttons 1/2 to accept/back.
        // Enter and Escape represent those logical controls in the desktop preview.
        switch (e.Key)
        {
            case Key.Up:
                session.MoveFocus(-1);
                break;
            case Key.Down:
                session.MoveFocus(1);
                break;
            case Key.Enter:
                session.HandleButton(1);
                break;
            case Key.Escape:
                session.HandleButton(2);
                break;
            case Key.X:
                session.HandleButton(3);
                break;
            case Key.Y:
                session.HandleButton(4);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private async void LoadConfig_Click(object? sender, RoutedEventArgs e)
    {
        if (_session is not { } session)
            return;

        IStorageFile? selection = null;
        SessionActions.IsEnabled = false;
        try
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "Load menu preview config",
                    FileTypeFilter = [new FilePickerFileType("IW4 config") { Patterns = ["*.cfg"] }]
                });
            selection = files.FirstOrDefault();
            if (selection is null || _isClosed || !await ReadConfigAsync(session, selection))
                return;

            _pickedConfig?.Dispose();
            _pickedConfig = selection;
            selection = null;
        }
        catch (Exception exception) when (IsConfigReadError(exception))
        {
            if (!_isClosed)
                session.ReportConfigError(selection?.Name ?? "the selected config", exception.Message);
        }
        finally
        {
            selection?.Dispose();
            FinishConfigAction();
        }
    }

    private async void ReloadDefaults_Click(object? sender, RoutedEventArgs e)
    {
        if (_session is not { } session)
            return;

        if (_pickedConfig is not { } file)
        {
            session.ReloadWorkspaceDefaults();
            Stage.Focus();
            return;
        }

        SessionActions.IsEnabled = false;
        try
        {
            await ReadConfigAsync(session, file);
        }
        catch (Exception exception) when (IsConfigReadError(exception))
        {
            if (!_isClosed)
                session.ReportConfigError(file.Name, exception.Message);
        }
        finally
        {
            FinishConfigAction();
        }
    }

    private async Task<bool> ReadConfigAsync(MenuPlaySessionViewModel session, IStorageFile file)
    {
        await using Stream stream = await file.OpenReadAsync();
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        string text = await reader.ReadToEndAsync();
        if (_isClosed)
            return false;

        session.LoadPickedConfig(file.Name, text);
        return true;
    }

    private void FinishConfigAction()
    {
        if (_isClosed)
            return;

        SessionActions.IsEnabled = true;
        Stage.Focus();
    }

    private static bool IsConfigReadError(Exception exception) =>
        exception is IOException or InvalidDataException or UnauthorizedAccessException or
            ArgumentException or InvalidOperationException or NotSupportedException;

    private void Restart_Click(object? sender, RoutedEventArgs e)
    {
        _session?.Restart();
        Stage.Focus();
    }

    private void OpenRegistration_Click(object? sender, RoutedEventArgs e)
    {
        _session?.OpenSelectedRegistration();
        Stage.Focus();
    }

    private void ScenarioInput_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: MenuPreviewScenarioInputViewModel input })
            _ = input.CommitPendingValue();
    }

    private void ScenarioInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Control { DataContext: MenuPreviewScenarioInputViewModel input })
            return;

        if (e.Key == Key.Enter)
        {
            _ = input.CommitPendingValue();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            input.ResetPendingValue();
            e.Handled = true;
        }
    }
}
