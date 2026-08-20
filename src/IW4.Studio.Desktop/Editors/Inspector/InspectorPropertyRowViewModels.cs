using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using IW4.FastFiles.Zone;
using IW4.Studio.Desktop.ViewModels;

namespace IW4.Studio.Desktop.Editors.Inspector;

/// <summary>
/// Implemented by rows that deliberately stage user text before updating an
/// editor draft. Views commit these rows on Enter or focus loss.
/// </summary>
public interface IInspectorStagedPropertyRow
{
    bool HasStagedValue { get; }

    bool CommitInput();

    void ResetInput();
}

/// <summary>Common presentation and validation state for one explicit row.</summary>
public abstract class InspectorPropertyRowViewModel : ObservableObject
{
    private string? _validationMessage;
    private bool _isInteractionBlocked;

    protected InspectorPropertyRowViewModel(
        string label,
        string fieldPath,
        string? description,
        bool isReadOnly)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);

        Label = label;
        FieldPath = fieldPath;
        Description = description;
        IsReadOnly = isReadOnly;
    }

    public string Label { get; }

    public string FieldPath { get; }

    public string? Description { get; }

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    /// <summary>
    /// Requests a visible information affordance beside the row label. The
    /// view uses <see cref="Description"/> as its tooltip content.
    /// </summary>
    public bool ShowInfoIcon { get; init; }

    public bool HasInfoIcon => ShowInfoIcon && HasDescription;

    public bool IsReadOnly { get; }

    public bool IsInteractionBlocked => _isInteractionBlocked;

    public bool IsEditable => !IsReadOnly && !_isInteractionBlocked;

    public string? ValidationMessage
    {
        get => _validationMessage;
        private set
        {
            if (!SetProperty(ref _validationMessage, value))
                return;

            OnPropertyChanged(nameof(HasValidationError));
        }
    }

    public bool HasValidationError =>
        !string.IsNullOrWhiteSpace(ValidationMessage);

    protected void SetValidationMessage(string? message) =>
        ValidationMessage = message;

    protected bool TryApply(Action apply)
    {
        ArgumentNullException.ThrowIfNull(apply);

        try
        {
            apply();
            SetValidationMessage(null);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidOperationException or
            OverflowException)
        {
            SetValidationMessage(exception.Message);
            return false;
        }
    }

    internal void SetInteractionBlocked(bool value)
    {
        if (!SetProperty(
                ref _isInteractionBlocked,
                value,
                nameof(IsInteractionBlocked)))
            return;

        OnPropertyChanged(nameof(IsEditable));
        OnInteractionStateChanged();
    }

    protected virtual void OnInteractionStateChanged()
    {
    }
}

/// <summary>
/// Base for a single text input whose parsed value is committed explicitly.
/// </summary>
public abstract class InspectorStagedPropertyRowViewModel<TValue>
    : InspectorPropertyRowViewModel,
      IInspectorStagedPropertyRow
{
    private readonly Action<TValue>? _apply;
    private string _appliedInput;
    private string _input;

    protected InspectorStagedPropertyRowViewModel(
        string label,
        string fieldPath,
        string input,
        Action<TValue>? apply,
        string? description,
        bool isReadOnly)
        : base(label, fieldPath, description, isReadOnly || apply is null)
    {
        _apply = apply;
        _appliedInput = input;
        _input = input;
    }

    public string Input
    {
        get => _input;
        set
        {
            value ??= string.Empty;
            if (!SetProperty(ref _input, value))
                return;

            ValidateInput();
            OnPropertyChanged(nameof(HasStagedValue));
            OnPropertyChanged(nameof(CanCommit));
        }
    }

    public bool HasStagedValue =>
        !string.Equals(Input, _appliedInput, StringComparison.Ordinal);

    public bool CanCommit =>
        IsEditable && HasStagedValue && !HasValidationError;

    public bool CommitInput()
    {
        if (!IsEditable || !HasStagedValue || _apply is null)
            return false;

        if (!TryParse(Input, out TValue? value, out string? error))
        {
            SetValidationMessage(error);
            OnPropertyChanged(nameof(CanCommit));
            return false;
        }

        if (!TryApply(() => _apply(value!)))
        {
            OnPropertyChanged(nameof(CanCommit));
            return false;
        }

        _appliedInput = Input;
        OnPropertyChanged(nameof(HasStagedValue));
        OnPropertyChanged(nameof(CanCommit));
        return true;
    }

    public void ResetInput()
    {
        Input = _appliedInput;
        SetValidationMessage(null);
        OnPropertyChanged(nameof(CanCommit));
    }

    public void RefreshAppliedValue(TValue value)
    {
        string input = Format(value);
        _appliedInput = input;
        _input = input;
        SetValidationMessage(null);
        OnPropertyChanged(nameof(Input));
        OnPropertyChanged(nameof(HasStagedValue));
        OnPropertyChanged(nameof(CanCommit));
    }

    protected abstract string Format(TValue value);

    protected abstract bool TryParse(
        string input,
        out TValue? value,
        out string? error);

    protected void ValidateInput()
    {
        _ = TryParse(Input, out _, out string? error);
        SetValidationMessage(error);
    }
}

public sealed class InspectorTextPropertyRowViewModel
    : InspectorStagedPropertyRowViewModel<string>
{
    private readonly Func<string, string?>? _validate;

    public InspectorTextPropertyRowViewModel(
        string label,
        string fieldPath,
        string? value,
        Action<string>? apply = null,
        Func<string, string?>? validate = null,
        string? description = null,
        bool isReadOnly = false)
        : base(
            label,
            fieldPath,
            value ?? string.Empty,
            apply,
            description,
            isReadOnly)
    {
        _validate = validate;
        ValidateInput();
    }

    protected override string Format(string value) => value;

    protected override bool TryParse(
        string input,
        out string? value,
        out string? error)
    {
        value = input;
        error = _validate?.Invoke(input);
        return error is null;
    }
}

public sealed class InspectorIntegerPropertyRowViewModel
    : InspectorStagedPropertyRowViewModel<int>
{
    public InspectorIntegerPropertyRowViewModel(
        string label,
        string fieldPath,
        int value,
        Action<int>? apply = null,
        string? description = null,
        bool isReadOnly = false)
        : base(
            label,
            fieldPath,
            value.ToString(CultureInfo.InvariantCulture),
            apply,
            description,
            isReadOnly)
    {
        ValidateInput();
    }

    protected override string Format(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    protected override bool TryParse(
        string input,
        out int value,
        out string? error)
    {
        bool parsed = int.TryParse(
            input,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);
        error = parsed ? null : "Enter a whole number.";
        return parsed;
    }
}

/// <summary>Invariant, staged editor for a serialized unsigned 32-bit value.</summary>
public sealed class InspectorUnsignedIntegerPropertyRowViewModel
    : InspectorStagedPropertyRowViewModel<uint>
{
    private readonly uint _maxValue;

    public InspectorUnsignedIntegerPropertyRowViewModel(
        string label,
        string fieldPath,
        uint value,
        Action<uint>? apply = null,
        string? description = null,
        bool isReadOnly = false,
        uint maxValue = uint.MaxValue)
        : base(label, fieldPath, value.ToString(CultureInfo.InvariantCulture), apply, description, isReadOnly)
    {
        _maxValue = maxValue;
        ValidateInput();
    }

    protected override string Format(uint value) => value.ToString(CultureInfo.InvariantCulture);

    protected override bool TryParse(string input, out uint value, out string? error)
    {
        bool parsed = uint.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        if (!parsed)
        {
            error = "Enter an unsigned whole number.";
            return false;
        }
        error = value <= _maxValue ? null : $"Enter a value from 0 through {_maxValue.ToString(CultureInfo.InvariantCulture)}.";
        return error is null;
    }
}

public sealed class InspectorFloatPropertyRowViewModel
    : InspectorStagedPropertyRowViewModel<float>
{
    public InspectorFloatPropertyRowViewModel(
        string label,
        string fieldPath,
        float value,
        Action<float>? apply = null,
        string? description = null,
        bool isReadOnly = false)
        : base(
            label,
            fieldPath,
            FormatInvariant(value),
            apply,
            description,
            isReadOnly)
    {
        ValidateInput();
    }

    protected override string Format(float value) => FormatInvariant(value);

    protected override bool TryParse(
        string input,
        out float value,
        out string? error)
    {
        bool parsed = float.TryParse(
            input,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value) && float.IsFinite(value);
        error = parsed ? null : "Enter a finite number.";
        return parsed;
    }

    private static string FormatInvariant(float value) =>
        value.ToString("G9", CultureInfo.InvariantCulture);
}

public sealed class InspectorBooleanPropertyRowViewModel
    : InspectorPropertyRowViewModel
{
    private readonly Action<bool>? _apply;
    private bool _value;
    private bool _isUpdating;

    public InspectorBooleanPropertyRowViewModel(
        string label,
        string fieldPath,
        bool value,
        Action<bool>? apply = null,
        string? description = null,
        bool isReadOnly = false)
        : base(label, fieldPath, description, isReadOnly || apply is null)
    {
        _value = value;
        _apply = apply;
    }

    public bool Value
    {
        get => _value;
        set
        {
            if (_isUpdating || value == _value || !IsEditable || _apply is null)
                return;

            bool previous = _value;
            _value = value;
            OnPropertyChanged();
            if (TryApply(() => _apply(value)))
                return;

            _isUpdating = true;
            _value = previous;
            OnPropertyChanged();
            _isUpdating = false;
        }
    }
}

public sealed record InspectorChoice(string Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed class InspectorChoicePropertyRowViewModel
    : InspectorPropertyRowViewModel
{
    private readonly Action<string>? _apply;
    private InspectorChoice _selectedChoice;
    private bool _isUpdating;

    public InspectorChoicePropertyRowViewModel(
        string label,
        string fieldPath,
        IEnumerable<InspectorChoice> choices,
        string selectedValue,
        Action<string>? apply = null,
        string? description = null,
        bool isReadOnly = false)
        : base(label, fieldPath, description, isReadOnly || apply is null)
    {
        ArgumentNullException.ThrowIfNull(choices);
        InspectorChoice[] materialized = choices.ToArray();
        if (materialized.Length == 0)
        {
            throw new ArgumentException(
                "An inspector choice row must contain at least one option.",
                nameof(choices));
        }

        Choices = Array.AsReadOnly(materialized);
        _selectedChoice = materialized.FirstOrDefault(choice =>
                string.Equals(choice.Value, selectedValue, StringComparison.Ordinal))
            ?? new InspectorChoice(selectedValue, $"Unknown ({selectedValue})");
        if (!materialized.Contains(_selectedChoice))
            Choices = Array.AsReadOnly([.. materialized, _selectedChoice]);
        _apply = apply;
    }

    public IReadOnlyList<InspectorChoice> Choices { get; private set; }

    public InspectorChoice SelectedChoice
    {
        get => _selectedChoice;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_isUpdating || Equals(value, _selectedChoice) || !IsEditable || _apply is null)
                return;

            InspectorChoice previous = _selectedChoice;
            _selectedChoice = value;
            OnPropertyChanged();
            if (TryApply(() => _apply(value.Value)))
                return;

            _isUpdating = true;
            _selectedChoice = previous;
            OnPropertyChanged();
            _isUpdating = false;
        }
    }
}

public sealed class InspectorFlagOptionViewModel : ObservableObject
{
    private readonly Action<InspectorFlagOptionViewModel> _changed;
    private bool _isSet;

    internal InspectorFlagOptionViewModel(
        string label,
        ulong mask,
        bool isSet,
        Action<InspectorFlagOptionViewModel> changed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (mask == 0)
            throw new ArgumentOutOfRangeException(nameof(mask));

        Label = label;
        Mask = mask;
        _isSet = isSet;
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
    }

    public string Label { get; }

    public ulong Mask { get; }

    public bool IsSet
    {
        get => _isSet;
        set
        {
            if (!SetProperty(ref _isSet, value))
                return;

            _changed(this);
        }
    }

    internal void Restore(bool value)
    {
        _isSet = value;
        OnPropertyChanged(nameof(IsSet));
    }
}

public sealed class InspectorFlagsPropertyRowViewModel
    : InspectorPropertyRowViewModel
{
    private readonly Action<ulong>? _apply;
    private readonly ulong _unknownBits;
    private bool _isUpdating;

    public InspectorFlagsPropertyRowViewModel(
        string label,
        string fieldPath,
        ulong value,
        IEnumerable<(string Label, ulong Mask)> options,
        Action<ulong>? apply = null,
        string? description = null,
        bool isReadOnly = false)
        : base(label, fieldPath, description, isReadOnly || apply is null)
    {
        ArgumentNullException.ThrowIfNull(options);
        (string Label, ulong Mask)[] materialized = options.ToArray();
        ulong knownMask = materialized.Aggregate(
            0UL,
            (current, option) => current | option.Mask);
        _unknownBits = value & ~knownMask;
        _apply = apply;
        Options = Array.AsReadOnly(materialized
            .Select(option => new InspectorFlagOptionViewModel(
                option.Label,
                option.Mask,
                (value & option.Mask) == option.Mask,
                OptionChanged))
            .ToArray());
    }

    public IReadOnlyList<InspectorFlagOptionViewModel> Options { get; }

    public bool HasUnknownBits => _unknownBits != 0;

    public string UnknownBitsText => $"Unknown bits: 0x{_unknownBits:X}";

    private void OptionChanged(InspectorFlagOptionViewModel changed)
    {
        if (_isUpdating || !IsEditable || _apply is null)
            return;

        ulong value = _unknownBits;
        foreach (InspectorFlagOptionViewModel option in Options)
        {
            if (option.IsSet)
                value |= option.Mask;
        }

        bool requestedValue = changed.IsSet;
        if (TryApply(() => _apply(value)))
            return;

        _isUpdating = true;
        changed.Restore(!requestedValue);
        _isUpdating = false;
    }
}

public readonly record struct InspectorColorValue(
    float Red,
    float Green,
    float Blue,
    float Alpha);

public sealed class InspectorColorPropertyRowViewModel
    : InspectorPropertyRowViewModel,
      IInspectorStagedPropertyRow
{
    private readonly Action<InspectorColorValue>? _apply;
    private InspectorColorValue _appliedValue;
    private string _redInput;
    private string _greenInput;
    private string _blueInput;
    private string _alphaInput;

    public InspectorColorPropertyRowViewModel(
        string label,
        string fieldPath,
        InspectorColorValue value,
        Action<InspectorColorValue>? apply = null,
        string? description = null,
        bool isReadOnly = false)
        : base(label, fieldPath, description, isReadOnly || apply is null)
    {
        _appliedValue = value;
        _redInput = Format(value.Red);
        _greenInput = Format(value.Green);
        _blueInput = Format(value.Blue);
        _alphaInput = Format(value.Alpha);
        _apply = apply;
        ValidateInput();
    }

    public string RedInput
    {
        get => _redInput;
        set => SetInput(ref _redInput, value);
    }

    public string GreenInput
    {
        get => _greenInput;
        set => SetInput(ref _greenInput, value);
    }

    public string BlueInput
    {
        get => _blueInput;
        set => SetInput(ref _blueInput, value);
    }

    public string AlphaInput
    {
        get => _alphaInput;
        set => SetInput(ref _alphaInput, value);
    }

    public bool HasStagedValue =>
        !EqualsInput(_appliedValue);

    public bool CanCommit =>
        IsEditable && HasStagedValue && !HasValidationError;

    public InspectorColorValue CurrentValue =>
        TryReadValue(out InspectorColorValue value) ? value : _appliedValue;

    public IBrush PreviewBrush =>
        new SolidColorBrush(ToAvaloniaColor(CurrentValue));

    public bool CommitInput()
    {
        if (!CanCommit || !TryReadValue(out InspectorColorValue value))
            return false;

        return SetValue(value);
    }

    public bool SetValue(InspectorColorValue value)
    {
        if (!IsEditable || _apply is null)
            return false;

        if (!IsFinite(value))
        {
            SetValidationMessage("Color components must be finite numbers.");
            return false;
        }

        if (!TryApply(() => _apply(value)))
            return false;

        _appliedValue = value;
        SetInputs(value);
        NotifyInputStateChanged();
        return true;
    }

    public void ResetInput()
    {
        SetInputs(_appliedValue);
        NotifyInputStateChanged();
    }

    private void SetInput(
        ref string field,
        string? value,
        [CallerMemberName] string? propertyName = null)
    {
        value ??= string.Empty;
        if (!SetProperty(ref field, value, propertyName))
            return;

        ValidateInput();
        NotifyInputStateChanged();
    }

    private bool TryReadValue(out InspectorColorValue value)
    {
        bool valid = TryParseFloat(RedInput, out float red) &
            TryParseFloat(GreenInput, out float green) &
            TryParseFloat(BlueInput, out float blue) &
            TryParseFloat(AlphaInput, out float alpha);
        value = new InspectorColorValue(red, green, blue, alpha);
        return valid;
    }

    private bool EqualsInput(InspectorColorValue value) =>
        string.Equals(RedInput, Format(value.Red), StringComparison.Ordinal) &&
        string.Equals(GreenInput, Format(value.Green), StringComparison.Ordinal) &&
        string.Equals(BlueInput, Format(value.Blue), StringComparison.Ordinal) &&
        string.Equals(AlphaInput, Format(value.Alpha), StringComparison.Ordinal);

    private void SetInputs(InspectorColorValue value)
    {
        _redInput = Format(value.Red);
        _greenInput = Format(value.Green);
        _blueInput = Format(value.Blue);
        _alphaInput = Format(value.Alpha);
        OnPropertyChanged(nameof(RedInput));
        OnPropertyChanged(nameof(GreenInput));
        OnPropertyChanged(nameof(BlueInput));
        OnPropertyChanged(nameof(AlphaInput));
        ValidateInput();
    }

    private void ValidateInput()
    {
        SetValidationMessage(
            TryReadValue(out _)
                ? null
                : "Color components must be finite numbers.");
    }

    private void NotifyInputStateChanged()
    {
        OnPropertyChanged(nameof(HasStagedValue));
        OnPropertyChanged(nameof(CanCommit));
        OnPropertyChanged(nameof(CurrentValue));
        OnPropertyChanged(nameof(PreviewBrush));
    }

    private static bool IsFinite(InspectorColorValue value) =>
        float.IsFinite(value.Red) &&
        float.IsFinite(value.Green) &&
        float.IsFinite(value.Blue) &&
        float.IsFinite(value.Alpha);

    private static Color ToAvaloniaColor(InspectorColorValue value) =>
        Color.FromRgb(
            ToColorComponent(value.Red),
            ToColorComponent(value.Green),
            ToColorComponent(value.Blue));

    private static byte ToColorComponent(float value) =>
        !float.IsFinite(value)
            ? (byte)0
            : (byte)Math.Round(
                Math.Clamp(value, 0f, 1f) * byte.MaxValue,
                MidpointRounding.AwayFromZero);

    private static bool TryParseFloat(string input, out float value) =>
        float.TryParse(
            input,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value) && float.IsFinite(value);

    private static string Format(float value) =>
        value.ToString("G9", CultureInfo.InvariantCulture);
}

public readonly record struct InspectorRectValue(
    float X,
    float Y,
    float Width,
    float Height);

public sealed class InspectorRectPropertyRowViewModel
    : InspectorPropertyRowViewModel,
      IInspectorStagedPropertyRow
{
    private readonly Action<InspectorRectValue>? _apply;
    private InspectorRectValue _appliedValue;
    private string _xInput;
    private string _yInput;
    private string _widthInput;
    private string _heightInput;

    public InspectorRectPropertyRowViewModel(
        string label,
        string fieldPath,
        InspectorRectValue value,
        Action<InspectorRectValue>? apply = null,
        string? description = null,
        bool isReadOnly = false)
        : base(label, fieldPath, description, isReadOnly || apply is null)
    {
        _appliedValue = value;
        _xInput = Format(value.X);
        _yInput = Format(value.Y);
        _widthInput = Format(value.Width);
        _heightInput = Format(value.Height);
        _apply = apply;
        ValidateInput();
    }

    public string XInput
    {
        get => _xInput;
        set => SetInput(ref _xInput, value);
    }

    public string YInput
    {
        get => _yInput;
        set => SetInput(ref _yInput, value);
    }

    public string WidthInput
    {
        get => _widthInput;
        set => SetInput(ref _widthInput, value);
    }

    public string HeightInput
    {
        get => _heightInput;
        set => SetInput(ref _heightInput, value);
    }

    public bool HasStagedValue => !EqualsInput(_appliedValue);

    public bool CanCommit =>
        IsEditable && HasStagedValue && !HasValidationError;

    public bool CommitInput()
    {
        if (!CanCommit || _apply is null || !TryReadValue(out InspectorRectValue value))
            return false;
        if (!TryApply(() => _apply(value)))
            return false;

        _appliedValue = value;
        NotifyInputStateChanged();
        return true;
    }

    public void ResetInput()
    {
        _xInput = Format(_appliedValue.X);
        _yInput = Format(_appliedValue.Y);
        _widthInput = Format(_appliedValue.Width);
        _heightInput = Format(_appliedValue.Height);
        OnPropertyChanged(nameof(XInput));
        OnPropertyChanged(nameof(YInput));
        OnPropertyChanged(nameof(WidthInput));
        OnPropertyChanged(nameof(HeightInput));
        ValidateInput();
        NotifyInputStateChanged();
    }

    private void SetInput(
        ref string field,
        string? value,
        [CallerMemberName] string? propertyName = null)
    {
        value ??= string.Empty;
        if (!SetProperty(ref field, value, propertyName))
            return;

        ValidateInput();
        NotifyInputStateChanged();
    }

    private bool TryReadValue(out InspectorRectValue value)
    {
        bool valid = TryParseFloat(XInput, out float x) &
            TryParseFloat(YInput, out float y) &
            TryParseFloat(WidthInput, out float width) &
            TryParseFloat(HeightInput, out float height);
        value = new InspectorRectValue(x, y, width, height);
        return valid;
    }

    private bool EqualsInput(InspectorRectValue value) =>
        string.Equals(XInput, Format(value.X), StringComparison.Ordinal) &&
        string.Equals(YInput, Format(value.Y), StringComparison.Ordinal) &&
        string.Equals(WidthInput, Format(value.Width), StringComparison.Ordinal) &&
        string.Equals(HeightInput, Format(value.Height), StringComparison.Ordinal);

    private void ValidateInput() =>
        SetValidationMessage(
            TryReadValue(out _)
                ? null
                : "Rectangle components must be finite numbers.");

    private void NotifyInputStateChanged()
    {
        OnPropertyChanged(nameof(HasStagedValue));
        OnPropertyChanged(nameof(CanCommit));
    }

    private static bool TryParseFloat(string input, out float value) =>
        float.TryParse(
            input,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value) && float.IsFinite(value);

    private static string Format(float value) =>
        value.ToString("G9", CultureInfo.InvariantCulture);
}

public sealed class InspectorAssetReferencePropertyRowViewModel
    : InspectorPropertyRowViewModel
{
    private readonly Action<InspectorAssetReferencePropertyRowViewModel>?
        _requestSelection;
    private readonly Action<string?>? _apply;
    private string? _assetName;

    public InspectorAssetReferencePropertyRowViewModel(
        string label,
        string fieldPath,
        XAssetType assetType,
        string? assetName,
        Action<string?>? apply = null,
        Action<InspectorAssetReferencePropertyRowViewModel>? requestSelection = null,
        bool isMissing = false,
        string? description = null,
        bool isReadOnly = false)
        : base(
            label,
            fieldPath,
            description,
            isReadOnly || apply is null)
    {
        AssetType = assetType;
        _assetName = assetName;
        _apply = apply;
        _requestSelection = requestSelection;
        IsMissing = isMissing;
        BrowseCommand = new ViewModelCommand(
            RequestSelection,
            () => CanBrowse);
        ClearCommand = new ViewModelCommand(
            Clear,
            () => CanClear);
    }

    public XAssetType AssetType { get; }

    public string AssetTypeText => AssetType.ToString();

    public string? AssetName
    {
        get => _assetName;
        private set
        {
            if (!SetProperty(ref _assetName, value))
                return;

            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(CanClear));
            ClearCommand.RaiseCanExecuteChanged();
        }
    }

    public string DisplayName =>
        string.IsNullOrWhiteSpace(AssetName) ? "None" : AssetName!;

    public bool IsMissing { get; private set; }

    public bool CanBrowse => IsEditable && _requestSelection is not null;

    public bool CanClear => IsEditable && !string.IsNullOrEmpty(AssetName);

    public ViewModelCommand BrowseCommand { get; }

    public ViewModelCommand ClearCommand { get; }

    public bool AcceptSelection(string? assetName, bool isMissing = false)
    {
        if (!IsEditable || _apply is null)
            return false;

        string? normalized = string.IsNullOrWhiteSpace(assetName)
            ? null
            : assetName;
        if (!TryApply(() => _apply(normalized)))
            return false;

        AssetName = normalized;
        IsMissing = isMissing;
        OnPropertyChanged(nameof(IsMissing));
        return true;
    }

    private void RequestSelection() => _requestSelection?.Invoke(this);

    private void Clear() => _ = AcceptSelection(null);

    protected override void OnInteractionStateChanged()
    {
        OnPropertyChanged(nameof(CanBrowse));
        OnPropertyChanged(nameof(CanClear));
        BrowseCommand.RaiseCanExecuteChanged();
        ClearCommand.RaiseCanExecuteChanged();
    }
}

/// <summary>
/// A compact, generic inspector row that presents a read-only summary with
/// one optional action. The workbench supplies the visual treatment; editors
/// supply only the label, summary, and action callback.
/// </summary>
public sealed class InspectorActionPropertyRowViewModel
    : InspectorPropertyRowViewModel
{
    private readonly Action? _invoke;

    public InspectorActionPropertyRowViewModel(
        string label,
        string fieldPath,
        string? value,
        Action? invoke = null,
        string? actionToolTip = null,
        string? actionAutomationName = null,
        string? description = null,
        bool isReadOnly = false)
        : base(
            label,
            fieldPath,
            description,
            isReadOnly || invoke is null)
    {
        Value = value ?? "—";
        _invoke = invoke;
        ActionToolTip = string.IsNullOrWhiteSpace(actionToolTip)
            ? $"Edit {label}"
            : actionToolTip;
        ActionAutomationName = string.IsNullOrWhiteSpace(
            actionAutomationName)
            ? ActionToolTip
            : actionAutomationName;
        InvokeCommand = new ViewModelCommand(Invoke, () => CanInvoke);
    }

    public string Value { get; }

    public string ActionToolTip { get; }

    public string ActionAutomationName { get; }

    public bool HasAction => _invoke is not null;

    public bool CanInvoke => IsEditable && HasAction;

    public ViewModelCommand InvokeCommand { get; }

    private void Invoke()
    {
        if (_invoke is null || !CanInvoke)
            return;

        _ = TryApply(_invoke);
    }

    protected override void OnInteractionStateChanged()
    {
        OnPropertyChanged(nameof(CanInvoke));
        InvokeCommand.RaiseCanExecuteChanged();
    }
}

public sealed class InspectorReadOnlyPropertyRowViewModel
    : InspectorPropertyRowViewModel
{
    public InspectorReadOnlyPropertyRowViewModel(
        string label,
        string fieldPath,
        string? value,
        string? description = null)
        : base(label, fieldPath, description, isReadOnly: true)
    {
        Value = value ?? "—";
    }

    public string Value { get; }
}
