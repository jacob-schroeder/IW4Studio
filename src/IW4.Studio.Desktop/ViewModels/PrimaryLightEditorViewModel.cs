using Avalonia.Media;
using IW4.Studio.MapEditor.Editing.Commands;
using IW4.Studio.MapEditor.Editing.Objects;

namespace IW4.Studio.Desktop.ViewModels;

/// <summary>
/// Typed inspector adapter for the independently authored color, exponent,
/// and bounded type-2 inner-cone falloff fields of one existing ComMap
/// primary light.
/// </summary>
public sealed class PrimaryLightEditorViewModel : ObservableObject
{
    private readonly EditorPrimaryLight _light;
    private readonly Action<IMapEditCommand> _execute;

    public PrimaryLightEditorViewModel(
        EditorPrimaryLight light,
        Action<IMapEditCommand> execute)
    {
        _light = light ?? throw new ArgumentNullException(nameof(light));
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
    }

    public decimal Red
    {
        get => ToDecimal(_light.Color.Value.X);
        set => SetComponent(PrimaryLightColorComponent.Red, value);
    }

    public decimal Green
    {
        get => ToDecimal(_light.Color.Value.Y);
        set => SetComponent(PrimaryLightColorComponent.Green, value);
    }

    public decimal Blue
    {
        get => ToDecimal(_light.Color.Value.Z);
        set => SetComponent(PrimaryLightColorComponent.Blue, value);
    }

    public decimal Exponent
    {
        get => _light.Exponent.Value;
        set
        {
            if (value != decimal.Truncate(value) ||
                value < byte.MinValue ||
                value > byte.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Primary-light exponent must be an integer from 0 through 255.");
            }

            byte exponent = decimal.ToByte(value);
            if (_light.Exponent.Value == exponent)
                return;

            _execute(new SetPrimaryLightExponentCommand(
                _light.Id,
                exponent));
        }
    }

    public decimal CosHalfFovInner
    {
        get => ToDecimal(_light.CosHalfFovInner.Value);
        set
        {
            if (!CanEditSpotFalloff)
            {
                throw new InvalidOperationException(
                    "Inner-cone falloff is editable only for an exact " +
                    "type-2 spotlight satisfying 0 < outer < inner <= 1.");
            }

            float inner = checked((float)value);
            float outer = _light.CosHalfFovOuter.Value;
            if (!float.IsFinite(inner) ||
                inner <= outer ||
                inner > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "The inner-cone cosine must be finite and satisfy " +
                    "outer < inner <= 1.");
            }
            if (SameBits(_light.CosHalfFovInner.Value, inner))
                return;

            _execute(new SetPrimaryLightCosHalfFovInnerCommand(
                _light.Id,
                inner));
        }
    }

    public bool CanEditSpotFalloff => IsEditableSpotlight(_light);

    public string SpotFalloffStatusText =>
        _light.LightType.Value != 2
            ? "Read-only: inner-cone authoring is limited to type-2 spotlights."
            : CanEditSpotFalloff
                ? $"Outer cone remains immutable at " +
                  $"{_light.CosHalfFovOuter.Value:0.######}; set inner above it."
                : "Read-only: imported cone values violate " +
                  "0 < outer < inner <= 1.";

    public string SourceOrdinalText =>
        $"ComMap primary light #{_light.SourceOrdinal.Value}";

    public string ColorProvenanceText =>
        _light.Color.Provenance.ToString();

    public string ExponentProvenanceText =>
        _light.Exponent.Provenance.ToString();

    public string SpotFalloffProvenanceText =>
        _light.CosHalfFovInner.Provenance.ToString();

    public IBrush PreviewBrush
    {
        get
        {
            MapVector3 color = _light.Color.Value;
            return new SolidColorBrush(Color.FromRgb(
                ToDisplayByte(color.X),
                ToDisplayByte(color.Y),
                ToDisplayByte(color.Z)));
        }
    }

    internal void Refresh()
    {
        OnPropertyChanged(nameof(Red));
        OnPropertyChanged(nameof(Green));
        OnPropertyChanged(nameof(Blue));
        OnPropertyChanged(nameof(Exponent));
        OnPropertyChanged(nameof(CosHalfFovInner));
        OnPropertyChanged(nameof(CanEditSpotFalloff));
        OnPropertyChanged(nameof(SpotFalloffStatusText));
        OnPropertyChanged(nameof(PreviewBrush));
        OnPropertyChanged(nameof(ColorProvenanceText));
        OnPropertyChanged(nameof(ExponentProvenanceText));
        OnPropertyChanged(nameof(SpotFalloffProvenanceText));
    }

    private void SetComponent(
        PrimaryLightColorComponent component,
        decimal value)
    {
        float scalar = checked((float)value);
        if (!float.IsFinite(scalar) || scalar < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Primary-light color components must be finite and nonnegative.");
        }

        MapVector3 current = _light.Color.Value;
        float previous = component switch
        {
            PrimaryLightColorComponent.Red => current.X,
            PrimaryLightColorComponent.Green => current.Y,
            PrimaryLightColorComponent.Blue => current.Z,
            _ => throw new ArgumentOutOfRangeException(nameof(component))
        };
        if (previous == scalar)
            return;

        _execute(new SetPrimaryLightColorComponentCommand(
            _light.Id,
            component,
            scalar));
    }

    private static decimal ToDecimal(float value) =>
        Convert.ToDecimal(value);

    private static bool IsEditableSpotlight(EditorPrimaryLight light)
    {
        float outer = light.CosHalfFovOuter.Value;
        float importedInner = light.ImportedCosHalfFovInner.Value;
        float currentInner = light.CosHalfFovInner.Value;
        return light.LightType.Value == 2 &&
               float.IsFinite(outer) &&
               float.IsFinite(importedInner) &&
               float.IsFinite(currentInner) &&
               outer > 0f &&
               outer < importedInner &&
               importedInner <= 1f &&
               outer < currentInner &&
               currentInner <= 1f;
    }

    private static bool SameBits(float left, float right) =>
        BitConverter.SingleToInt32Bits(left) ==
        BitConverter.SingleToInt32Bits(right);

    private static byte ToDisplayByte(float value) =>
        checked((byte)Math.Clamp(
            (int)MathF.Round(Math.Clamp(value, 0f, 1f) * byte.MaxValue),
            byte.MinValue,
            byte.MaxValue));
}
