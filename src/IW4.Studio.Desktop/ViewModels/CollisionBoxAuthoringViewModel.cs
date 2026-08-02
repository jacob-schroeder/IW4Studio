using IW4.Studio.MapEditor.Compilation.Collision;
using IW4.Studio.MapEditor.Editing.Collision;
using IW4.Studio.MapEditor.Editing.Commands;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;

namespace IW4.Studio.Desktop.ViewModels;

/// <summary>
/// Bounded M2/M3 primitive tool. Placement comes from a selected world-space
/// object, material semantics come from one explicitly selected imported
/// ClipMaterial row, and creation emits one canonical source command.
/// </summary>
public sealed class CollisionBoxAuthoringViewModel : ObservableObject
{
    private readonly EditorMapDocument _document;
    private readonly Func<MapBounds?> _getAnchorBounds;
    private readonly Func<bool> _areEditorsEnabled;
    private readonly Action<IMapEditCommand> _execute;
    private readonly Action<MapObjectId?> _selectObject;
    private CollisionAuthoringMaterialOption? _selectedMaterial;
    private decimal _sizeX = 64m;
    private decimal _sizeY = 64m;
    private decimal _sizeZ = 64m;

    public CollisionBoxAuthoringViewModel(
        EditorMapDocument document,
        CollisionAuthoringMaterialCatalog materials,
        Func<MapBounds?> getAnchorBounds,
        Func<bool> areEditorsEnabled,
        Action<IMapEditCommand> execute,
        Action<MapObjectId?> selectObject)
    {
        _document =
            document ?? throw new ArgumentNullException(nameof(document));
        ArgumentNullException.ThrowIfNull(materials);
        _getAnchorBounds = getAnchorBounds ??
            throw new ArgumentNullException(nameof(getAnchorBounds));
        _areEditorsEnabled = areEditorsEnabled ??
            throw new ArgumentNullException(nameof(areEditorsEnabled));
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _selectObject = selectObject ??
            throw new ArgumentNullException(nameof(selectObject));
        Materials = materials.Options;
        AddBoxCommand = new ViewModelCommand(
            AddBox,
            () => CanAddBox);
    }

    public IReadOnlyList<CollisionAuthoringMaterialOption> Materials
    {
        get;
    }

    public ViewModelCommand AddBoxCommand { get; }

    public CollisionAuthoringMaterialOption? SelectedMaterial
    {
        get => _selectedMaterial;
        set
        {
            if (SetProperty(ref _selectedMaterial, value))
                Refresh();
        }
    }

    public decimal SizeX
    {
        get => _sizeX;
        set => SetSize(ref _sizeX, value, nameof(SizeX));
    }

    public decimal SizeY
    {
        get => _sizeY;
        set => SetSize(ref _sizeY, value, nameof(SizeY));
    }

    public decimal SizeZ
    {
        get => _sizeZ;
        set => SetSize(ref _sizeZ, value, nameof(SizeZ));
    }

    public bool HasMaterials => Materials.Count != 0;

    public bool HasAnchor => TryGetAnchor(out _);

    public bool CanAddBox =>
        _areEditorsEnabled() &&
        SelectedMaterial is not null &&
        HasValidSize &&
        TryGetAnchor(out _);

    public string PlacementText =>
        TryGetAnchor(out MapBounds bounds)
            ? $"New box center: {bounds.MidPoint}"
            : "Select an object with finite world bounds to place a box.";

    public string AvailabilityText =>
        !HasMaterials
            ? "No ColMapMp material catalog is available."
            : SelectedMaterial is null
                ? "Choose an exact imported collision material."
                : !HasValidSize
                    ? "Box dimensions must be finite and greater than zero."
                    : PlacementText;

    public void Refresh()
    {
        OnPropertyChanged(nameof(HasAnchor));
        OnPropertyChanged(nameof(CanAddBox));
        OnPropertyChanged(nameof(PlacementText));
        OnPropertyChanged(nameof(AvailabilityText));
        AddBoxCommand.RaiseCanExecuteChanged();
    }

    private bool HasValidSize =>
        IsValidSize(SizeX) &&
        IsValidSize(SizeY) &&
        IsValidSize(SizeZ);

    private void SetSize(
        ref decimal field,
        decimal value,
        string propertyName)
    {
        if (field == value)
            return;

        field = value;
        OnPropertyChanged(propertyName);
        Refresh();
    }

    private void AddBox()
    {
        if (!CanAddBox ||
            SelectedMaterial is not { } material ||
            !TryGetAnchor(out MapBounds anchor))
        {
            return;
        }

        var bounds = new MapBounds(
            anchor.MidPoint,
            new MapVector3(
                ToPositiveHalfSize(SizeX),
                ToPositiveHalfSize(SizeY),
                ToPositiveHalfSize(SizeZ)));
        AuthoredConvexBrushCollisionSource source =
            AuthoredCollisionPrimitiveFactory
                .CreateStandaloneAxisAlignedBox(
                    new MapObjectId(Guid.NewGuid()),
                    bounds,
                    material.Material);
        var command = new AddAuthoredCollisionSourceCommand(
            _document,
            source);
        _execute(command);
        _selectObject(command.Authored.Id);
    }

    private bool TryGetAnchor(out MapBounds bounds)
    {
        MapBounds? candidate = _getAnchorBounds();
        if (candidate is not { IsFinite: true } value)
        {
            bounds = default;
            return false;
        }

        bounds = value;
        return true;
    }

    private static bool IsValidSize(decimal value)
    {
        if (value <= 0m)
            return false;

        try
        {
            float scalar = checked((float)value);
            return float.IsFinite(scalar) && scalar > 0f;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static float ToPositiveHalfSize(decimal size)
    {
        float value = checked((float)(size / 2m));
        if (!float.IsFinite(value) || !(value > 0f))
        {
            throw new InvalidOperationException(
                "Collision-box dimensions must produce finite positive " +
                "half sizes.");
        }

        return value;
    }
}
