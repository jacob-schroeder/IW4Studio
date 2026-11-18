using System.Globalization;
using IW4.Game.Zone;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.ViewModels;

public sealed class VehicleEditorField : ObservableObject
{
    private readonly VehicleEditorViewModel _owner;
    private string _value, _appliedValue, _savedValue;
    private string? _error;

    internal VehicleEditorField(VehicleEditorViewModel owner, VehicleField field, VehicleDraft applied, VehicleDraft saved)
    {
        _owner = owner;
        Definition = field;
        _value = _appliedValue = Format(applied.GetValue(field));
        _savedValue = Format(saved.GetValue(field));
    }

    internal VehicleField Definition { get; }
    public string Key => Definition.Path;
    public string Label => Definition.Label;
    public string Group => Key switch
    {
        "Phys.PhysPreset" => "Physics preset",
        "CompassFriendlyIcon" or "CompassEnemyIcon" => "Compass icons",
        "TurretWeapon" or "TurretSpinSound" or "TurretStopSound" => "Turret",
        _ when Key.StartsWith("EngineSounds.", StringComparison.Ordinal) && ReferenceType.HasValue => "Engine sounds",
        _ when Key.StartsWith("SuspensionSounds.", StringComparison.Ordinal) && ReferenceType.HasValue => "Suspension sounds",
        _ when Key.StartsWith("SurfaceSoundFields[", StringComparison.Ordinal) => "Surface sounds",
        _ when ReferenceType == XAssetType.Sound => "Driving sounds",
        _ => Definition.Section
    };
    public bool IsBoolean => Definition.Kind == VehicleFieldKind.Boolean;
    public IReadOnlyList<string> Choices => Definition.Options;
    public XAssetType? ReferenceType => Definition.ReferenceType;
    public string SavedValue => _savedValue;
    public bool IsChanged => Value != _appliedValue &&
        (!TryParse(out var typed) || Format(typed) != _appliedValue);
    public string? Error => _error;

    public string Value
    {
        get => _value;
        set
        {
            if (!_owner.IsEditable || !SetProperty(ref _value, value ?? string.Empty)) return;
            SetError(null);
            if (IsChanged && !TryParse(out _))
                SetError(Definition.Kind == VehicleFieldKind.Integer ? "Enter a whole number." :
                    Definition.Kind == VehicleFieldKind.Float ? "Enter a finite number." : "Choose a valid value.");
            OnPropertyChanged(nameof(IsChanged));
            _owner.FieldEdited(this);
        }
    }

    internal void SetError(string? error) => SetProperty(ref _error, error, nameof(Error));

    internal bool TryParse(out object? result)
    {
        result = Value;
        switch (Definition.Kind)
        {
            case VehicleFieldKind.Boolean:
                if (!bool.TryParse(Value, out var flag)) return false;
                result = flag;
                break;
            case VehicleFieldKind.Integer:
                if (!int.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) return false;
                result = integer;
                break;
            case VehicleFieldKind.Float:
                if (!float.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !float.IsFinite(number)) return false;
                result = number;
                break;
            case VehicleFieldKind.Choice:
                return Choices.Contains(Value);
            default:
                result = string.IsNullOrEmpty(Value) ? null : Value;
                break;
        }
        return true;
    }

    internal void Reset(VehicleDraft applied)
    {
        _appliedValue = _value = Format(applied.GetValue(Definition));
        SetError(null);
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(IsChanged));
    }

    internal void ReadSaved(VehicleDraft saved) =>
        SetProperty(ref _savedValue, Format(saved.GetValue(Definition)), nameof(SavedValue));

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        float number => number.ToString("R", CultureInfo.InvariantCulture),
        IFormattable printable => printable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };
}
