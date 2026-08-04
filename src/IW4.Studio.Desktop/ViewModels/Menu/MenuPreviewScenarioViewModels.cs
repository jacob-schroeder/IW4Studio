using System.Globalization;
using IW4.Assets.Assets.Menu;
using IW4.Studio.Documents.MenuEditing;
using IW4.Studio.Documents.MenuEditing.Debugging;

namespace IW4.Studio.Desktop.ViewModels.Menu;

public enum MenuPreviewMode
{
    Authored,
    Simulate
}

public sealed record MenuPreviewBooleanOption(bool? Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// One explicit value in the editor-only simulation. Unset is a first-class
/// state: it asks the evaluator to report missing runtime state instead of
/// silently assuming a false, zero, or empty value.
/// </summary>
public sealed class MenuPreviewScenarioInputViewModel : ObservableObject
{
    private static readonly IReadOnlyList<MenuPreviewBooleanOption>
        BooleanOptionValues = Array.AsReadOnly(
        new MenuPreviewBooleanOption[]
        {
            new(null, "Unset"),
            new(false, "False"),
            new(true, "True")
        });

    private readonly Action _changed;
    private bool _isRequired;
    private bool _isSet;
    private decimal? _numericInput;
    private string _textInput;
    private string _valueInput;

    internal MenuPreviewScenarioInputViewModel(
        MenuDebugDependency dependency,
        Action changed,
        bool isSet = false,
        string? valueInput = null)
    {
        Dependency = dependency ?? throw new ArgumentNullException(
            nameof(dependency));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        _isSet = isSet;
        _valueInput = valueInput ?? DefaultValue(dependency.ValueKind);
        _textInput = _valueInput;
        _numericInput = ParseNumericValue(_isSet, _valueInput);
        ClearCommand = new ViewModelCommand(
            Clear,
            () => IsSet || HasPendingValue);
        SetEmptyCommand = new ViewModelCommand(
            SetEmpty,
            () => IsText &&
                  (!IsSet || ValueInput.Length > 0 || TextValue.Length > 0));
    }

    internal MenuDebugDependency Dependency { get; }

    internal string Identity => StorageKey(Dependency);

    public string KindLabel => Dependency.Kind switch
    {
        MenuDebugDependencyKind.Dvar => "Game variable",
        MenuDebugDependencyKind.LocalVariable => "Menu local",
        MenuDebugDependencyKind.Environment => "Runtime value",
        MenuDebugDependencyKind.Menu => "Open menu",
        _ => Dependency.Kind.ToString()
    };

    public string Name => Dependency.Name;

    public string ValueHint => Dependency.ValueKind?.ToString() ?? "String";

    public string Detail => $"{KindLabel} · {ValueHint} · {ValueState}";

    public string ValueState => !IsSet
        ? "Unset"
        : IsText && ValueInput.Length == 0
            ? "Set to empty"
            : "Set";

    public bool IsBoolean =>
        Dependency.ValueKind == MenuDebugValueKind.Boolean;

    public bool IsInteger =>
        Dependency.ValueKind == MenuDebugValueKind.Integer;

    public bool IsFloat =>
        Dependency.ValueKind == MenuDebugValueKind.Float;

    public bool IsText => !IsBoolean && !IsInteger && !IsFloat;

    public IReadOnlyList<MenuPreviewBooleanOption> BooleanOptions =>
        BooleanOptionValues;

    public MenuPreviewBooleanOption SelectedBoolean
    {
        get
        {
            if (!IsSet || !TryGetValue(out MenuDebugValue value, out _) ||
                !value.TryGetBoolean(out bool boolean))
            {
                return BooleanOptionValues[0];
            }

            return BooleanOptionValues[boolean ? 2 : 1];
        }
        set
        {
            value ??= BooleanOptionValues[0];
            if (value.Value is not { } boolean)
            {
                SetRawValue(false, DefaultValue(Dependency.ValueKind));
                return;
            }

            SetRawValue(true, boolean ? "true" : "false");
        }
    }

    public decimal? NumericValue
    {
        get => _numericInput;
        set
        {
            if (!SetProperty(ref _numericInput, value))
                return;

            NotifyPendingValueChanged();
        }
    }

    public string TextValue
    {
        get => _textInput;
        set
        {
            if (!SetProperty(ref _textInput, value ?? string.Empty))
                return;

            NotifyPendingValueChanged();
        }
    }

    public bool HasPendingValue => IsText
        ? !string.Equals(TextValue, ValueInput, StringComparison.Ordinal)
        : IsInteger || IsFloat
            ? NumericValue != ParseNumericValue(IsSet, ValueInput)
            : false;

    /// <summary>
    /// Compatibility surface for callers that preserve raw input while a
    /// document snapshot is replaced. New UI binds typed editor properties.
    /// </summary>
    public string ValueInput
    {
        get => _valueInput;
        set => SetRawValue(true, value ?? string.Empty);
    }

    public bool IsSet
    {
        get => _isSet;
        set => SetRawValue(
            value,
            value ? ValueInput : DefaultValue(Dependency.ValueKind));
    }

    public bool IsRequired
    {
        get => _isRequired;
        internal set => SetProperty(ref _isRequired, value);
    }

    public bool HasValidationError =>
        IsSet && !TryGetValue(out _, out _);

    public string? ValidationMessage
    {
        get
        {
            TryGetValue(out _, out string? error);
            return error;
        }
    }

    public ViewModelCommand ClearCommand { get; }

    public ViewModelCommand SetEmptyCommand { get; }

    internal bool TryGetValue(out MenuDebugValue value, out string? error)
    {
        error = null;
        MenuDebugValueKind kind = Dependency.ValueKind ??
            MenuDebugValueKind.String;
        switch (kind)
        {
            case MenuDebugValueKind.Integer when
                int.TryParse(
                    ValueInput,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int integer):
                value = MenuDebugValue.FromInt(integer);
                return true;
            case MenuDebugValueKind.Float when
                float.TryParse(
                    ValueInput,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float floatingPoint) &&
                float.IsFinite(floatingPoint):
                value = MenuDebugValue.FromFloat(floatingPoint);
                return true;
            case MenuDebugValueKind.Boolean when
                TryBoolean(ValueInput, out bool boolean):
                value = MenuDebugValue.FromBoolean(boolean);
                return true;
            case MenuDebugValueKind.String:
                value = MenuDebugValue.FromString(ValueInput);
                return true;
            default:
                value = default;
                error = $"Enter a valid {kind} value.";
                return false;
        }
    }

    internal void Clear() =>
        SetRawValue(false, DefaultValue(Dependency.ValueKind));

    public bool CommitPendingValue()
    {
        if (!HasPendingValue)
            return false;

        if (IsText)
        {
            SetRawValue(true, TextValue);
        }
        else if (NumericValue is { } numericValue)
        {
            SetRawValue(true, FormatNumericValue(numericValue));
        }
        else
        {
            SetRawValue(false, DefaultValue(Dependency.ValueKind));
        }

        return true;
    }

    public void ResetPendingValue()
    {
        if (!HasPendingValue)
            return;

        _textInput = ValueInput;
        _numericInput = ParseNumericValue(IsSet, ValueInput);
        OnPropertyChanged(nameof(TextValue));
        OnPropertyChanged(nameof(NumericValue));
        NotifyPendingValueChanged();
    }

    private void SetEmpty() => SetRawValue(true, string.Empty);

    private void SetRawValue(bool isSet, string value)
    {
        bool appliedValueChanged =
            _isSet != isSet ||
            !string.Equals(_valueInput, value, StringComparison.Ordinal);
        bool textInputChanged =
            !string.Equals(_textInput, value, StringComparison.Ordinal);
        decimal? numericInput = ParseNumericValue(isSet, value);
        bool numericInputChanged = _numericInput != numericInput;
        if (!appliedValueChanged && !textInputChanged &&
            !numericInputChanged)
        {
            return;
        }

        _isSet = isSet;
        _valueInput = value;
        _textInput = value;
        _numericInput = numericInput;
        OnPropertyChanged(nameof(IsSet));
        OnPropertyChanged(nameof(ValueInput));
        OnPropertyChanged(nameof(TextValue));
        OnPropertyChanged(nameof(NumericValue));
        OnPropertyChanged(nameof(SelectedBoolean));
        OnPropertyChanged(nameof(HasValidationError));
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(ValueState));
        OnPropertyChanged(nameof(Detail));
        NotifyPendingValueChanged();
        if (appliedValueChanged)
            _changed();
    }

    private void NotifyPendingValueChanged()
    {
        OnPropertyChanged(nameof(HasPendingValue));
        ClearCommand.RaiseCanExecuteChanged();
        SetEmptyCommand.RaiseCanExecuteChanged();
    }

    private string FormatNumericValue(decimal value)
    {
        string format = IsInteger ? "0" : "0.######";
        return value.ToString(format, CultureInfo.InvariantCulture);
    }

    private decimal? ParseNumericValue(bool isSet, string value) =>
        isSet && (IsInteger || IsFloat) &&
        decimal.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out decimal numericValue)
                ? numericValue
                : null;

    private static string DefaultValue(MenuDebugValueKind? kind) => kind switch
    {
        MenuDebugValueKind.Boolean => "false",
        MenuDebugValueKind.Integer => "0",
        MenuDebugValueKind.Float => "0",
        _ => string.Empty
    };

    private static bool TryBoolean(string text, out bool value)
    {
        if (bool.TryParse(text, out value))
            return true;
        if (text == "1")
        {
            value = true;
            return true;
        }
        if (text == "0")
        {
            value = false;
            return true;
        }

        value = false;
        return false;
    }

    internal static string StorageKey(MenuDebugDependency dependency) =>
        dependency.Kind == MenuDebugDependencyKind.Environment
            ? $"{dependency.Kind}:{dependency.Operation}:{dependency.Name}"
            : $"{dependency.Kind}:{dependency.Name}";
}

/// <summary>
/// Owns presentation and base input state for a simulation. The debug view
/// model composes this immutable base with the runtime state produced by menu
/// activation and explicit events.
/// </summary>
public sealed class MenuPreviewSimulationViewModel : ObservableObject
{
    private readonly Action _inputsChanged;
    private readonly Action _timeChanged;
    private IReadOnlyList<MenuPreviewScenarioInputViewModel> _inputs = [];
    private IReadOnlyList<string> _unsupportedInputLines = [];
    private int _milliseconds;
    private bool _isBatching;
    private bool _isConfigurationOpen;

    internal MenuPreviewSimulationViewModel(
        Action inputsChanged,
        Action timeChanged)
    {
        _inputsChanged = inputsChanged ??
            throw new ArgumentNullException(nameof(inputsChanged));
        _timeChanged = timeChanged ??
            throw new ArgumentNullException(nameof(timeChanged));
        ClearOverridesCommand = new ViewModelCommand(
            ClearOverrides,
            () => HasOverrides);
        OpenConfigurationCommand = new ViewModelCommand(
            () => IsConfigurationOpen = true);
    }

    public IReadOnlyList<MenuPreviewScenarioInputViewModel> Inputs => _inputs;

    public IReadOnlyList<MenuPreviewScenarioInputViewModel> OrderedInputs =>
        Array.AsReadOnly(Inputs
            .OrderByDescending(value => value.IsRequired)
            .ThenBy(value => value.Dependency.Kind)
            .ThenBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray());

    public IReadOnlyList<MenuPreviewScenarioInputViewModel> MissingInputs =>
        Array.AsReadOnly(Inputs
            .Where(value => value.IsRequired && !value.IsSet)
            .ToArray());

    public IReadOnlyList<string> UnsupportedInputLines =>
        _unsupportedInputLines;

    public bool HasInputs => Inputs.Count > 0;
    public bool HasUnsupportedInputs => UnsupportedInputLines.Count > 0;
    public bool HasMissingInputs => MissingInputs.Count > 0;
    public int MissingInputCount => MissingInputs.Count;
    public int OverrideCount => Inputs.Count(value => value.IsSet);
    public bool HasOverrides => OverrideCount > 0;

    public string MissingInputSummary => !HasMissingInputs
        ? "No editable values are currently required."
        : "Set " + string.Join(
            ", ",
            MissingInputs.Take(4).Select(value => value.Name)) +
          (MissingInputCount > 4 ? $" and {MissingInputCount - 4:N0} more" :
              string.Empty) + ".";

    public string ValuesHeading => HasInputs
        ? $"Simulation values · {OverrideCount:N0} set / {Inputs.Count:N0} available"
        : "No editable simulation values";

    public int Milliseconds
    {
        get => _milliseconds;
        set
        {
            if (!SetProperty(ref _milliseconds, value))
                return;
            RaiseTimeChanged();
        }
    }

    public bool IsConfigurationOpen
    {
        get => _isConfigurationOpen;
        set => SetProperty(ref _isConfigurationOpen, value);
    }

    public ViewModelCommand ClearOverridesCommand { get; }
    public ViewModelCommand OpenConfigurationCommand { get; }

    internal void ReplaceDocument(MenuEditorSnapshot? snapshot)
    {
        var previous = Inputs.ToDictionary(
            value => value.Identity,
            value => (value.IsSet, value.ValueInput),
            StringComparer.OrdinalIgnoreCase);
        MenuDebugDependency[] dependencies = snapshot?.DebugProgram.Dependencies
            .Where(IsEditableInput)
            .GroupBy(
                MenuPreviewScenarioInputViewModel.StorageKey,
                StringComparer.OrdinalIgnoreCase)
            .Select(CollapseDependency)
            .OrderBy(value => value.Kind)
            .ThenBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        _inputs = Array.AsReadOnly(dependencies.Select(dependency =>
        {
            var probe = new MenuPreviewScenarioInputViewModel(
                dependency,
                InputChanged);
            return previous.TryGetValue(probe.Identity, out var state)
                ? new MenuPreviewScenarioInputViewModel(
                    dependency,
                    InputChanged,
                    state.IsSet,
                    state.ValueInput)
                : probe;
        }).ToArray());

        _unsupportedInputLines = Array.AsReadOnly(
            (snapshot?.DebugProgram.Dependencies ?? [])
                .Where(value => string.Equals(
                    value.Name,
                    "<dynamic>",
                    StringComparison.OrdinalIgnoreCase))
                .Select(FormatUnsupportedInput)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray());

        NotifyStateChanged();
    }

    internal void SetRequiredDependencies(
        IEnumerable<MenuDebugDependency> dependencies)
    {
        HashSet<string> required = dependencies
            .Select(MenuPreviewScenarioInputViewModel.StorageKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (MenuPreviewScenarioInputViewModel input in Inputs)
            input.IsRequired = !input.IsSet && required.Contains(input.Identity);
        NotifyInputCollectionsChanged();
    }

    internal MenuDebugScenario BuildScenario(
        MenuDebugLocalizationResolver? localizationResolver)
    {
        var dvars = new Dictionary<string, MenuDebugValue>(
            StringComparer.OrdinalIgnoreCase);
        var locals = new Dictionary<string, MenuDebugValue>(
            StringComparer.OrdinalIgnoreCase);
        var environment = new Dictionary<
            MenuDebugEnvironmentKey,
            MenuDebugValue>();
        var openMenus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (MenuPreviewScenarioInputViewModel input in Inputs)
        {
            if (!input.IsSet ||
                !input.TryGetValue(out MenuDebugValue value, out _))
            {
                continue;
            }

            MenuDebugDependency dependency = input.Dependency;
            switch (dependency.Kind)
            {
                case MenuDebugDependencyKind.Dvar:
                    dvars[dependency.Name] = value;
                    break;
                case MenuDebugDependencyKind.LocalVariable:
                    locals[dependency.Name] = value;
                    break;
                case MenuDebugDependencyKind.Environment when
                    dependency.Operation is { } operation:
                    environment[new MenuDebugEnvironmentKey(
                        operation,
                        EnvironmentQualifier(dependency))] = value;
                    break;
                case MenuDebugDependencyKind.Menu when
                    value.TryGetBoolean(out bool isOpen) && isOpen:
                    openMenus.Add(dependency.Name);
                    break;
            }
        }

        return new MenuDebugScenario(
            Milliseconds,
            dvars,
            locals,
            environment,
            openMenus,
            focusedItemId: null,
            localizationResolver);
    }

    private void ClearOverrides()
    {
        if (!HasOverrides)
            return;
        _isBatching = true;
        try
        {
            foreach (MenuPreviewScenarioInputViewModel input in Inputs)
                input.Clear();
        }
        finally
        {
            _isBatching = false;
        }
        NotifyInputCollectionsChanged();
        _inputsChanged();
    }

    private void InputChanged()
    {
        NotifyInputCollectionsChanged();
        if (!_isBatching)
            _inputsChanged();
    }

    private void RaiseTimeChanged()
    {
        if (!_isBatching)
            _timeChanged();
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(Inputs));
        NotifyInputCollectionsChanged();
        OnPropertyChanged(nameof(UnsupportedInputLines));
        OnPropertyChanged(nameof(HasUnsupportedInputs));
    }

    private void NotifyInputCollectionsChanged()
    {
        OnPropertyChanged(nameof(OrderedInputs));
        OnPropertyChanged(nameof(MissingInputs));
        OnPropertyChanged(nameof(HasInputs));
        OnPropertyChanged(nameof(HasMissingInputs));
        OnPropertyChanged(nameof(MissingInputCount));
        OnPropertyChanged(nameof(MissingInputSummary));
        OnPropertyChanged(nameof(OverrideCount));
        OnPropertyChanged(nameof(HasOverrides));
        OnPropertyChanged(nameof(ValuesHeading));
        ClearOverridesCommand.RaiseCanExecuteChanged();
    }

    private static bool IsEditableInput(MenuDebugDependency dependency) =>
        !string.Equals(
            dependency.Name,
            "<dynamic>",
            StringComparison.OrdinalIgnoreCase) &&
        (dependency.Kind is
            MenuDebugDependencyKind.Dvar or
            MenuDebugDependencyKind.LocalVariable or
            MenuDebugDependencyKind.Menu ||
         dependency.Kind == MenuDebugDependencyKind.Environment &&
         dependency.Operation is not null &&
         dependency.Operation != OperationEnum.OP_MILLISECONDS);

    private static MenuDebugDependency CollapseDependency(
        IGrouping<string, MenuDebugDependency> group)
    {
        MenuDebugDependency first = group.First();
        MenuDebugValueKind?[] kinds = group
            .Select(value => value.ValueKind)
            .Distinct()
            .ToArray();
        return kinds.Length <= 1
            ? first
            : first with { ValueKind = MenuDebugValueKind.String };
    }

    private static string? EnvironmentQualifier(
        MenuDebugDependency dependency) =>
        dependency.Operation is { } operation &&
        !string.Equals(
            dependency.Name,
            operation.ToString(),
            StringComparison.OrdinalIgnoreCase)
                ? dependency.Name
                : null;

    private static string FormatUnsupportedInput(MenuDebugDependency value) =>
        value.Operation is { } operation
            ? $"{operation}: runtime-computed name cannot be overridden"
            : $"{value.Kind}: runtime-computed name cannot be overridden";
}
