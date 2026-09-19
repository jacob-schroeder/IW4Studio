using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using IW4.Studio.Documents.MenuEditing.Behavior;

namespace IW4.Studio.Desktop.Editors.Menu.Behavior;

/// <summary>
/// Modal host for one isolated MenuDef or ItemDef behavior draft. Acceptance
/// exposes the resulting immutable behavior value; document mutation remains
/// the caller's responsibility.
/// </summary>
public sealed partial class MenuItemBehaviorBuilderWindow : Window
{
    private readonly MenuItemBehaviorBuilderSessionViewModel _session;
    private readonly Action<MenuItemBehaviorBindings>? _applyItemResult;
    private readonly Action<MenuDefinitionBehaviorBindings>? _applyMenuResult;
    private bool _closeApproved;

    public MenuItemBehaviorBuilderWindow()
        : this(
            new MenuItemBehaviorBuilderSessionViewModel(),
            (Action<MenuItemBehaviorBindings>?)null)
    {
    }

    public MenuItemBehaviorBuilderWindow(
        MenuItemBehaviorBuilderSessionViewModel session,
        Action<MenuItemBehaviorBindings>? applyResult = null)
        : this(session, applyResult, applyMenuResult: null)
    {
    }

    internal MenuItemBehaviorBuilderWindow(
        MenuItemBehaviorBuilderSessionViewModel session,
        Action<MenuDefinitionBehaviorBindings>? applyResult)
        : this(
            session,
            applyItemResult: null,
            applyMenuResult: applyResult)
    {
    }

    private MenuItemBehaviorBuilderWindow(
        MenuItemBehaviorBuilderSessionViewModel session,
        Action<MenuItemBehaviorBindings>? applyItemResult,
        Action<MenuDefinitionBehaviorBindings>? applyMenuResult)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _applyItemResult = applyItemResult;
        _applyMenuResult = applyMenuResult;
        InitializeComponent();
        Icon = AppIcon.Create();
        DataContext = _session;
        Opened += (_, _) => NavigationList.Focus();
    }

    public MenuItemBehaviorBindings? Result { get; private set; }

    private void ApplyButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (!_session.TryBeginApply())
            return;

        try
        {
            if (_session.IsMenuDefinition)
            {
                if (!_session.TryGetMenuResult(
                        out MenuDefinitionBehaviorBindings? result) ||
                    result is null)
                {
                    _session.CompleteApplyFailure(
                        "The behavior draft could not be validated.");
                    return;
                }

                _applyMenuResult?.Invoke(result);
            }
            else
            {
                if (!_session.TryGetResult(
                        out MenuItemBehaviorBindings? result) ||
                    result is null)
                {
                    _session.CompleteApplyFailure(
                        "The behavior draft could not be validated.");
                    return;
                }

                _applyItemResult?.Invoke(result);
                Result = result;
            }
        }
        catch (Exception exception) when (exception is
                   ArgumentException or
                   InvalidOperationException or
                   InvalidDataException or
                   KeyNotFoundException or
                   OverflowException)
        {
            _session.CompleteApplyFailure(exception.Message);
            return;
        }

        _session.CompleteApplySuccess();
        _closeApproved = true;
        Close(true);
    }

    private void CancelButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (!_session.RequestCancel())
            return;

        CloseCancelled();
    }

    private void KeepEditingButton_Click(
        object? sender,
        RoutedEventArgs e) =>
        _session.KeepEditing();

    private void DiscardButton_Click(
        object? sender,
        RoutedEventArgs e)
    {
        _session.ConfirmDiscard();
        CloseCancelled();
    }

    private void Window_KeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
            e.Key == Key.Enter)
        {
            ApplyButton_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Escape)
            return;

        if (_session.RequestCancel())
            CloseCancelled();
        e.Handled = true;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_closeApproved && _session.HasUnsavedChanges)
        {
            e.Cancel = true;
            _ = _session.RequestCancel();
        }

        base.OnClosing(e);
    }

    private void CloseCancelled()
    {
        _closeApproved = true;
        Close(false);
    }
}
