using IW4.Studio.Documents.MenuEditing;
using IW4.Studio.Documents.MenuEditing.Debugging;

namespace IW4.Studio.Desktop.ViewModels.Menu;

/// <summary>
/// Presentation state for explicit debugger dispatch. Options map one-to-one
/// to authored hook sets; this component never invents input routing.
/// </summary>
public sealed class MenuPreviewInteractionViewModel : ObservableObject
{
    private readonly Action<MenuDebugInput> _dispatch;
    private IReadOnlyList<MenuPreviewInteractionOption> _options = [];
    private MenuPreviewInteractionOption? _selectedOption;
    private MenuDebugInput? _focusSelectedInput;
    private string _selectionSummary = "Selected: none";
    private string _activationSummary = "Menu activation produced no changes";
    private string _activationDetails = string.Empty;
    private string _resultSummary = "No event run";
    private string _resultDetails = string.Empty;
    private int _activationIssueCount;
    private int _resultIssueCount;
    private bool _hasActivationDetails;
    private bool _hasResult;

    internal MenuPreviewInteractionViewModel(Action<MenuDebugInput> dispatch)
    {
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        DispatchCommand = new ViewModelCommand(
            DispatchSelected,
            () => SelectedOption is not null);
        FocusSelectedCommand = new ViewModelCommand(
            FocusSelected,
            () => _focusSelectedInput is not null);
    }

    public IReadOnlyList<MenuPreviewInteractionOption> Options => _options;

    public string SelectionSummary => _selectionSummary;

    public MenuPreviewInteractionOption? SelectedOption
    {
        get => _selectedOption;
        set
        {
            if (!SetProperty(ref _selectedOption, value))
                return;
            DispatchCommand.RaiseCanExecuteChanged();
        }
    }

    public ViewModelCommand DispatchCommand { get; }
    public ViewModelCommand FocusSelectedCommand { get; }

    public bool HasActivationDetails
    {
        get => _hasActivationDetails;
        private set => SetProperty(ref _hasActivationDetails, value);
    }

    public string ActivationSummary
    {
        get => _activationSummary;
        private set => SetProperty(ref _activationSummary, value);
    }

    public string ActivationDetails
    {
        get => _activationDetails;
        private set => SetProperty(ref _activationDetails, value);
    }

    public int ActivationIssueCount
    {
        get => _activationIssueCount;
        private set => SetProperty(ref _activationIssueCount, value);
    }

    public int ResultIssueCount
    {
        get => _resultIssueCount;
        private set => SetProperty(ref _resultIssueCount, value);
    }

    public bool HasResult
    {
        get => _hasResult;
        private set => SetProperty(ref _hasResult, value);
    }

    public string ResultSummary
    {
        get => _resultSummary;
        private set => SetProperty(ref _resultSummary, value);
    }

    public string ResultDetails
    {
        get => _resultDetails;
        private set => SetProperty(ref _resultDetails, value);
    }

    internal void ReplaceOptions(
        MenuDebugProgram? program,
        MenuOutlineNodeKind? selectedKind,
        MenuNodeId? selectedNodeId)
    {
        MenuDebugInput? previous = SelectedOption?.Input;
        MenuPreviewInteractionSelection selection =
            MenuPreviewInteractionOptions.Build(
            program,
            selectedKind,
            selectedNodeId);
        _selectionSummary = selection.Summary;
        _focusSelectedInput = selection.FocusInput;
        _options = selection.Events;
        _selectedOption = _options.FirstOrDefault(value =>
                Equals(value.Input, previous)) ??
            _options.FirstOrDefault();
        ClearResult();
        OnPropertyChanged(nameof(Options));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(SelectedOption));
        DispatchCommand.RaiseCanExecuteChanged();
        FocusSelectedCommand.RaiseCanExecuteChanged();
    }

    internal void ShowActivation(MenuDebugDispatchResult result)
    {
        MenuPreviewInteractionResultPresentation presentation =
            MenuPreviewInteractionResultFormatter.Format(result);
        ActivationIssueCount = presentation.IssueCount;
        ActivationDetails = presentation.Details;
        HasActivationDetails = presentation.HasDetails;
        ActivationSummary = HasActivationDetails
            ? $"Menu opened · {presentation.StateChangeCount:N0} state change(s) · " +
              $"{presentation.ScriptCount:N0} command(s) awaiting runtime · " +
              $"{presentation.IssueCount:N0} issue(s)"
            : "Menu opened with no simulated state changes";
    }

    internal void ShowResult(MenuDebugDispatchResult result)
    {
        MenuPreviewInteractionResultPresentation presentation =
            MenuPreviewInteractionResultFormatter.Format(result);
        HasResult = true;
        ResultIssueCount = presentation.IssueCount;
        ResultDetails = presentation.Details;
        ResultSummary =
            $"Event run · {presentation.StateChangeCount:N0} state change(s) · " +
            $"{presentation.ScriptCount:N0} command(s) awaiting runtime · " +
            $"{presentation.IssueCount:N0} issue(s)";
    }

    internal void ClearInteractionState()
    {
        ClearResult();
        ActivationDetails = string.Empty;
        ActivationSummary = "Menu activation produced no changes";
        HasActivationDetails = false;
        ActivationIssueCount = 0;
    }

    private void DispatchSelected()
    {
        if (SelectedOption is { } option)
            _dispatch(option.Input);
    }

    private void FocusSelected()
    {
        if (_focusSelectedInput is { } input)
            _dispatch(input);
    }

    private void ClearResult()
    {
        HasResult = false;
        ResultIssueCount = 0;
        ResultSummary = "No event run";
        ResultDetails = string.Empty;
    }
}
