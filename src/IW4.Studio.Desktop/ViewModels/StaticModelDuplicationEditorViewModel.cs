using IW4.Studio.MapEditor.Compilation.Bundles;
using IW4.Studio.MapEditor.Compilation.StaticModels;
using IW4.Studio.MapEditor.Editing.Commands;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;

namespace IW4.Studio.Desktop.ViewModels;

/// <summary>
/// Precision inspector for the constrained Phase 6 static-model addition
/// boundary. It drafts one destination and creates a paired authored
/// Gfx/collision object only after every compiled invariant passes.
/// </summary>
public sealed class StaticModelDuplicationEditorViewModel : ObservableObject
{
    private readonly EditorMapDocument _document;
    private readonly CompiledMapBundle _bundle;
    private readonly EditorStaticModel _renderTemplate;
    private readonly StaticModelCorrespondenceCatalog _catalog;
    private readonly StaticModelCompilationRelationship? _relationship;
    private readonly Func<bool> _areEditorsEnabled;
    private readonly Action<IMapEditCommand> _execute;
    private readonly Action<MapObjectId?> _selectObject;
    private decimal _x;
    private decimal _y;
    private decimal _z;
    private StaticModelDuplicationEligibilityAssessment? _assessment;

    public StaticModelDuplicationEditorViewModel(
        EditorMapDocument document,
        CompiledMapBundle bundle,
        EditorStaticModel renderTemplate,
        StaticModelCorrespondenceCatalog catalog,
        Func<bool> areEditorsEnabled,
        Action<IMapEditCommand> execute,
        Action<MapObjectId?> selectObject)
    {
        _document = document ??
            throw new ArgumentNullException(nameof(document));
        _bundle = bundle ??
            throw new ArgumentNullException(nameof(bundle));
        _renderTemplate = renderTemplate ??
            throw new ArgumentNullException(nameof(renderTemplate));
        _catalog = catalog ??
            throw new ArgumentNullException(nameof(catalog));
        _areEditorsEnabled = areEditorsEnabled ??
            throw new ArgumentNullException(nameof(areEditorsEnabled));
        _execute = execute ??
            throw new ArgumentNullException(nameof(execute));
        _selectObject = selectObject ??
            throw new ArgumentNullException(nameof(selectObject));
        if (!renderTemplate.IsImported ||
            renderTemplate.Representation !=
                StaticModelRepresentation.Render)
        {
            throw new ArgumentException(
                "Static-model duplication starts from an imported render " +
                "template.",
                nameof(renderTemplate));
        }

        _catalog.TryGetByRenderObjectId(
            renderTemplate.Id,
            out _relationship);
        MapVector3 source = renderTemplate.Origin.Value;
        _x = ToDecimal(source.X + 0.125f);
        _y = ToDecimal(source.Y);
        _z = ToDecimal(source.Z);
        DuplicatePairCommand = new ViewModelCommand(
            DuplicatePair,
            () => CanDuplicatePair);
        RefreshAssessment();
    }

    public ViewModelCommand DuplicatePairCommand { get; }

    public decimal X
    {
        get => _x;
        set => SetCoordinate(ref _x, value);
    }

    public decimal Y
    {
        get => _y;
        set => SetCoordinate(ref _y, value);
    }

    public decimal Z
    {
        get => _z;
        set => SetCoordinate(ref _z, value);
    }

    public bool IsEligible =>
        _assessment?.IsPatchEligible == true;

    public bool HasAuthoredPair =>
        _document.StaticModels.Any(value => !value.IsImported);

    public bool CanDuplicatePair =>
        _areEditorsEnabled() &&
        !HasAuthoredPair &&
        IsEligible;

    public string StatusText =>
        HasAuthoredPair
            ? "One Pair Pending"
            : IsEligible
                ? "Patch Saveable"
                : "Blocked";

    public string PairText =>
        _relationship is null
            ? "No ExactBundleUnique Gfx/Col relationship."
            : $"Gfx #{_relationship.GfxSourceOrdinal} + " +
              $"{SplitWords(_relationship.CollisionAssetKind.ToString())} " +
              $"#{_relationship.ClipSourceOrdinal}";

    public string DestinationText =>
        _assessment is
        {
            Gfx.NewOrdinal: var gfxOrdinal,
            Collision.NewOrdinal: var clipOrdinal
        }
            ? $"Projected Gfx #{gfxOrdinal} + collision #{clipOrdinal}"
            : "No compiled destination ordinals authorized.";

    public string EvidenceText =>
        _assessment?.Evidence ??
        "Duplication requires one exact imported render/collision pair.";

    public string AvailabilityText =>
        HasAuthoredPair
            ? "Undo the pending duplicate or Save As and reopen before " +
              "creating another pair."
            : IsEligible
                ? "Creates one authored pair and rebuilds Gfx AABB/shadow " +
                  "membership plus Clip child ranges. The loaded baseline " +
                  "viewport cannot synthesize the new draw; Save As and " +
                  "reopen materializes it."
                : "Choose a finite destination that preserves the imported " +
                  "cell, leaf, lighting, probe, and shadow assignments. " +
                  "Definition-owning or unmatched models remain blocked.";

    internal void Refresh()
    {
        RefreshAssessment();
        OnPropertyChanged(nameof(IsEligible));
        OnPropertyChanged(nameof(HasAuthoredPair));
        OnPropertyChanged(nameof(CanDuplicatePair));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(PairText));
        OnPropertyChanged(nameof(DestinationText));
        OnPropertyChanged(nameof(EvidenceText));
        OnPropertyChanged(nameof(AvailabilityText));
        DuplicatePairCommand.RaiseCanExecuteChanged();
    }

    private void SetCoordinate(ref decimal field, decimal value)
    {
        float scalar = checked((float)value);
        if (!float.IsFinite(scalar))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Static-model duplication coordinates must be finite.");
        }
        if (!SetProperty(ref field, value))
            return;

        Refresh();
    }

    private void RefreshAssessment()
    {
        _assessment = _relationship is null
            ? null
            : StaticModelDuplicationEligibilityEvaluator.Evaluate(
                _bundle,
                _document,
                _catalog,
                _relationship,
                new MapVector3(
                    checked((float)_x),
                    checked((float)_y),
                    checked((float)_z)));
    }

    private void DuplicatePair()
    {
        if (!CanDuplicatePair ||
            _assessment is null)
        {
            return;
        }

        var command =
            new DuplicateCompiledStaticModelCommand(_assessment);
        _execute(command);
        _selectObject(command.RenderObjectId);
    }

    private static decimal ToDecimal(float value) =>
        Convert.ToDecimal(value);

    private static string SplitWords(string value) =>
        string.Concat(value.Select((character, index) =>
            index > 0 && char.IsUpper(character)
                ? $" {character}"
                : character.ToString()));
}
