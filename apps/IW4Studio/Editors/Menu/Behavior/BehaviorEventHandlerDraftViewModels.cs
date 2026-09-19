using System.Collections.ObjectModel;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents.MenuEditing.Behavior;
using IW4.Studio.Documents.MenuEditing.Behavior.Expressions;

namespace IW4.Studio.Desktop.Editors.Menu.Behavior;

/// <summary>
/// Mutable, modal-local projection of one ordered event-handler set. It owns
/// every structural edit and produces a new immutable Documents value only
/// when the parent session is applied.
/// </summary>
public sealed class BehaviorEventHandlerSetDraftViewModel : ObservableObject
{
    private readonly MenuBehaviorEventHandlerSet? _source;
    private readonly BehaviorExpressionSupportDraftViewModel _expressionSupport;
    private readonly Action _changed;
    private bool _wasChanged;

    internal BehaviorEventHandlerSetDraftViewModel(
        MenuBehaviorEventHandlerSet? source,
        BehaviorExpressionSupportDraftViewModel expressionSupport,
        Action changed)
    {
        _source = source;
        _expressionSupport = expressionSupport ??
            throw new ArgumentNullException(nameof(expressionSupport));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        Handlers = [];

        if (source is not null)
            Hydrate(source.Handlers);

        AddScriptCommand = new ViewModelCommand(AddScript);
        AddConditionalCommand = new ViewModelCommand(AddConditional);
        AddSetLocalCommand = new ViewModelCommand(AddSetLocal);
        RefreshOrdering();
    }

    public ObservableCollection<BehaviorEventHandlerDraftViewModel> Handlers { get; }

    public bool HasHandlers => Handlers.Count != 0;

    internal bool HasChanges => _wasChanged;

    public string Summary => Handlers.Count == 0
        ? "No handlers"
        : $"{Handlers.Count} handler{Plural(Handlers.Count)}";

    public ViewModelCommand AddScriptCommand { get; }

    public ViewModelCommand AddConditionalCommand { get; }

    public ViewModelCommand AddSetLocalCommand { get; }

    internal MenuBehaviorEventHandlerSet? ToDomain()
    {
        if (_source is null && !_wasChanged)
            return null;

        return new MenuBehaviorEventHandlerSet(
            Handlers.SelectMany(handler => handler.ToEntries()),
            _source?.HandlerTablePointer ?? default);
    }

    internal IReadOnlyList<string> Validate(string path)
    {
        var messages = new List<string>();
        for (int index = 0; index < Handlers.Count; index++)
        {
            messages.AddRange(Handlers[index].Validate(
                $"{path}, handler {index + 1}"));
        }

        return messages;
    }

    internal void EnsurePresent()
    {
        if (_source is not null || _wasChanged)
            return;

        NotifyChanged();
    }

    private void Hydrate(
        IReadOnlyList<MenuBehaviorEventHandlerEntry> entries)
    {
        for (int index = 0; index < entries.Count; index++)
        {
            MenuBehaviorEventHandlerEntry entry = entries[index];
            if (entry.Handler is MenuBehaviorConditionalEventHandler conditional)
            {
                MenuBehaviorEventHandlerEntry? otherwise = null;
                if (index + 1 < entries.Count &&
                    entries[index + 1].Handler is MenuBehaviorElseEventHandler)
                {
                    otherwise = entries[++index];
                }

                AddDraft(new BehaviorConditionalEventHandlerDraftViewModel(
                    entry,
                    conditional,
                    otherwise,
                    _expressionSupport,
                    NotifyChanged));
                continue;
            }

            AddDraft(CreateDraft(entry));
        }
    }

    private BehaviorEventHandlerDraftViewModel CreateDraft(
        MenuBehaviorEventHandlerEntry entry) => entry.Handler switch
    {
        MenuBehaviorScriptEventHandler handler =>
            new BehaviorScriptEventHandlerDraftViewModel(
                entry, handler, NotifyChanged),
        MenuBehaviorElseEventHandler handler =>
            new BehaviorElseEventHandlerDraftViewModel(
                entry, handler, _expressionSupport, NotifyChanged),
        MenuBehaviorSetLocalVariableEventHandler handler =>
            new BehaviorSetLocalEventHandlerDraftViewModel(
                entry, handler, _expressionSupport, NotifyChanged),
        MenuBehaviorOpaqueEventHandler handler =>
            new BehaviorOpaqueEventHandlerDraftViewModel(entry, handler),
        null => new BehaviorMissingEventHandlerDraftViewModel(entry),
        _ => new BehaviorUnsupportedEventHandlerDraftViewModel(entry)
    };

    private void AddScript()
    {
        AddDraft(new BehaviorScriptEventHandlerDraftViewModel(NotifyChanged));
        NotifyChanged();
    }

    private void AddConditional()
    {
        AddDraft(new BehaviorConditionalEventHandlerDraftViewModel(
            _expressionSupport,
            NotifyChanged));
        NotifyChanged();
    }

    private void AddSetLocal()
    {
        AddDraft(new BehaviorSetLocalEventHandlerDraftViewModel(
            _expressionSupport,
            NotifyChanged));
        NotifyChanged();
    }

    private void AddDraft(BehaviorEventHandlerDraftViewModel draft)
    {
        draft.AttachOwner(this);
        Handlers.Add(draft);
    }

    internal void Remove(BehaviorEventHandlerDraftViewModel draft)
    {
        if (!Handlers.Remove(draft))
            return;

        RefreshOrdering();
        NotifyChanged();
    }

    internal void Move(BehaviorEventHandlerDraftViewModel draft, int offset)
    {
        int oldIndex = Handlers.IndexOf(draft);
        int newIndex = oldIndex + offset;
        if (oldIndex < 0 || newIndex < 0 || newIndex >= Handlers.Count)
            return;

        Handlers.Move(oldIndex, newIndex);
        RefreshOrdering();
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        _wasChanged = true;
        OnPropertyChanged(nameof(HasHandlers));
        OnPropertyChanged(nameof(Summary));
        RefreshOrdering();
        _changed();
    }

    private void RefreshOrdering()
    {
        foreach (BehaviorEventHandlerDraftViewModel handler in Handlers)
            handler.RefreshOrdering();
    }

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
}

public abstract class BehaviorEventHandlerDraftViewModel : ObservableObject
{
    private BehaviorEventHandlerSetDraftViewModel? _owner;

    protected BehaviorEventHandlerDraftViewModel(string title)
    {
        Title = title;
        MoveUpCommand = new ViewModelCommand(
            () => _owner?.Move(this, -1),
            () => CanMoveUp);
        MoveDownCommand = new ViewModelCommand(
            () => _owner?.Move(this, 1),
            () => CanMoveDown);
        DeleteCommand = new ViewModelCommand(() => _owner?.Remove(this));
    }

    public string Title { get; }

    public bool CanMoveUp { get; private set; }

    public bool CanMoveDown { get; private set; }

    public ViewModelCommand MoveUpCommand { get; }

    public ViewModelCommand MoveDownCommand { get; }

    public ViewModelCommand DeleteCommand { get; }

    internal void AttachOwner(BehaviorEventHandlerSetDraftViewModel owner) =>
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    internal void RefreshOrdering()
    {
        bool canMoveUp = _owner is not null && _owner.Handlers.IndexOf(this) > 0;
        bool canMoveDown = _owner is not null &&
            _owner.Handlers.IndexOf(this) < _owner.Handlers.Count - 1;
        if (SetProperty(ref _canMoveUp, canMoveUp, nameof(CanMoveUp)))
            MoveUpCommand.RaiseCanExecuteChanged();
        if (SetProperty(ref _canMoveDown, canMoveDown, nameof(CanMoveDown)))
            MoveDownCommand.RaiseCanExecuteChanged();
    }

    private bool _canMoveUp;
    private bool _canMoveDown;

    internal abstract IEnumerable<MenuBehaviorEventHandlerEntry> ToEntries();

    internal virtual IReadOnlyList<string> Validate(string path) => [];
}

public sealed class BehaviorScriptEventHandlerDraftViewModel
    : BehaviorEventHandlerDraftViewModel
{
    private readonly MenuBehaviorEventHandlerEntry? _sourceEntry;
    private readonly MenuBehaviorScriptEventHandler? _source;
    private readonly Action _changed;
    private string? _script;
    private bool _wasEdited;

    internal BehaviorScriptEventHandlerDraftViewModel(Action changed)
        : base("Script")
    {
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        _script = string.Empty;
    }

    internal BehaviorScriptEventHandlerDraftViewModel(
        MenuBehaviorEventHandlerEntry entry,
        MenuBehaviorScriptEventHandler source,
        Action changed)
        : base("Script")
    {
        _sourceEntry = entry;
        _source = source;
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        _script = source.Script;
    }

    /// <summary>Raw script text is intentionally never trimmed or normalized.</summary>
    public string? Script
    {
        get => _script;
        set
        {
            if (!SetProperty(ref _script, value))
                return;

            _wasEdited = true;
            OnPropertyChanged(nameof(Summary));
            _changed();
        }
    }

    public string Summary => string.IsNullOrEmpty(Script)
        ? "Empty script"
        : Script.Replace("\r", " ").Replace("\n", " ");

    internal override IEnumerable<MenuBehaviorEventHandlerEntry> ToEntries()
    {
        MenuBehaviorScriptEventHandler handler = _source is not null && !_wasEdited
            ? _source
            : _source is not null
                ? _source with { Script = Script }
                : MenuBehaviorScriptEventHandler.Create(Script);
        yield return _sourceEntry is { } entry
            ? entry with { Handler = handler }
            : MenuBehaviorEventHandlerEntry.Create(handler);
    }
}

public sealed class BehaviorConditionalEventHandlerDraftViewModel
    : BehaviorEventHandlerDraftViewModel
{
    private readonly MenuBehaviorEventHandlerEntry? _sourceEntry;
    private readonly MenuBehaviorConditionalEventHandler? _source;
    private readonly MenuBehaviorEventHandlerEntry? _otherwiseSourceEntry;
    private readonly MenuBehaviorElseEventHandler? _otherwiseSource;
    private readonly Action _changed;
    private bool _hasOtherwise;

    internal BehaviorConditionalEventHandlerDraftViewModel(
        BehaviorExpressionSupportDraftViewModel expressionSupport,
        Action changed)
        : base("If")
    {
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        Condition = new BehaviorExpressionDraftViewModel(
            MenuBehaviorExpressionBinding.Empty,
            expressionSupport,
            _changed);
        Then = new BehaviorEventHandlerSetDraftViewModel(
            null, expressionSupport, _changed);
        Otherwise = new BehaviorEventHandlerSetDraftViewModel(
            null, expressionSupport, _changed);
        AddOtherwiseCommand = new ViewModelCommand(AddOtherwise, () => !HasOtherwise);
        RemoveOtherwiseCommand = new ViewModelCommand(RemoveOtherwise, () => HasOtherwise);
    }

    internal BehaviorConditionalEventHandlerDraftViewModel(
        MenuBehaviorEventHandlerEntry entry,
        MenuBehaviorConditionalEventHandler source,
        MenuBehaviorEventHandlerEntry? otherwiseEntry,
        BehaviorExpressionSupportDraftViewModel expressionSupport,
        Action changed)
        : base("If")
    {
        _sourceEntry = entry;
        _source = source;
        _otherwiseSourceEntry = otherwiseEntry;
        _otherwiseSource = otherwiseEntry?.Handler as MenuBehaviorElseEventHandler;
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        Condition = new BehaviorExpressionDraftViewModel(
            source.Condition, expressionSupport, _changed);
        Then = new BehaviorEventHandlerSetDraftViewModel(
            source.Then, expressionSupport, _changed);
        Otherwise = new BehaviorEventHandlerSetDraftViewModel(
            _otherwiseSource?.Handlers, expressionSupport, _changed);
        _hasOtherwise = _otherwiseSource is not null;
        AddOtherwiseCommand = new ViewModelCommand(AddOtherwise, () => !HasOtherwise);
        RemoveOtherwiseCommand = new ViewModelCommand(RemoveOtherwise, () => HasOtherwise);
    }

    public BehaviorExpressionDraftViewModel Condition { get; }

    public BehaviorEventHandlerSetDraftViewModel Then { get; }

    public BehaviorEventHandlerSetDraftViewModel Otherwise { get; }

    public bool HasOtherwise
    {
        get => _hasOtherwise;
        private set
        {
            if (!SetProperty(ref _hasOtherwise, value))
                return;

            AddOtherwiseCommand.RaiseCanExecuteChanged();
            RemoveOtherwiseCommand.RaiseCanExecuteChanged();
        }
    }

    public ViewModelCommand AddOtherwiseCommand { get; }

    public ViewModelCommand RemoveOtherwiseCommand { get; }

    private void AddOtherwise()
    {
        HasOtherwise = true;
        Otherwise.EnsurePresent();
        _changed();
    }

    private void RemoveOtherwise()
    {
        HasOtherwise = false;
        _changed();
    }

    internal override IEnumerable<MenuBehaviorEventHandlerEntry> ToEntries()
    {
        MenuBehaviorConditionalEventHandler handler = _source is not null
            ? _source with
            {
                Condition = Condition.ToBinding(),
                Then = Then.ToDomain()
            }
            : new MenuBehaviorConditionalEventHandler(
                Condition.ToBinding(),
                Then.ToDomain() ?? MenuBehaviorEventHandlerSet.Empty,
                default,
                MenuBehaviorRawEventHandlerShape.Empty);
        yield return _sourceEntry is { } entry
            ? entry with { Handler = handler }
            : MenuBehaviorEventHandlerEntry.Create(handler);

        if (!HasOtherwise)
            yield break;

        MenuBehaviorElseEventHandler otherwise = _otherwiseSource is not null
            ? _otherwiseSource with { Handlers = Otherwise.ToDomain() }
            : new MenuBehaviorElseEventHandler(
                Otherwise.ToDomain() ?? MenuBehaviorEventHandlerSet.Empty,
                default,
                MenuBehaviorRawEventHandlerShape.Empty);
        yield return _otherwiseSourceEntry is { } otherwiseEntry
            ? otherwiseEntry with { Handler = otherwise }
            : MenuBehaviorEventHandlerEntry.Create(otherwise);
    }

    internal override IReadOnlyList<string> Validate(string path)
    {
        var messages = new List<string>();
        messages.AddRange(Condition.Validate(
            $"{path} condition", required: true,
            BehaviorExpressionResultKind.Boolean));
        messages.AddRange(Then.Validate($"{path}, then"));
        if (HasOtherwise)
            messages.AddRange(Otherwise.Validate($"{path}, otherwise"));
        return messages;
    }
}

public sealed class BehaviorElseEventHandlerDraftViewModel
    : BehaviorEventHandlerDraftViewModel
{
    private readonly MenuBehaviorEventHandlerEntry _sourceEntry;
    private readonly MenuBehaviorElseEventHandler _source;

    internal BehaviorElseEventHandlerDraftViewModel(
        MenuBehaviorEventHandlerEntry entry,
        MenuBehaviorElseEventHandler source,
        BehaviorExpressionSupportDraftViewModel expressionSupport,
        Action changed)
        : base("Otherwise")
    {
        _sourceEntry = entry;
        _source = source;
        Handlers = new BehaviorEventHandlerSetDraftViewModel(
            source.Handlers, expressionSupport, changed);
    }

    public BehaviorEventHandlerSetDraftViewModel Handlers { get; }

    internal override IEnumerable<MenuBehaviorEventHandlerEntry> ToEntries()
    {
        if (!Handlers.HasChanges)
        {
            yield return _sourceEntry;
            yield break;
        }

        yield return _sourceEntry with
        {
            Handler = _source with { Handlers = Handlers.ToDomain() }
        };
    }

    internal override IReadOnlyList<string> Validate(string path) =>
        Handlers.Validate(path);
}

public sealed class BehaviorSetLocalEventHandlerDraftViewModel
    : BehaviorEventHandlerDraftViewModel
{
    private readonly MenuBehaviorEventHandlerEntry? _sourceEntry;
    private readonly MenuBehaviorSetLocalVariableEventHandler? _source;
    private readonly BehaviorExpressionSupportDraftViewModel _expressionSupport;
    private readonly Action _changed;
    private MenuBehaviorLocalValueType _valueType;
    private string? _name;

    internal BehaviorSetLocalEventHandlerDraftViewModel(
        BehaviorExpressionSupportDraftViewModel expressionSupport,
        Action changed)
        : base("Set local")
    {
        _expressionSupport = expressionSupport ??
            throw new ArgumentNullException(nameof(expressionSupport));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        ValueTypes = Enum.GetValues<MenuBehaviorLocalValueType>();
        _name = string.Empty;
        Expression = new BehaviorExpressionDraftViewModel(
            MenuBehaviorExpressionBinding.Empty,
            _expressionSupport,
            _changed);
    }

    internal BehaviorSetLocalEventHandlerDraftViewModel(
        MenuBehaviorEventHandlerEntry entry,
        MenuBehaviorSetLocalVariableEventHandler source,
        BehaviorExpressionSupportDraftViewModel expressionSupport,
        Action changed)
        : base("Set local")
    {
        _sourceEntry = entry;
        _source = source;
        _expressionSupport = expressionSupport ??
            throw new ArgumentNullException(nameof(expressionSupport));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        ValueTypes = Enum.GetValues<MenuBehaviorLocalValueType>();
        _valueType = source.ValueType;
        _name = source.Name;
        Expression = new BehaviorExpressionDraftViewModel(
            source.Expression,
            _expressionSupport,
            _changed);
    }

    public IReadOnlyList<MenuBehaviorLocalValueType> ValueTypes { get; }

    public MenuBehaviorLocalValueType ValueType
    {
        get => _valueType;
        set
        {
            if (!SetProperty(ref _valueType, value))
                return;

            OnPropertyChanged(nameof(ExpectedResultKindText));
            _changed();
        }
    }

    public string? Name
    {
        get => _name;
        set
        {
            if (!SetProperty(ref _name, value))
                return;

            _changed();
        }
    }

    public BehaviorExpressionDraftViewModel Expression { get; }

    public string ExpectedResultKindText => ExpectedResultKind().ToString();

    internal override IEnumerable<MenuBehaviorEventHandlerEntry> ToEntries()
    {
        MenuBehaviorSetLocalVariableEventHandler handler = _source is not null
            ? _source with
            {
                ValueType = ValueType,
                Name = Name,
                Expression = Expression.ToBinding()
            }
            : new MenuBehaviorSetLocalVariableEventHandler(
                ValueType,
                Name,
                Expression.ToBinding(),
                default,
                default,
                MenuBehaviorRawEventHandlerShape.Empty);
        yield return _sourceEntry is { } entry
            ? entry with { Handler = handler }
            : MenuBehaviorEventHandlerEntry.Create(handler);
    }

    internal override IReadOnlyList<string> Validate(string path)
    {
        var messages = new List<string>();
        if (string.IsNullOrWhiteSpace(Name))
            messages.Add($"{path}: enter a local variable name.");
        messages.AddRange(Expression.Validate(
            $"{path} value", required: true, ExpectedResultKind()));
        return messages;
    }

    private BehaviorExpressionResultKind ExpectedResultKind() => ValueType switch
    {
        MenuBehaviorLocalValueType.Boolean => BehaviorExpressionResultKind.Boolean,
        MenuBehaviorLocalValueType.Integer => BehaviorExpressionResultKind.Integer,
        MenuBehaviorLocalValueType.Float => BehaviorExpressionResultKind.Float,
        MenuBehaviorLocalValueType.String => BehaviorExpressionResultKind.String,
        _ => BehaviorExpressionResultKind.Unknown
    };
}

public sealed class BehaviorOpaqueEventHandlerDraftViewModel
    : BehaviorEventHandlerDraftViewModel
{
    private readonly MenuBehaviorEventHandlerEntry _entry;
    private readonly MenuBehaviorOpaqueEventHandler _handler;

    internal BehaviorOpaqueEventHandlerDraftViewModel(
        MenuBehaviorEventHandlerEntry entry,
        MenuBehaviorOpaqueEventHandler handler)
        : base("Unsupported handler")
    {
        _entry = entry;
        _handler = handler;
    }

    public string Description =>
        $"Imported handler type 0x{_handler.EventType:X2} is preserved read-only.";

    internal override IEnumerable<MenuBehaviorEventHandlerEntry> ToEntries()
    {
        yield return _entry;
    }
}

public sealed class BehaviorMissingEventHandlerDraftViewModel
    : BehaviorEventHandlerDraftViewModel
{
    private readonly MenuBehaviorEventHandlerEntry _entry;

    internal BehaviorMissingEventHandlerDraftViewModel(
        MenuBehaviorEventHandlerEntry entry)
        : base("Missing handler") => _entry = entry;

    public string Description => "The imported handler pointer is null and is preserved.";

    internal override IEnumerable<MenuBehaviorEventHandlerEntry> ToEntries()
    {
        yield return _entry;
    }
}

public sealed class BehaviorUnsupportedEventHandlerDraftViewModel
    : BehaviorEventHandlerDraftViewModel
{
    private readonly MenuBehaviorEventHandlerEntry _entry;

    internal BehaviorUnsupportedEventHandlerDraftViewModel(
        MenuBehaviorEventHandlerEntry entry)
        : base("Unsupported handler") => _entry = entry;

    public string Description => "This imported handler shape is preserved read-only.";

    internal override IEnumerable<MenuBehaviorEventHandlerEntry> ToEntries()
    {
        yield return _entry;
    }
}
