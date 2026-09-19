using System.Collections.ObjectModel;
using IW4.Studio.Desktop.ViewModels;
using IW4.Studio.Documents.MenuEditing.Behavior;
using IW4.Studio.Documents.MenuEditing.Behavior.Expressions;

namespace IW4.Studio.Desktop.Editors.Menu.Behavior;

/// <summary>
/// One modal-local projection of the Menu-wide expression support graph. It
/// permits only deterministic static-dvar appends; defining reusable UI
/// functions needs its own expression-definition surface and is intentionally
/// not represented by a fabricated empty support row.
/// </summary>
public sealed class BehaviorExpressionSupportDraftViewModel : ObservableObject
{
    private readonly BehaviorExpressionSupport _source;
    private readonly Action _changed;
    private readonly List<string> _staticDvarNames = [];
    private BehaviorExpressionSupport _current;
    private string _newStaticDvarName = string.Empty;
    private string? _staticDvarError;

    internal BehaviorExpressionSupportDraftViewModel(
        BehaviorExpressionSupport source,
        Action changed)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        _current = source;
        StaticDvars = new ObservableCollection<BehaviorStaticDvarOption>(
            source.StaticDvars.Select(value =>
                new BehaviorStaticDvarOption(value)));
        AddStaticDvarCommand = new ViewModelCommand(
            AddStaticDvar,
            () => CanAppendStaticDvar &&
                !string.IsNullOrWhiteSpace(NewStaticDvarName));
    }

    public ObservableCollection<BehaviorStaticDvarOption> StaticDvars { get; }

    public string NewStaticDvarName
    {
        get => _newStaticDvarName;
        set
        {
            value ??= string.Empty;
            if (!SetProperty(ref _newStaticDvarName, value))
                return;

            StaticDvarError = null;
            AddStaticDvarCommand.RaiseCanExecuteChanged();
        }
    }

    public string? StaticDvarError
    {
        get => _staticDvarError;
        private set
        {
            if (!SetProperty(ref _staticDvarError, value))
                return;

            OnPropertyChanged(nameof(HasStaticDvarError));
        }
    }

    public bool HasStaticDvarError => !string.IsNullOrWhiteSpace(
        StaticDvarError);

    public bool CanAppendStaticDvar => _source.HasSourceTable;

    public bool HasAppendedStaticDvars => _staticDvarNames.Count != 0;

    public string Summary => HasAppendedStaticDvars
        ? $"{_staticDvarNames.Count} new static dvar" +
          (_staticDvarNames.Count == 1 ? string.Empty : "s")
        : "No new support rows";

    public ViewModelCommand AddStaticDvarCommand { get; }

    internal event EventHandler? SupportChanged;

    internal bool AppliesTo(BehaviorExpressionSupport bindingSupport) =>
        bindingSupport.HasSameSourceTable(_source) ||
        !bindingSupport.HasSourceTable;

    internal BehaviorExpressionSupport Resolve(
        BehaviorExpressionSupport bindingSupport) => AppliesTo(bindingSupport)
            ? _current
            : bindingSupport;

    internal MenuBehaviorExpressionSupportDelta ToDelta() =>
        _staticDvarNames.Count == 0
            ? MenuBehaviorExpressionSupportDelta.Empty
            : new MenuBehaviorExpressionSupportDelta(
                _source.StaticDvars.Count,
                _staticDvarNames);

    private void AddStaticDvar()
    {
        string name = NewStaticDvarName.Trim();
        if (string.IsNullOrEmpty(name))
            return;
        if (!CanAppendStaticDvar)
        {
            StaticDvarError =
                "This Menu has no expression support table to extend.";
            return;
        }
        if (StaticDvars.Any(value => string.Equals(
                value.Reference.Name,
                name,
                StringComparison.OrdinalIgnoreCase)))
        {
            StaticDvarError = $"'{name}' is already in this Menu's static-dvar table.";
            return;
        }

        _staticDvarNames.Add(name);
        _current = _source.WithAppendedStaticDvars(_staticDvarNames);
        StaticDvars.Add(new BehaviorStaticDvarOption(
            _current.StaticDvars[^1]));
        NewStaticDvarName = string.Empty;
        OnPropertyChanged(nameof(HasAppendedStaticDvars));
        OnPropertyChanged(nameof(Summary));
        SupportChanged?.Invoke(this, EventArgs.Empty);
        _changed();
    }
}
