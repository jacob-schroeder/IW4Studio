using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents.MenuEditing.Behavior;
using IW4.Studio.Documents.MenuEditing.Behavior.Expressions;

namespace IW4.Studio.Desktop.Editors.Menu.Behavior;

/// <summary>
/// Owns a single, modal-local behavior draft. Child editors never mutate a
/// document; <see cref="TryGetResult"/> builds one immutable value for the
/// caller to apply atomically at its own document boundary.
/// </summary>
public sealed class MenuItemBehaviorBuilderSessionViewModel : ObservableObject
{
    private readonly MenuItemBehaviorBindings _source;
    private readonly MenuItemBehaviorValidator _behaviorValidator;
    private MenuItemBehaviorBuilderNavigationItemViewModel _selectedNavigationItem;
    private bool _hasUnsavedChanges;
    private string _statusMessage = "No local behavior changes.";
    private string? _validationMessage;
    private string? _externalValidationMessage;
    private bool _hasLocalValidationError;
    private bool _isSubmitting;
    private bool _isDiscardConfirmationVisible;

    public MenuItemBehaviorBuilderSessionViewModel(
        MenuItemBehaviorBindings? bindings = null,
        string? scopeText = null,
        BehaviorExpressionSupport? expressionSupport = null,
        bool supportsListBoxDoubleClick = true)
    {
        _source = bindings ?? MenuItemBehaviorBindings.Empty;
        ScopeText = string.IsNullOrWhiteSpace(scopeText)
            ? "Selected ItemDef"
            : scopeText;
        BehaviorExpressionSupport support = expressionSupport ??
            BehaviorExpressionSupport.Empty;
        ExpressionSupport = new BehaviorExpressionSupportDraftViewModel(
            support,
            ChildChanged);
        _behaviorValidator = new MenuItemBehaviorValidator(
            new MenuBehaviorExpressionCodec(null));
        Events = new BehaviorEventHooksViewModel(
            _source,
            ExpressionSupport,
            supportsListBoxDoubleClick,
            ChildChanged);
        Keys = new BehaviorKeyHandlersViewModel(
            _source.KeyHandlers, ExpressionSupport, ChildChanged);
        Bindings = new BehaviorBindingsViewModel(
            _source.Expressions, ExpressionSupport, ChildChanged);

        NavigationItems = Array.AsReadOnly(
        [
            new MenuItemBehaviorBuilderNavigationItemViewModel(
                MenuItemBehaviorBuilderSection.Events,
                "Events",
                "Event hooks and nested action sets.",
                Events.Summary),
            new MenuItemBehaviorBuilderNavigationItemViewModel(
                MenuItemBehaviorBuilderSection.Keys,
                "Keys",
                "Ordered key handlers and their action sets.",
                Keys.Summary),
            new MenuItemBehaviorBuilderNavigationItemViewModel(
                MenuItemBehaviorBuilderSection.Bindings,
                "Bindings",
                "Fixed statements and float-expression bindings.",
                Bindings.Summary)
        ]);
        _selectedNavigationItem = NavigationItems[0];
        _selectedNavigationItem.IsSelected = true;
        RefreshValidation();
    }

    public string ScopeText { get; }

    public BehaviorEventHooksViewModel Events { get; }

    public BehaviorKeyHandlersViewModel Keys { get; }

    public BehaviorBindingsViewModel Bindings { get; }

    public BehaviorExpressionSupportDraftViewModel ExpressionSupport { get; }

    public IReadOnlyList<MenuItemBehaviorBuilderNavigationItemViewModel>
        NavigationItems { get; }

    public MenuItemBehaviorBuilderNavigationItemViewModel
        SelectedNavigationItem
    {
        get => _selectedNavigationItem;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!SetProperty(ref _selectedNavigationItem, value))
                return;

            foreach (MenuItemBehaviorBuilderNavigationItemViewModel item in
                     NavigationItems)
            {
                item.IsSelected = ReferenceEquals(item, value);
            }

            OnPropertyChanged(nameof(IsEventsSelected));
            OnPropertyChanged(nameof(IsKeysSelected));
            OnPropertyChanged(nameof(IsBindingsSelected));
        }
    }

    public bool IsEventsSelected => SelectedNavigationItem.Section ==
        MenuItemBehaviorBuilderSection.Events;

    public bool IsKeysSelected => SelectedNavigationItem.Section ==
        MenuItemBehaviorBuilderSection.Keys;

    public bool IsBindingsSelected => SelectedNavigationItem.Section ==
        MenuItemBehaviorBuilderSection.Bindings;

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set
        {
            if (!SetProperty(ref _hasUnsavedChanges, value))
                return;

            OnPropertyChanged(nameof(CanApply));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string? ValidationMessage
    {
        get => _validationMessage;
        private set
        {
            if (!SetProperty(ref _validationMessage, value))
                return;

            OnPropertyChanged(nameof(HasValidationError));
            OnPropertyChanged(nameof(CanApply));
        }
    }

    public bool HasValidationError => !string.IsNullOrWhiteSpace(
        ValidationMessage);

    public bool IsSubmitting
    {
        get => _isSubmitting;
        private set
        {
            if (!SetProperty(ref _isSubmitting, value))
                return;

            OnPropertyChanged(nameof(CanApply));
        }
    }

    /// <summary>
    /// Host failures remain visible, but a valid unchanged local draft can be
    /// retried after the host has refreshed its document state.
    /// </summary>
    public bool CanApply => HasUnsavedChanges && !IsSubmitting &&
        !_hasLocalValidationError;

    public bool IsDiscardConfirmationVisible
    {
        get => _isDiscardConfirmationVisible;
        private set => SetProperty(ref _isDiscardConfirmationVisible, value);
    }

    /// <summary>
    /// The invoking document editor can layer contextual validation over the
    /// local draft without being given any mutable draft state.
    /// </summary>
    public void SetValidationMessage(string? message)
    {
        _externalValidationMessage = string.IsNullOrWhiteSpace(message)
            ? null
            : message;
        RefreshValidation();
    }

    /// <summary>Starts one atomic apply attempt and prevents re-entry.</summary>
    public bool TryBeginApply()
    {
        if (IsSubmitting)
            return false;

        // The host message describes the previous submission. Retrying must
        // re-evaluate the exact same local draft against the current host.
        _externalValidationMessage = null;
        RefreshValidation();
        if (!CanApply)
        {
            StatusMessage = _hasLocalValidationError
                ? "Resolve the validation errors before applying."
                : "Make a local behavior change before applying.";
            return false;
        }

        IsSubmitting = true;
        StatusMessage = "Applying behavior changes…";
        return true;
    }

    public void CompleteApplySuccess() => IsSubmitting = false;

    public void CompleteApplyFailure(string message)
    {
        IsSubmitting = false;
        SetValidationMessage(message);
        StatusMessage = "The host rejected the behavior change. Edit it or retry.";
    }

    /// <summary>Creates a complete immutable behavior value if the draft is valid.</summary>
    public bool TryGetResult(out MenuItemBehaviorBindings? result)
    {
        RefreshValidation();
        if (HasValidationError)
        {
            result = null;
            return false;
        }

        result = BuildResult();
        return true;
    }

    /// <summary>Returns true only when the caller may close the modal.</summary>
    public bool RequestCancel()
    {
        if (!HasUnsavedChanges)
            return true;

        IsDiscardConfirmationVisible = true;
        StatusMessage = "Discard the local behavior changes or continue editing.";
        return false;
    }

    public void KeepEditing()
    {
        IsDiscardConfirmationVisible = false;
        StatusMessage = "Continue editing local behavior changes.";
    }

    public void ConfirmDiscard()
    {
        IsDiscardConfirmationVisible = false;
        StatusMessage = "Local behavior changes discarded.";
    }

    private void ChildChanged()
    {
        // Compiler/staleness failures describe the last submitted value. Once
        // the local draft changes, let the next atomic apply re-evaluate it.
        _externalValidationMessage = null;
        RefreshNavigationSummaries();
        RefreshValidation();
        HasUnsavedChanges = true;
        IsDiscardConfirmationVisible = false;
        StatusMessage = "Local behavior changes have not been applied.";
    }

    private void RefreshValidation()
    {
        var messages = new List<string>();
        messages.AddRange(Events.Validate());
        messages.AddRange(Keys.Validate());
        messages.AddRange(Bindings.Validate());
        messages.AddRange(_behaviorValidator
            .Validate(BuildResult(), MenuBehaviorValidationMode.Authored)
            .Where(issue => issue.Severity == MenuBehaviorValidationSeverity.Error)
            .Select(issue => $"{issue.Path}: {issue.Message}"));
        bool hasLocalValidationError = messages.Count != 0;
        if (SetProperty(
                ref _hasLocalValidationError,
                hasLocalValidationError,
                nameof(_hasLocalValidationError)))
        {
            OnPropertyChanged(nameof(CanApply));
        }
        if (!string.IsNullOrWhiteSpace(_externalValidationMessage))
            messages.Add(_externalValidationMessage);
        ValidationMessage = messages.Count == 0
            ? null
            : string.Join(Environment.NewLine, messages);
    }

    private void RefreshNavigationSummaries()
    {
        foreach (MenuItemBehaviorBuilderNavigationItemViewModel item in
                 NavigationItems)
        {
            item.Summary = item.Section switch
            {
                MenuItemBehaviorBuilderSection.Events => Events.Summary,
                MenuItemBehaviorBuilderSection.Keys => Keys.Summary,
                MenuItemBehaviorBuilderSection.Bindings => Bindings.Summary,
                _ => item.Summary
            };
        }
    }

    private MenuItemBehaviorBindings BuildResult()
    {
        MenuItemBehaviorBindings withEvents = Events.ApplyTo(_source);
        return withEvents with
        {
            KeyHandlers = Keys.ToDomain(),
            Expressions = Bindings.ToDomain(),
            ExpressionSupportDelta = ExpressionSupport.ToDelta()
        };
    }
}
