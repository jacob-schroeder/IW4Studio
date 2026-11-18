using IW4.Game.Assets.Menu;
using IW4.Studio.Desktop.Documents.MenuEditing.Preview;
using IW4.Studio.Desktop.Rendering;
using IW4.Studio.Documents.MenuEditing;
using IW4.Studio.Documents.MenuEditing.Behavior;
using IW4.Studio.Documents.MenuEditing.Behavior.Expressions;
using IW4.Studio.Documents.MenuEditing.Debugging;

namespace IW4.Studio.Desktop.ViewModels.Menu;

public sealed class MenuPreviewStateComparisonViewModel : ObservableObject
{
    private readonly IMenuTextResourceResolver? _textResolver;
    private MenuEditorSnapshot? _document;
    private MenuEvaluatedState? _liveState;
    private MenuPreviewScene? _liveScene;
    private int _liveMilliseconds;
    private int _baselineMilliseconds;
    private MenuPreviewScene? _baselineScene;
    private MenuPreviewScene? _comparedLiveScene;
    private bool _isExpanded;
    private IReadOnlyList<MenuPreviewStateComparisonRowViewModel> _rows = [];
    private Dictionary<MenuNodeId, MenuPreviewStateComparisonRowViewModel> _itemRows = [];
    private MenuPreviewStateComparisonRowViewModel? _selectedRow;
    private int _changedCount;

    internal MenuPreviewStateComparisonViewModel(IMenuTextResourceResolver? textResolver)
    {
        _textResolver = textResolver;
        PinBaselineCommand = new ViewModelCommand(PinBaseline, () => _liveState is not null);
        ClearBaselineCommand = new ViewModelCommand(ClearBaseline, () => HasBaseline);
    }

    public ViewModelCommand PinBaselineCommand { get; }

    public ViewModelCommand ClearBaselineCommand { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value) && value)
                RefreshRows();
        }
    }

    public MenuPreviewScene? BaselineScene => _baselineScene;

    public MenuPreviewScene? LiveScene => _comparedLiveScene;

    public bool HasBaseline => BaselineScene is not null;

    public string BaselineHeading => $"A · Pinned at {_baselineMilliseconds:N0} ms";

    public string DifferenceSummary => _changedCount switch
    {
        0 => "No state changes",
        1 => "1 changed row",
        _ => $"{_changedCount:N0} changed rows"
    };

    public IReadOnlyList<MenuPreviewStateComparisonRowViewModel> Rows => _rows;

    public MenuPreviewStateComparisonRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (SetProperty(ref _selectedRow, value))
                OnPropertyChanged(nameof(HasSelectedRow));
        }
    }

    public bool HasSelectedRow => SelectedRow is not null;

    internal void ReplaceDocument(MenuEditorSnapshot? snapshot)
    {
        _document = snapshot;
        Update(null, null, 0);
        ClearBaseline();
    }

    internal void Update(MenuEvaluatedState? state, MenuPreviewScene? scene, int milliseconds)
    {
        bool wasAvailable = _liveState is not null;
        _liveState = state;
        _liveScene = scene;
        _liveMilliseconds = milliseconds;
        if (wasAvailable != (state is not null))
            PinBaselineCommand.RaiseCanExecuteChanged();
        if (IsExpanded && HasBaseline)
            RefreshRows();
    }

    private void PinBaseline()
    {
        if (_document is not { } document || _liveState is not { } state ||
            _liveScene is not { } scene)
        {
            return;
        }

        var rows = new List<MenuPreviewStateComparisonRowViewModel>(document.Items.Count + 1);
        string menuRule = document.Behavior.HasVisibleExpression
            ? "Menu visibility expression and open state"
            : "Authored menu visibility and open state";
        rows.Add(new MenuPreviewStateComparisonRowViewModel(
            MenuPresentationText.MenuTitle(document.Name),
            menuRule,
            state.Window.IsVisible,
            null,
            null));

        var itemsById = state.Items.ToDictionary(item => item.Id);
        _itemRows = new Dictionary<MenuNodeId, MenuPreviewStateComparisonRowViewModel>(
            document.Items.Count);
        for (int index = 0; index < document.Items.Count; index++)
        {
            MenuItemSnapshot item = document.Items[index];
            if (!itemsById.TryGetValue(item.Id, out MenuEvaluatedItemState? evaluated))
                continue;

            var row = new MenuPreviewStateComparisonRowViewModel(
                MenuPresentationText.OutlineTitle(item, index, _textResolver),
                ItemRules(item),
                evaluated.IsVisible,
                evaluated.IsDisabled,
                state.Window.IsVisible,
                item.IsResolved);
            _itemRows.Add(item.Id, row);
            rows.Add(row);
        }

        _rows = rows.AsReadOnly();
        _baselineScene = scene;
        _comparedLiveScene = scene;
        _baselineMilliseconds = _liveMilliseconds;
        _changedCount = 0;
        NotifyBaselineChanged();
        SelectedRow = rows.Count > 1 ? rows[1] : rows[0];
    }

    private void ClearBaseline()
    {
        if (!HasBaseline)
            return;

        _baselineScene = null;
        _comparedLiveScene = null;
        _rows = [];
        _itemRows.Clear();
        _changedCount = 0;
        SelectedRow = null;
        NotifyBaselineChanged();
    }

    private void NotifyBaselineChanged()
    {
        OnPropertyChanged(nameof(BaselineScene));
        OnPropertyChanged(nameof(LiveScene));
        OnPropertyChanged(nameof(HasBaseline));
        OnPropertyChanged(nameof(BaselineHeading));
        OnPropertyChanged(nameof(DifferenceSummary));
        OnPropertyChanged(nameof(Rows));
        ClearBaselineCommand.RaiseCanExecuteChanged();
    }

    private void RefreshRows()
    {
        if (_liveState is not { } state || !HasBaseline)
            return;

        if (_liveScene is { } live && _comparedLiveScene is { } previous &&
            !ReferenceEquals(live, previous) &&
            (live.Settings != previous.Settings || !live.Primitives.SequenceEqual(previous.Primitives)))
        {
            SetProperty(ref _comparedLiveScene, live, nameof(LiveScene));
        }

        _rows[0].Update(state.Window.IsVisible, null, null);
        int changed = _rows[0].IsChanged ? 1 : 0;
        foreach (MenuEvaluatedItemState item in state.Items)
        {
            if (!_itemRows.TryGetValue(item.Id, out MenuPreviewStateComparisonRowViewModel? row))
                continue;

            row.Update(item.IsVisible, item.IsDisabled, state.Window.IsVisible);
            if (row.IsChanged)
                changed++;
        }
        SetProperty(ref _changedCount, changed, nameof(DifferenceSummary));
    }

    private static string ItemRules(MenuItemSnapshot item)
    {
        if (!item.IsResolved)
            return "Item definition unavailable";

        MenuItemBehaviorExpressionBindings bindings = item.Behavior.Expressions;
        string visible = Formula(bindings.Visible, "Authored visibility flag");
        string disabled = Formula(bindings.Disabled, "Not set");
        ItemDvarFlags gate = item.Value.DvarFlags &
            (ItemDvarFlags.Show | ItemDvarFlags.Hide | ItemDvarFlags.Enable | ItemDvarFlags.Disable);
        string rules = $"Visible · {visible}\nDisabled expression · {disabled}";
        if (gate != ItemDvarFlags.None &&
            !string.IsNullOrWhiteSpace(item.Value.DvarTest) &&
            !string.IsNullOrEmpty(item.Value.EnableDvar))
        {
            rules += $"\nDvar gate · {gate} when {item.Value.DvarTest} matches {item.Value.EnableDvar}";
        }
        return rules;
    }

    private static string Formula(MenuBehaviorExpressionBinding binding, string fallback) =>
        binding.Value is { } value
            ? BehaviorExpressionFormatter.Format(value).Value ?? "Unsupported expression"
            : fallback;
}

public sealed class MenuPreviewStateComparisonRowViewModel : ObservableObject
{
    private readonly MenuEvaluation<bool> _baselineVisible;
    private readonly MenuEvaluation<bool>? _baselineDisabled;
    private readonly MenuEvaluation<bool>? _baselineMenuVisible;
    private readonly bool _isResolved;
    private MenuEvaluation<bool> _liveVisible;
    private MenuEvaluation<bool>? _liveDisabled;
    private MenuEvaluation<bool>? _liveMenuVisible;
    private bool _isChanged;

    internal MenuPreviewStateComparisonRowViewModel(
        string title,
        string rules,
        MenuEvaluation<bool> visible,
        MenuEvaluation<bool>? disabled,
        MenuEvaluation<bool>? menuVisible,
        bool isResolved = true)
    {
        Title = title;
        Rules = rules;
        _baselineVisible = _liveVisible = visible;
        _baselineDisabled = _liveDisabled = disabled;
        _baselineMenuVisible = _liveMenuVisible = menuVisible;
        _isResolved = isResolved;
    }

    public string Title { get; }

    public string Rules { get; }

    public bool IsChanged => _isChanged;

    public string Baseline => Describe(_baselineVisible, _baselineDisabled, _baselineMenuVisible);

    public string Live => Describe(_liveVisible, _liveDisabled, _liveMenuVisible);

    public string BaselineDetails => Diagnostics(_baselineVisible, _baselineDisabled, _baselineMenuVisible);

    public string LiveDetails => Diagnostics(_liveVisible, _liveDisabled, _liveMenuVisible);

    internal void Update(
        MenuEvaluation<bool> visible,
        MenuEvaluation<bool>? disabled,
        MenuEvaluation<bool>? menuVisible)
    {
        if (Equivalent(_liveVisible, visible) &&
            Equivalent(_liveDisabled, disabled) &&
            Equivalent(_liveMenuVisible, menuVisible))
        {
            return;
        }

        _liveVisible = visible;
        _liveDisabled = disabled;
        _liveMenuVisible = menuVisible;
        SetProperty(
            ref _isChanged,
            !Equivalent(_baselineVisible, visible) ||
            !Equivalent(_baselineDisabled, disabled) ||
            !Equivalent(_baselineMenuVisible, menuVisible),
            nameof(IsChanged));
        OnPropertyChanged(nameof(Live));
        OnPropertyChanged(nameof(LiveDetails));
    }

    private string Describe(
        MenuEvaluation<bool> visible,
        MenuEvaluation<bool>? disabled,
        MenuEvaluation<bool>? menuVisible)
    {
        if (!_isResolved)
            return "Unavailable";

        string visibility;
        if (visible is { IsKnown: true, Value: false })
            visibility = "Hidden";
        else if (menuVisible is { IsKnown: true, Value: false })
            visibility = "Hidden by menu";
        else
        {
            MenuEvaluationStatus status = (visible.Status, menuVisible?.Status) switch
            {
                (MenuEvaluationStatus.Error, _) or (_, MenuEvaluationStatus.Error) => MenuEvaluationStatus.Error,
                (MenuEvaluationStatus.Unknown, _) or (_, MenuEvaluationStatus.Unknown) => MenuEvaluationStatus.Unknown,
                _ => MenuEvaluationStatus.Known
            };
            visibility = State(status, visible.Value && menuVisible?.Value != false, "Visible", "Hidden");
        }
        return disabled is null
            ? visibility
            : $"{visibility} · {State(disabled.Status, disabled.Value, "Disabled", "Enabled")}";
    }

    private static string State(MenuEvaluationStatus status, bool result, string whenTrue, string whenFalse)
    {
        string value = result ? whenTrue : whenFalse;
        return status == MenuEvaluationStatus.Known ? value : $"{status} ({value.ToLowerInvariant()} fallback)";
    }

    private static bool Equivalent(MenuEvaluation<bool>? first, MenuEvaluation<bool>? second) =>
        ReferenceEquals(first, second) ||
        first is not null && second is not null &&
        first.Status == second.Status && first.Value == second.Value &&
        first.Trace.Count == second.Trace.Count &&
        (first.Trace.Count == 0 || first.Trace.SequenceEqual(second.Trace));

    private static string Diagnostics(
        MenuEvaluation<bool> visible,
        MenuEvaluation<bool>? disabled,
        MenuEvaluation<bool>? menuVisible)
    {
        var messages = new List<string>();
        Append(visible, "Visibility");
        Append(disabled, "Enabled");
        Append(menuVisible, "Menu visibility");
        return string.Join(Environment.NewLine, messages);

        void Append(MenuEvaluation<bool>? result, string label)
        {
            if (result is null || result.IsKnown)
                return;

            foreach (string message in result.Trace.Select(entry => entry.Message).Distinct())
                messages.Add($"{label} · {message}");
        }
    }
}
