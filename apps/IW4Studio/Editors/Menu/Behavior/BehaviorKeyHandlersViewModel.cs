using System.Collections.ObjectModel;
using System.Globalization;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Desktop.ViewModels.Menu;
using IW4.Studio.Documents.MenuEditing.Behavior;
using IW4.Studio.Documents.MenuEditing.Behavior.Expressions;

namespace IW4.Studio.Desktop.Editors.Menu.Behavior;

/// <summary>Editable ordered projection of the native ItemKeyHandler chain.</summary>
public sealed class BehaviorKeyHandlersViewModel : ObservableObject
{
    private readonly MenuBehaviorKeyHandlerBindings _source;
    private readonly BehaviorExpressionSupportDraftViewModel _expressionSupport;
    private readonly Action _changed;
    private bool _retainTruncatedImportedTail;

    internal BehaviorKeyHandlersViewModel(
        MenuBehaviorKeyHandlerBindings source,
        BehaviorExpressionSupportDraftViewModel expressionSupport,
        Action changed)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _expressionSupport = expressionSupport ??
            throw new ArgumentNullException(nameof(expressionSupport));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        _retainTruncatedImportedTail = source.HasTruncatedImportedTail;
        Handlers = [];
        foreach (MenuBehaviorKeyHandlerBinding binding in source.Handlers)
            AddDraft(new BehaviorKeyHandlerDraftViewModel(
                binding, _expressionSupport, OnHandlerChanged));
        DiscardImportedTailCommand = new ViewModelCommand(
            DiscardImportedTail,
            () => HasTruncatedImportedTail);
        RefreshOrdering();
        RefreshDuplicateKeys();
    }

    public ObservableCollection<BehaviorKeyHandlerDraftViewModel> Handlers { get; }

    public bool HasHandlers => Handlers.Count != 0;

    public bool HasTruncatedImportedTail => _retainTruncatedImportedTail;

    public string ImportedTailText => HasTruncatedImportedTail
        ? "The imported key chain has an unresolved or cyclic tail. " +
          "Remove that tail before applying other behavior changes."
        : string.Empty;

    public string Summary => Handlers.Count == 0
        ? "No key handlers"
        : $"{Handlers.Count} key handler{Plural(Handlers.Count)}";

    public ViewModelCommand DiscardImportedTailCommand { get; }

    internal MenuBehaviorKeyHandlerBindings ToDomain() => new(
        Handlers.Select(handler => handler.ToDomain()),
        _source.RootPointer,
        _retainTruncatedImportedTail);

    internal IReadOnlyList<string> Validate()
    {
        var messages = new List<string>();
        if (HasTruncatedImportedTail)
        {
            messages.Add(
                "Key handlers: remove the unresolved imported tail before applying.");
        }
        for (int index = 0; index < Handlers.Count; index++)
            messages.AddRange(Handlers[index].Validate($"Key handler {index + 1}"));
        return messages;
    }

    internal BehaviorKeyHandlerDraftViewModel AddKeyHandler()
    {
        var handler = new BehaviorKeyHandlerDraftViewModel(_expressionSupport, OnHandlerChanged);
        AddDraft(handler);
        RefreshOrdering();
        RefreshDuplicateKeys();
        NotifyChanged();
        return handler;
    }

    private void DiscardImportedTail()
    {
        if (!HasTruncatedImportedTail)
            return;

        _retainTruncatedImportedTail = false;
        OnPropertyChanged(nameof(HasTruncatedImportedTail));
        OnPropertyChanged(nameof(ImportedTailText));
        DiscardImportedTailCommand.RaiseCanExecuteChanged();
        NotifyChanged();
    }

    private void AddDraft(BehaviorKeyHandlerDraftViewModel handler)
    {
        handler.AttachOwner(this);
        Handlers.Add(handler);
    }

    internal void Remove(BehaviorKeyHandlerDraftViewModel handler)
    {
        if (!Handlers.Remove(handler))
            return;

        RefreshOrdering();
        RefreshDuplicateKeys();
        NotifyChanged();
    }

    internal void Move(BehaviorKeyHandlerDraftViewModel handler, int offset)
    {
        int oldIndex = Handlers.IndexOf(handler);
        int newIndex = oldIndex + offset;
        if (oldIndex < 0 || newIndex < 0 || newIndex >= Handlers.Count)
            return;

        Handlers.Move(oldIndex, newIndex);
        RefreshOrdering();
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        OnPropertyChanged(nameof(HasHandlers));
        OnPropertyChanged(nameof(Summary));
        _changed();
    }

    private void OnHandlerChanged()
    {
        RefreshDuplicateKeys();
        NotifyChanged();
    }

    private void RefreshDuplicateKeys()
    {
        HashSet<int> duplicateKeys = Handlers
            .Where(handler => handler.TryGetKey(out _))
            .GroupBy(handler => handler.KeyValue)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();
        foreach (BehaviorKeyHandlerDraftViewModel handler in Handlers)
            handler.IsDuplicateKey = handler.TryGetKey(out int key) &&
                duplicateKeys.Contains(key);
    }

    private void RefreshOrdering()
    {
        foreach (BehaviorKeyHandlerDraftViewModel handler in Handlers)
            handler.RefreshOrdering();
    }

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
}

public sealed class BehaviorKeyHandlerDraftViewModel : ObservableObject
{
    private static readonly MenuKeyGroup[] Groups = Enum.GetValues<MenuKeyGroup>();
    private readonly MenuBehaviorKeyHandlerBinding? _source;
    private readonly BehaviorExpressionSupportDraftViewModel _expressionSupport;
    private readonly Action _changed;
    private BehaviorKeyHandlersViewModel? _owner;
    private MenuKeyGroup _keyGroup = MenuKeyGroup.Controller;
    private string _keyText;
    private int _position;
    private bool _canMoveUp;
    private bool _canMoveDown;

    internal BehaviorKeyHandlerDraftViewModel(
        BehaviorExpressionSupportDraftViewModel expressionSupport,
        Action changed)
    {
        _expressionSupport = expressionSupport ??
            throw new ArgumentNullException(nameof(expressionSupport));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        _keyText = string.Empty;
        IsExpanded = true;
        Action = new BehaviorEventHandlerSetDraftViewModel(
            null, _expressionSupport, _changed);
        Action.EnsurePresent();
        MoveUpCommand = new ViewModelCommand(
            () => _owner?.Move(this, -1), () => CanMoveUp);
        MoveDownCommand = new ViewModelCommand(
            () => _owner?.Move(this, 1), () => CanMoveDown);
        RemoveCommand = new ViewModelCommand(() => _owner?.Remove(this));
    }

    internal BehaviorKeyHandlerDraftViewModel(
        MenuBehaviorKeyHandlerBinding source,
        BehaviorExpressionSupportDraftViewModel expressionSupport,
        Action changed)
    {
        _source = source;
        _expressionSupport = expressionSupport ??
            throw new ArgumentNullException(nameof(expressionSupport));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        _keyText = source.Key.ToString(CultureInfo.InvariantCulture);
        _keyGroup = MenuKeyChoice.Find(source.Key)?.Group ?? MenuKeyGroup.Custom;
        Action = new BehaviorEventHandlerSetDraftViewModel(
            source.Action, _expressionSupport, _changed);
        MoveUpCommand = new ViewModelCommand(
            () => _owner?.Move(this, -1), () => CanMoveUp);
        MoveDownCommand = new ViewModelCommand(
            () => _owner?.Move(this, 1), () => CanMoveDown);
        RemoveCommand = new ViewModelCommand(() => _owner?.Remove(this));
    }

    public IReadOnlyList<MenuKeyGroup> KeyGroups => Groups;

    public MenuKeyGroup KeyGroup
    {
        get => _keyGroup;
        set
        {
            if (!SetProperty(ref _keyGroup, value))
                return;

            OnPropertyChanged(nameof(KeyOptions));
            OnPropertyChanged(nameof(SelectedKey));
            OnPropertyChanged(nameof(IsCustomKey));
            OnPropertyChanged(nameof(KeyGroupHint));
            OnPropertyChanged(nameof(KeyErrorText));
        }
    }

    public IReadOnlyList<MenuKeyChoice> KeyOptions => MenuKeyChoice.Options
        .Where(choice => choice.Group == KeyGroup)
        .ToArray();

    public MenuKeyChoice? SelectedKey
    {
        get => !IsCustomKey && TryGetKey(out int key) && MenuKeyChoice.Find(key) is { } choice &&
            choice.Group == KeyGroup ? choice : null;
        set
        {
            if (value is not null && value.Group == KeyGroup)
                KeyText = value.Code.ToString(CultureInfo.InvariantCulture);
        }
    }

    public bool IsCustomKey => KeyGroup == MenuKeyGroup.Custom;

    public string? KeyGroupHint => KeyGroup switch
    {
        MenuKeyGroup.Controller => "Confirm and Cancel follow the PS3 system’s Cross/Circle assignment.",
        MenuKeyGroup.Character => "Character keys are case-sensitive.",
        _ => null
    };

    public string KeyText
    {
        get => _keyText;
        set
        {
            value ??= string.Empty;
            if (!SetProperty(ref _keyText, value))
                return;

            OnPropertyChanged(nameof(SelectedKey));
            OnPropertyChanged(nameof(HasKeyError));
            OnPropertyChanged(nameof(KeyErrorText));
            OnPropertyChanged(nameof(Heading));
            _changed();
        }
    }

    public BehaviorEventHandlerSetDraftViewModel Action { get; }

    public bool IsExpanded { get; set; }

    public string Heading => TryGetKey(out int key)
        ? $"{_position}. {MenuKeyChoice.Describe(key)}"
        : $"{_position}. Choose a key";

    internal int KeyValue
    {
        get
        {
            _ = TryGetKey(out int key);
            return key;
        }
    }

    internal bool IsDuplicateKey
    {
        get => _isDuplicateKey;
        set
        {
            if (!SetProperty(ref _isDuplicateKey, value))
                return;

            OnPropertyChanged(nameof(HasKeyError));
            OnPropertyChanged(nameof(KeyErrorText));
        }
    }

    private bool _isDuplicateKey;

    public bool HasKeyError => !TryGetKey(out _) || IsDuplicateKey;

    public string KeyErrorText
    {
        get
        {
            if (IsDuplicateKey)
                return "Each key code can have only one handler.";
            if (TryGetKey(out _))
                return string.Empty;

            return IsCustomKey
                ? "Enter a signed integer engine key code."
                : "Choose a key or enter a custom code.";
        }
    }

    public bool CanMoveUp
    {
        get => _canMoveUp;
        private set
        {
            if (SetProperty(ref _canMoveUp, value))
                MoveUpCommand.RaiseCanExecuteChanged();
        }
    }

    public bool CanMoveDown
    {
        get => _canMoveDown;
        private set
        {
            if (SetProperty(ref _canMoveDown, value))
                MoveDownCommand.RaiseCanExecuteChanged();
        }
    }

    public ViewModelCommand MoveUpCommand { get; }

    public ViewModelCommand MoveDownCommand { get; }

    public ViewModelCommand RemoveCommand { get; }

    internal MenuBehaviorKeyHandlerBinding ToDomain()
    {
        _ = TryGetKey(out int key);
        return _source is { } source
            ? source with
            {
                Key = key,
                Action = Action.ToDomain() ?? MenuBehaviorEventHandlerSet.Empty
            }
            : new MenuBehaviorKeyHandlerBinding(
                key, Action.ToDomain() ?? MenuBehaviorEventHandlerSet.Empty,
                default, default);
    }

    internal IReadOnlyList<string> Validate(string path)
    {
        var messages = new List<string>();
        if (HasKeyError)
            messages.Add($"{path}: {KeyErrorText}");
        messages.AddRange(Action.Validate($"{path} actions"));
        return messages;
    }

    internal void AttachOwner(BehaviorKeyHandlersViewModel owner) =>
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    internal void RefreshOrdering()
    {
        int index = _owner?.Handlers.IndexOf(this) ?? -1;
        SetProperty(ref _position, index + 1, nameof(Heading));
        CanMoveUp = index > 0;
        CanMoveDown = _owner is not null && index >= 0 &&
            index < _owner.Handlers.Count - 1;
    }

    internal bool TryGetKey(out int key) => int.TryParse(
        KeyText,
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out key);
}
