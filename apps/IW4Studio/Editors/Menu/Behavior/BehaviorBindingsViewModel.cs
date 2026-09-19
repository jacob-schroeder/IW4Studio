using System.Collections.ObjectModel;
using IW4.Assets.Assets.Menu;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents.MenuEditing.Behavior;
using IW4.Studio.Documents.MenuEditing.Behavior.Expressions;

namespace IW4.Studio.Desktop.Editors.Menu.Behavior;

/// <summary>Editable fixed statements and ordered ItemFloatExpression rows.</summary>
public sealed class BehaviorBindingsViewModel : ObservableObject
{
    private readonly MenuItemBehaviorExpressionBindings _source;
    private readonly BehaviorExpressionSupportDraftViewModel _expressionSupport;
    private readonly Action _changed;

    internal BehaviorBindingsViewModel(
        MenuItemBehaviorExpressionBindings source,
        BehaviorExpressionSupportDraftViewModel expressionSupport,
        Action changed)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _expressionSupport = expressionSupport ??
            throw new ArgumentNullException(nameof(expressionSupport));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        FixedBindings = Array.AsReadOnly(
        [
            new BehaviorFixedBindingViewModel("Visible", "Boolean", source.Visible, BehaviorExpressionResultKind.Boolean, _expressionSupport, NotifyChanged),
            new BehaviorFixedBindingViewModel("Disabled", "Boolean", source.Disabled, BehaviorExpressionResultKind.Boolean, _expressionSupport, NotifyChanged),
            new BehaviorFixedBindingViewModel("Text", "String", source.Text, BehaviorExpressionResultKind.String, _expressionSupport, NotifyChanged),
            new BehaviorFixedBindingViewModel("Material", "String", source.Material, BehaviorExpressionResultKind.String, _expressionSupport, NotifyChanged)
        ]);
        FloatBindings = [];
        foreach (MenuBehaviorFloatExpressionBinding binding in source.FloatExpressions.Entries)
            AddDraft(new BehaviorFloatBindingDraftViewModel(
                binding, _expressionSupport, OnFloatBindingChanged));
        AddFloatBindingCommand = new ViewModelCommand(AddFloatBinding);
        RefreshOrdering();
        RefreshDuplicateTargets();
    }

    public IReadOnlyList<BehaviorFixedBindingViewModel> FixedBindings { get; }

    public BehaviorExpressionSupportDraftViewModel ExpressionSupport =>
        _expressionSupport;

    public ObservableCollection<BehaviorFloatBindingDraftViewModel> FloatBindings { get; }

    public bool HasFloatBindings => FloatBindings.Count != 0;

    public string Summary
    {
        get
        {
            int fixedCount = FixedBindings.Count(binding => binding.Expression.HasExpression);
            int total = fixedCount + FloatBindings.Count;
            return total == 0
                ? "No bindings"
                : $"{total} binding{Plural(total)}";
        }
    }

    public ViewModelCommand AddFloatBindingCommand { get; }

    internal MenuItemBehaviorExpressionBindings ToDomain() => _source with
    {
        Visible = FixedBindings[0].ToBinding(),
        Disabled = FixedBindings[1].ToBinding(),
        Text = FixedBindings[2].ToBinding(),
        Material = FixedBindings[3].ToBinding(),
        FloatExpressions = new MenuBehaviorFloatExpressionBindings(
            FloatBindings.Select(binding => binding.ToDomain()),
            _source.FloatExpressions.SourcePointer)
    };

    internal IReadOnlyList<string> Validate()
    {
        var messages = new List<string>();
        foreach (BehaviorFixedBindingViewModel binding in FixedBindings)
            messages.AddRange(binding.Validate());
        for (int index = 0; index < FloatBindings.Count; index++)
            messages.AddRange(FloatBindings[index].Validate(index + 1));
        return messages;
    }

    private void AddFloatBinding()
    {
        AddDraft(new BehaviorFloatBindingDraftViewModel(
            _expressionSupport, OnFloatBindingChanged));
        RefreshOrdering();
        RefreshDuplicateTargets();
        NotifyChanged();
    }

    private void AddDraft(BehaviorFloatBindingDraftViewModel binding)
    {
        binding.AttachOwner(this);
        FloatBindings.Add(binding);
    }

    internal void Remove(BehaviorFloatBindingDraftViewModel binding)
    {
        if (!FloatBindings.Remove(binding))
            return;

        RefreshOrdering();
        RefreshDuplicateTargets();
        NotifyChanged();
    }

    internal void Move(BehaviorFloatBindingDraftViewModel binding, int offset)
    {
        int oldIndex = FloatBindings.IndexOf(binding);
        int newIndex = oldIndex + offset;
        if (oldIndex < 0 || newIndex < 0 || newIndex >= FloatBindings.Count)
            return;

        FloatBindings.Move(oldIndex, newIndex);
        RefreshOrdering();
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        OnPropertyChanged(nameof(HasFloatBindings));
        OnPropertyChanged(nameof(Summary));
        _changed();
    }

    private void OnFloatBindingChanged()
    {
        RefreshDuplicateTargets();
        NotifyChanged();
    }

    private void RefreshDuplicateTargets()
    {
        HashSet<ItemFloatExpressionTarget> duplicateTargets = FloatBindings
            .GroupBy(binding => binding.Target)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();
        foreach (BehaviorFloatBindingDraftViewModel binding in FloatBindings)
            binding.IsDuplicateTarget = duplicateTargets.Contains(binding.Target);
    }

    private void RefreshOrdering()
    {
        foreach (BehaviorFloatBindingDraftViewModel binding in FloatBindings)
            binding.RefreshOrdering();
    }

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
}

public sealed class BehaviorFixedBindingViewModel : ObservableObject
{
    private readonly Action _changed;

    internal BehaviorFixedBindingViewModel(
        string title,
        string expectedType,
        MenuBehaviorExpressionBinding source,
        BehaviorExpressionResultKind expectedResultKind,
        BehaviorExpressionSupportDraftViewModel expressionSupport,
        Action changed)
    {
        Title = title;
        ExpectedType = expectedType;
        ExpectedResultKind = expectedResultKind;
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        Expression = new BehaviorExpressionDraftViewModel(
            source, expressionSupport, NotifyChanged);
        BeginReplacementCommand = new ViewModelCommand(Expression.BeginReplacement);
        ClearExpressionCommand = new ViewModelCommand(
            Expression.Clear, () => Expression.HasExpression);
    }

    public string Title { get; }

    public string ExpectedType { get; }

    public BehaviorExpressionResultKind ExpectedResultKind { get; }

    public BehaviorExpressionDraftViewModel Expression { get; }

    public ViewModelCommand BeginReplacementCommand { get; }

    public ViewModelCommand ClearExpressionCommand { get; }

    internal MenuBehaviorExpressionBinding ToBinding() => Expression.ToBinding();

    internal IReadOnlyList<string> Validate() => Expression.Validate(
        Title, required: false, ExpectedResultKind);

    private void NotifyChanged()
    {
        ClearExpressionCommand.RaiseCanExecuteChanged();
        _changed();
    }
}

public sealed class BehaviorFloatBindingDraftViewModel : ObservableObject
{
    private static readonly IReadOnlyList<BehaviorFloatTargetOption>
        AvailableTargets = Array.AsReadOnly(
            MenuBehaviorFloatExpressionBindings.AllTargets
                .Select(target => new BehaviorFloatTargetOption(
                    target, FormatTarget(target)))
                .ToArray());
    private readonly MenuBehaviorFloatExpressionBinding? _source;
    private readonly Action _changed;
    private BehaviorBindingsViewModel? _owner;
    private readonly BehaviorFloatTargetOption _unknownTargetOption;
    private readonly bool _isImportedUnknownTarget;
    private ItemFloatExpressionTarget _target;
    private bool _canMoveUp;
    private bool _canMoveDown;

    internal BehaviorFloatBindingDraftViewModel(
        BehaviorExpressionSupportDraftViewModel expressionSupport,
        Action changed)
    {
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        Targets = AvailableTargets;
        _target = Targets[0].Target;
        _unknownTargetOption = new BehaviorFloatTargetOption(
            _target,
            FormatTarget(_target));
        Expression = new BehaviorExpressionDraftViewModel(
            MenuBehaviorExpressionBinding.Empty, expressionSupport, _changed);
        MoveUpCommand = new ViewModelCommand(
            () => _owner?.Move(this, -1), () => CanMoveUp);
        MoveDownCommand = new ViewModelCommand(
            () => _owner?.Move(this, 1), () => CanMoveDown);
        RemoveCommand = new ViewModelCommand(() => _owner?.Remove(this));
    }

    internal BehaviorFloatBindingDraftViewModel(
        MenuBehaviorFloatExpressionBinding source,
        BehaviorExpressionSupportDraftViewModel expressionSupport,
        Action changed)
    {
        _source = source;
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        _target = source.Target;
        _isImportedUnknownTarget = !IsSupportedTarget(source.Target);
        _unknownTargetOption = new BehaviorFloatTargetOption(
            source.Target,
            $"Unknown imported target (0x{(int)source.Target:X})");
        Targets = _isImportedUnknownTarget
            ? Array.AsReadOnly([_unknownTargetOption])
            : AvailableTargets;
        Expression = new BehaviorExpressionDraftViewModel(
            source.Expression, expressionSupport, _changed);
        MoveUpCommand = new ViewModelCommand(
            () => _owner?.Move(this, -1), () => CanMoveUp);
        MoveDownCommand = new ViewModelCommand(
            () => _owner?.Move(this, 1), () => CanMoveDown);
        RemoveCommand = new ViewModelCommand(() => _owner?.Remove(this));
    }

    public IReadOnlyList<BehaviorFloatTargetOption> Targets { get; }

    /// <summary>
    /// Unknown native target values are retained for deletion or an unchanged
    /// round trip, but cannot be edited into a newly authored invalid row.
    /// </summary>
    public bool IsTargetEditable => !_isImportedUnknownTarget;

    public bool IsReadOnlyImportedTarget => _isImportedUnknownTarget;

    public string ImportedTargetText => IsReadOnlyImportedTarget
        ? $"Unknown imported target (0x{(int)Target:X}) is read-only. Delete this row to remove it."
        : string.Empty;

    public ItemFloatExpressionTarget Target
    {
        get => _target;
        set
        {
            if (!SetProperty(ref _target, value))
                return;

            OnPropertyChanged(nameof(SelectedTarget));
            OnPropertyChanged(nameof(ImportedTargetText));
            _changed();
        }
    }

    public BehaviorFloatTargetOption SelectedTarget
    {
        get => Targets.FirstOrDefault(option => option.Target == Target) ??
            _unknownTargetOption;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Target = value.Target;
        }
    }

    public BehaviorExpressionDraftViewModel Expression { get; }

    internal bool IsDuplicateTarget
    {
        get => _isDuplicateTarget;
        set
        {
            if (!SetProperty(ref _isDuplicateTarget, value))
                return;

            OnPropertyChanged(nameof(HasTargetError));
            OnPropertyChanged(nameof(TargetErrorText));
        }
    }

    private bool _isDuplicateTarget;

    public bool HasTargetError => IsDuplicateTarget;

    public string TargetErrorText => IsDuplicateTarget
        ? "Each float target can have only one binding."
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

    internal MenuBehaviorFloatExpressionBinding ToDomain() =>
        _source is { } source
            ? source with
            {
                Target = Target,
                Expression = Expression.ToBinding()
            }
            : new MenuBehaviorFloatExpressionBinding(
                Target,
                Expression.ToBinding());

    internal IReadOnlyList<string> Validate(int row)
    {
        var messages = new List<string>();
        messages.AddRange(Expression.Validate(
            $"Float binding {row} ({Target})", required: true,
            BehaviorExpressionResultKind.Float));
        return messages;
    }

    internal void AttachOwner(BehaviorBindingsViewModel owner) =>
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    internal void RefreshOrdering()
    {
        int index = _owner?.FloatBindings.IndexOf(this) ?? -1;
        CanMoveUp = index > 0;
        CanMoveDown = _owner is not null && index >= 0 &&
            index < _owner.FloatBindings.Count - 1;
    }

    private static string FormatTarget(ItemFloatExpressionTarget target) => target switch
    {
        ItemFloatExpressionTarget.RectX => "Rectangle X",
        ItemFloatExpressionTarget.RectY => "Rectangle Y",
        ItemFloatExpressionTarget.RectW => "Rectangle width",
        ItemFloatExpressionTarget.RectH => "Rectangle height",
        ItemFloatExpressionTarget.ForeColorR => "Foreground color red",
        ItemFloatExpressionTarget.ForeColorG => "Foreground color green",
        ItemFloatExpressionTarget.ForeColorB => "Foreground color blue",
        ItemFloatExpressionTarget.ForeColorRgb => "Foreground color RGB",
        ItemFloatExpressionTarget.ForeColorA => "Foreground color alpha",
        ItemFloatExpressionTarget.GlowColorR => "Glow color red",
        ItemFloatExpressionTarget.GlowColorG => "Glow color green",
        ItemFloatExpressionTarget.GlowColorB => "Glow color blue",
        ItemFloatExpressionTarget.GlowColorRgb => "Glow color RGB",
        ItemFloatExpressionTarget.GlowColorA => "Glow color alpha",
        ItemFloatExpressionTarget.BackColorR => "Background color red",
        ItemFloatExpressionTarget.BackColorG => "Background color green",
        ItemFloatExpressionTarget.BackColorB => "Background color blue",
        ItemFloatExpressionTarget.BackColorRgb => "Background color RGB",
        ItemFloatExpressionTarget.BackColorA => "Background color alpha",
        _ => target.ToString()
    };

    private static bool IsSupportedTarget(ItemFloatExpressionTarget target) =>
        MenuBehaviorFloatExpressionBindings.AllTargets.Contains(target);
}

public sealed record BehaviorFloatTargetOption(
    ItemFloatExpressionTarget Target,
    string Label)
{
    public override string ToString() => Label;
}
