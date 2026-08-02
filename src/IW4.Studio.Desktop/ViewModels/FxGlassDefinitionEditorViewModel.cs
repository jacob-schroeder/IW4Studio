using Avalonia.Media;
using IW4.Studio.MapEditor.Editing.Commands;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.Objects;

namespace IW4.Studio.Desktop.ViewModels;

/// <summary>
/// Typed inspector for the bounded, independently proven properties of one
/// existing FxGlassDef. Initial pieces remain read-only HalfThickness views
/// joined to this definition through DefIndex; packed RGBA is definition-only.
/// </summary>
public sealed class FxGlassDefinitionEditorViewModel
    : ObservableObject
{
    private readonly EditorMapDocument _document;
    private readonly EditorGlassObject _definition;
    private readonly Action<IMapEditCommand> _execute;

    public FxGlassDefinitionEditorViewModel(
        EditorMapDocument document,
        EditorGlassObject definition,
        Action<IMapEditCommand> execute)
    {
        _document =
            document ?? throw new ArgumentNullException(nameof(document));
        _definition =
            definition ?? throw new ArgumentNullException(nameof(definition));
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        if (!_document.Glass.Contains(_definition) ||
            _definition.Representation != GlassRepresentation.FxDefinition ||
            _definition.HalfThickness.Value is null ||
            _definition.Color.Value is null)
        {
            throw new ArgumentException(
                "The glass definition editor requires an authoritative " +
                "FxGlassDef projection owned by the document.",
                nameof(definition));
        }
    }

    public decimal HalfThickness
    {
        get => Convert.ToDecimal(
            _definition.HalfThickness.Value ??
            throw new InvalidOperationException(
                "The FX glass definition lost its HalfThickness value."));
        set
        {
            float scalar = checked((float)value);
            if (!float.IsFinite(scalar) || scalar <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "FX glass definition half thickness must be finite and " +
                    "strictly positive.");
            }

            float current =
                _definition.HalfThickness.Value ??
                throw new InvalidOperationException(
                    "The FX glass definition lost its HalfThickness value.");
            if (BitConverter.SingleToInt32Bits(current) ==
                BitConverter.SingleToInt32Bits(scalar))
            {
                return;
            }

            _execute(
                new SetFxGlassDefinitionHalfThicknessCommand(
                    _document,
                    _definition.Id,
                    scalar));
        }
    }

    public decimal Red
    {
        get => Component(_definition.Color.Value, shift: 24);
        set => SetColorComponent(shift: 24, value);
    }

    public decimal Green
    {
        get => Component(_definition.Color.Value, shift: 16);
        set => SetColorComponent(shift: 16, value);
    }

    public decimal Blue
    {
        get => Component(_definition.Color.Value, shift: 8);
        set => SetColorComponent(shift: 8, value);
    }

    public decimal Alpha
    {
        get => Component(_definition.Color.Value, shift: 0);
        set => SetColorComponent(shift: 0, value);
    }

    public IBrush PreviewBrush
    {
        get
        {
            uint color = RequireColor();
            return new SolidColorBrush(Color.FromArgb(
                ComponentByte(color, shift: 0),
                ComponentByte(color, shift: 24),
                ComponentByte(color, shift: 16),
                ComponentByte(color, shift: 8)));
        }
    }

    public string SourceOrdinalText =>
        $"FxMap glass definition #{_definition.SourceOrdinal.Value}";

    public string DependencyText
    {
        get
        {
            int ordinal = _definition.SourceOrdinal.Value;
            int count = _document.Glass.Count(value =>
                value.Representation ==
                    GlassRepresentation.FxInitialPiece &&
                value.DefinitionIndex.Value == ordinal);
            return $"{count:N0} initial piece" +
                   (count == 1 ? string.Empty : "s") +
                   " derive half thickness through DefIndex; color remains " +
                   "definition-scoped";
        }
    }

    public string HalfThicknessProvenanceText =>
        _definition.HalfThickness.Provenance.ToString();

    public string ColorProvenanceText =>
        _definition.Color.Provenance.ToString();

    public string ClassificationText => "PATCH-SAVEABLE FXMAP";

    internal void Refresh()
    {
        OnPropertyChanged(nameof(HalfThickness));
        OnPropertyChanged(nameof(Red));
        OnPropertyChanged(nameof(Green));
        OnPropertyChanged(nameof(Blue));
        OnPropertyChanged(nameof(Alpha));
        OnPropertyChanged(nameof(PreviewBrush));
        OnPropertyChanged(nameof(DependencyText));
        OnPropertyChanged(nameof(HalfThicknessProvenanceText));
        OnPropertyChanged(nameof(ColorProvenanceText));
    }

    private void SetColorComponent(int shift, decimal value)
    {
        if (value < byte.MinValue ||
            value > byte.MaxValue ||
            decimal.Truncate(value) != value)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "FX glass color components must be whole bytes from 0 to 255.");
        }

        uint current = RequireColor();
        uint mask = ((uint)byte.MaxValue) << shift;
        uint color =
            (current & ~mask) |
            (checked((uint)value) << shift);
        if (current == color)
            return;

        _execute(new SetFxGlassDefinitionColorCommand(
            _document,
            _definition.Id,
            color));
    }

    private uint RequireColor() =>
        _definition.Color.Value ??
        throw new InvalidOperationException(
            "The FX glass definition lost its packed RGBA value.");

    private static decimal Component(uint? color, int shift) =>
        ComponentByte(
            color ??
            throw new InvalidOperationException(
                "The FX glass definition lost its packed RGBA value."),
            shift);

    private static byte ComponentByte(uint color, int shift) =>
        (byte)((color >> shift) & byte.MaxValue);
}
