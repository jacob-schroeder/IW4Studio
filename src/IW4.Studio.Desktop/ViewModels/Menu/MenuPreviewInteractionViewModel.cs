using IW4.Studio.Documents.MenuEditing;
using IW4.Studio.Documents.MenuEditing.Debugging;

namespace IW4.Studio.Desktop.ViewModels.Menu;

public sealed record MenuPreviewInteractionOption(
    string Label,
    MenuDebugInput Input);

/// <summary>
/// Presentation state for explicit debugger dispatch. Options map one-to-one
/// to authored hook sets; this component never invents input routing.
/// </summary>
public sealed class MenuPreviewInteractionViewModel : ObservableObject
{
    private readonly Action<MenuDebugInput> _dispatch;
    private readonly Action _reset;
    private IReadOnlyList<MenuPreviewInteractionOption> _options = [];
    private MenuPreviewInteractionOption? _selectedOption;
    private IReadOnlyList<string> _traceLines = [];
    private IReadOnlyList<string> _diagnosticLines = [];
    private IReadOnlyList<string> _localChangeLines = [];
    private IReadOnlyList<string> _queuedScripts = [];
    private string _resultSummary = "No interaction dispatched";
    private bool _hasResult;
    private bool _canReset;

    internal MenuPreviewInteractionViewModel(
        Action<MenuDebugInput> dispatch,
        Action reset)
    {
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _reset = reset ?? throw new ArgumentNullException(nameof(reset));
        DispatchCommand = new ViewModelCommand(
            DispatchSelected,
            () => SelectedOption is not null);
        ResetCommand = new ViewModelCommand(
            _reset,
            () => _canReset);
    }

    public IReadOnlyList<MenuPreviewInteractionOption> Options => _options;

    public bool HasOptions => Options.Count > 0;

    public string Heading => HasOptions
        ? $"Explicit interactions ({Options.Count:N0})"
        : "No authored interactions for selection";

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
    public ViewModelCommand ResetCommand { get; }

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

    public IReadOnlyList<string> TraceLines
    {
        get => _traceLines;
        private set => SetProperty(ref _traceLines, value);
    }

    public IReadOnlyList<string> DiagnosticLines
    {
        get => _diagnosticLines;
        private set => SetProperty(ref _diagnosticLines, value);
    }

    public IReadOnlyList<string> LocalChangeLines
    {
        get => _localChangeLines;
        private set => SetProperty(ref _localChangeLines, value);
    }

    public IReadOnlyList<string> QueuedScripts
    {
        get => _queuedScripts;
        private set => SetProperty(ref _queuedScripts, value);
    }

    public bool HasDiagnostics => DiagnosticLines.Count > 0;
    public bool HasLocalChanges => LocalChangeLines.Count > 0;
    public bool HasQueuedScripts => QueuedScripts.Count > 0;

    public string ResultDetails
    {
        get
        {
            var sections = new List<string>();
            if (TraceLines.Count > 0)
            {
                sections.Add(
                    "Ordered trace" + Environment.NewLine +
                    string.Join(Environment.NewLine, TraceLines));
            }
            if (LocalChangeLines.Count > 0)
            {
                sections.Add(
                    "Applied local changes" + Environment.NewLine +
                    string.Join(Environment.NewLine, LocalChangeLines));
            }
            if (DiagnosticLines.Count > 0)
            {
                sections.Add(
                    "Diagnostics" + Environment.NewLine +
                    string.Join(Environment.NewLine, DiagnosticLines));
            }
            if (QueuedScripts.Count > 0)
            {
                sections.Add(
                    "Queued scripts (inspection only; never executed)" +
                    Environment.NewLine +
                    string.Join(
                        Environment.NewLine + "---" + Environment.NewLine,
                        QueuedScripts));
            }
            return sections.Count == 0
                ? "The selected hook produced no trace entries."
                : string.Join(Environment.NewLine + Environment.NewLine, sections);
        }
    }

    internal void ReplaceOptions(
        MenuDebugProgram? program,
        MenuOutlineNodeKind? selectedKind,
        MenuNodeId? selectedNodeId)
    {
        MenuDebugInput? previous = SelectedOption?.Input;
        _options = Array.AsReadOnly(BuildOptions(
            program,
            selectedKind,
            selectedNodeId).ToArray());
        _selectedOption = _options.FirstOrDefault(value =>
                Equals(value.Input, previous)) ??
            _options.FirstOrDefault();
        ClearResult();
        OnPropertyChanged(nameof(Options));
        OnPropertyChanged(nameof(HasOptions));
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(SelectedOption));
        DispatchCommand.RaiseCanExecuteChanged();
    }

    internal void ShowResult(MenuDebugDispatchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        TraceLines = Array.AsReadOnly(result.Trace
            .OrderBy(value => value.Sequence)
            .Select(FormatTrace)
            .ToArray());
        DiagnosticLines = Array.AsReadOnly(result.Diagnostics
            .Select(value => $"{value.Kind} {value.Code}: {value.Message}")
            .ToArray());
        LocalChangeLines = Array.AsReadOnly(result.LocalVariableChanges
            .Select(value =>
                $"{value.Name}: {FormatValue(value.PreviousValue)} → " +
                $"{FormatValue(value.Value)} ({value.DeclaredKind})")
            .ToArray());
        QueuedScripts = Array.AsReadOnly(result.QueuedScripts
            .Select(value => value.Script)
            .ToArray());
        HasResult = true;
        ResultSummary =
            $"{result.Trace.Count:N0} trace · " +
            $"{result.LocalVariableChanges.Count:N0} local change(s) · " +
            $"{result.QueuedScripts.Count:N0} script(s) queued · " +
            $"{result.Diagnostics.Count:N0} diagnostic(s)";
        NotifyResultCollectionsChanged();
        SetCanReset(true);
    }

    internal void ClearInteractionState()
    {
        ClearResult();
        SetCanReset(false);
    }

    private void DispatchSelected()
    {
        if (SelectedOption is { } option)
            _dispatch(option.Input);
    }

    private void ClearResult()
    {
        TraceLines = [];
        DiagnosticLines = [];
        LocalChangeLines = [];
        QueuedScripts = [];
        HasResult = false;
        ResultSummary = "No interaction dispatched";
        NotifyResultCollectionsChanged();
    }

    private void NotifyResultCollectionsChanged()
    {
        OnPropertyChanged(nameof(HasDiagnostics));
        OnPropertyChanged(nameof(HasLocalChanges));
        OnPropertyChanged(nameof(HasQueuedScripts));
        OnPropertyChanged(nameof(ResultDetails));
    }

    private void SetCanReset(bool value)
    {
        if (_canReset == value)
            return;
        _canReset = value;
        ResetCommand.RaiseCanExecuteChanged();
    }

    private static IEnumerable<MenuPreviewInteractionOption> BuildOptions(
        MenuDebugProgram? program,
        MenuOutlineNodeKind? selectedKind,
        MenuNodeId? selectedNodeId)
    {
        if (program is null)
            yield break;
        if (selectedKind is MenuOutlineNodeKind.Menu or
            MenuOutlineNodeKind.Window)
        {
            foreach (MenuPreviewInteractionOption option in MenuOptions(program))
                yield return option;
            yield break;
        }
        if (selectedKind != MenuOutlineNodeKind.Item ||
            selectedNodeId is not { } itemId)
        {
            yield break;
        }

        MenuDebugItemProgram? item = program.Items.FirstOrDefault(value =>
            value.Id == itemId);
        if (item is null)
            yield break;
        foreach (MenuPreviewInteractionOption option in ItemOptions(item))
            yield return option;
    }

    private static IEnumerable<MenuPreviewInteractionOption> MenuOptions(
        MenuDebugProgram program)
    {
        foreach (MenuPreviewInteractionOption option in NonEmpty(
            "On open",
            new MenuDebugMenuHookInput(MenuDebugMenuHook.Open),
            program.Hooks.OnOpen))
        {
            yield return option;
        }
        foreach (MenuPreviewInteractionOption option in NonEmpty(
            "On close request",
            new MenuDebugMenuHookInput(MenuDebugMenuHook.CloseRequest),
            program.Hooks.OnCloseRequest))
        {
            yield return option;
        }
        foreach (MenuPreviewInteractionOption option in NonEmpty(
            "On close",
            new MenuDebugMenuHookInput(MenuDebugMenuHook.Close),
            program.Hooks.OnClose))
        {
            yield return option;
        }
        foreach (MenuPreviewInteractionOption option in NonEmpty(
            "On escape",
            new MenuDebugMenuHookInput(MenuDebugMenuHook.Escape),
            program.Hooks.OnEscape))
        {
            yield return option;
        }
        for (int index = 0; index < program.Hooks.KeyHandlers.Count; index++)
        {
            MenuDebugKeyHandler key = program.Hooks.KeyHandlers[index];
            yield return KeyOption(
                key,
                index,
                new MenuDebugMenuKeyInput(
                    new MenuDebugKeySelection(key.Key, index)));
        }
    }

    private static IEnumerable<MenuPreviewInteractionOption> ItemOptions(
        MenuDebugItemProgram item)
    {
        foreach (MenuPreviewInteractionOption option in NonEmpty(
            "Pointer enter",
            new MenuDebugItemHookInput(item.Id, MenuDebugItemHook.PointerEnter),
            item.Hooks.MouseEnter))
        {
            yield return option;
        }
        foreach (MenuPreviewInteractionOption option in NonEmpty(
            "Pointer exit",
            new MenuDebugItemHookInput(item.Id, MenuDebugItemHook.PointerExit),
            item.Hooks.MouseExit))
        {
            yield return option;
        }
        foreach (MenuPreviewInteractionOption option in NonEmpty(
            "Text pointer enter",
            new MenuDebugItemHookInput(item.Id, MenuDebugItemHook.TextPointerEnter),
            item.Hooks.MouseEnterText))
        {
            yield return option;
        }
        foreach (MenuPreviewInteractionOption option in NonEmpty(
            "Text pointer exit",
            new MenuDebugItemHookInput(item.Id, MenuDebugItemHook.TextPointerExit),
            item.Hooks.MouseExitText))
        {
            yield return option;
        }

        yield return new MenuPreviewInteractionOption(
            FocusLabel("Focus item", item.Hooks.OnFocus),
            new MenuDebugItemHookInput(item.Id, MenuDebugItemHook.Focus));
        yield return new MenuPreviewInteractionOption(
            FocusLabel("Leave focus", item.Hooks.LeaveFocus),
            new MenuDebugItemHookInput(item.Id, MenuDebugItemHook.LeaveFocus));

        foreach (MenuPreviewInteractionOption option in NonEmpty(
            "Action",
            new MenuDebugItemHookInput(item.Id, MenuDebugItemHook.Action),
            item.Hooks.Action))
        {
            yield return option;
        }
        foreach (MenuPreviewInteractionOption option in NonEmpty(
            "Accept",
            new MenuDebugItemHookInput(item.Id, MenuDebugItemHook.Accept),
            item.Hooks.Accept))
        {
            yield return option;
        }
        foreach (MenuPreviewInteractionOption option in NonEmpty(
            "Double click",
            new MenuDebugItemHookInput(item.Id, MenuDebugItemHook.DoubleClick),
            item.Hooks.DoubleClick))
        {
            yield return option;
        }
        for (int index = 0; index < item.Hooks.KeyHandlers.Count; index++)
        {
            MenuDebugKeyHandler key = item.Hooks.KeyHandlers[index];
            yield return KeyOption(
                key,
                index,
                new MenuDebugItemKeyInput(
                    item.Id,
                    new MenuDebugKeySelection(key.Key, index)));
        }
    }

    private static IEnumerable<MenuPreviewInteractionOption> NonEmpty(
        string label,
        MenuDebugInput input,
        MenuDebugEventSet eventSet)
    {
        if (eventSet.Handlers.Count > 0)
        {
            yield return new MenuPreviewInteractionOption(
                $"{label} ({eventSet.Handlers.Count:N0} handler(s))",
                input);
        }
    }

    private static MenuPreviewInteractionOption KeyOption(
        MenuDebugKeyHandler key,
        int index,
        MenuDebugInput input) => new(
            $"Key {key.Key} · authored #{index} " +
            $"({key.Actions.Handlers.Count:N0} handler(s))",
            input);

    private static string FocusLabel(
        string label,
        MenuDebugEventSet eventSet) =>
        eventSet.Handlers.Count == 0
            ? $"{label} (transition only)"
            : $"{label} + {eventSet.Handlers.Count:N0} handler(s)";

    private static string FormatTrace(MenuDebugDispatchTraceEntry value) =>
        value switch
        {
            MenuDebugDecisionTraceEntry decision =>
                $"#{value.Sequence:N0} {decision.BranchKind}: " +
                $"{(decision.IsSelected is null ? decision.Status : decision.IsSelected)} " +
                $"· {value.HandlerPath}",
            MenuDebugLocalVariableTraceEntry local =>
                $"#{value.Sequence:N0} Local {local.Name}: " +
                $"{FormatValue(local.PreviousValue)} → {FormatValue(local.Value)} " +
                $"({local.Status}, {(local.IsApplied ? "applied" : "not applied")}) " +
                $"· {value.HandlerPath}",
            MenuDebugQueuedScriptTraceEntry =>
                $"#{value.Sequence:N0} Script queued for inspection · {value.HandlerPath}",
            MenuDebugFocusTraceEntry focus =>
                $"#{value.Sequence:N0} Focus: " +
                $"{FormatNode(focus.PreviousItemId)} → {FormatNode(focus.ItemId)} " +
                $"· {value.HandlerPath}",
            MenuDebugDiagnosticTraceEntry diagnostic =>
                $"#{value.Sequence:N0} {diagnostic.Kind} {diagnostic.Code}: " +
                $"{diagnostic.Message} · {value.HandlerPath}",
            _ => $"#{value.Sequence:N0} {value.GetType().Name} · {value.HandlerPath}"
        };

    private static string FormatValue(MenuDebugValue? value) =>
        value is { } actual ? actual.AsString() : "<unset>";

    private static string FormatNode(MenuNodeId? value) =>
        value?.ToString() ?? "<none>";
}
