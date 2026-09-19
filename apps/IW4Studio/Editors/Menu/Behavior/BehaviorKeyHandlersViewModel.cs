using System.Collections.ObjectModel;
using System.Globalization;
using IW4.Studio.Desktop.ViewModels;
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
        AddKeyHandlerCommand = new ViewModelCommand(AddKeyHandler);
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

    public ViewModelCommand AddKeyHandlerCommand { get; }

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

    private void AddKeyHandler()
    {
        AddDraft(new BehaviorKeyHandlerDraftViewModel(
            _expressionSupport, OnHandlerChanged));
        RefreshOrdering();
        RefreshDuplicateKeys();
        NotifyChanged();
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
    private readonly MenuBehaviorKeyHandlerBinding? _source;
    private readonly BehaviorExpressionSupportDraftViewModel _expressionSupport;
    private readonly Action _changed;
    private BehaviorKeyHandlersViewModel? _owner;
    private string _keyText;
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
        Action = new BehaviorEventHandlerSetDraftViewModel(
            source.Action, _expressionSupport, _changed);
        MoveUpCommand = new ViewModelCommand(
            () => _owner?.Move(this, -1), () => CanMoveUp);
        MoveDownCommand = new ViewModelCommand(
            () => _owner?.Move(this, 1), () => CanMoveDown);
        RemoveCommand = new ViewModelCommand(() => _owner?.Remove(this));
    }

    public string KeyText
    {
        get => _keyText;
        set
        {
            value ??= string.Empty;
            if (!SetProperty(ref _keyText, value))
                return;

            OnPropertyChanged(nameof(HasKeyError));
            OnPropertyChanged(nameof(KeyErrorText));
            _changed();
        }
    }

    public BehaviorEventHandlerSetDraftViewModel Action { get; }

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

    public string KeyErrorText => HasKeyError
        ? IsDuplicateKey
            ? "Each key code can have only one handler."
            : "Enter a signed integer engine key code."
        : string.Empty;

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
        if (!TryGetKey(out _))
            messages.Add($"{path}: enter a signed integer engine key code.");
        else if (IsDuplicateKey)
            messages.Add($"{path}: each key code can have only one handler.");
        messages.AddRange(Action.Validate($"{path} actions"));
        return messages;
    }

    internal void AttachOwner(BehaviorKeyHandlersViewModel owner) =>
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    internal void RefreshOrdering()
    {
        int index = _owner?.Handlers.IndexOf(this) ?? -1;
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
