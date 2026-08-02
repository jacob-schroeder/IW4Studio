using IW4.Studio.MapEditor.Compilation.Bundles;
using IW4.Studio.MapEditor.Compilation.StaticModels;
using IW4.Studio.MapEditor.Editing.Commands;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.Objects;

namespace IW4.Studio.Desktop.ViewModels;

/// <summary>
/// Inspector for the Phase 6 ExactBundleUnique static-model removal boundary.
/// It never treats an unmatched or merely similar Gfx/Col row as authority.
/// </summary>
public sealed class StaticModelRemovalEditorViewModel : ObservableObject
{
    private readonly EditorMapDocument _document;
    private readonly EditorStaticModel _renderModel;
    private readonly Func<bool> _areEditorsEnabled;
    private readonly Action<IMapEditCommand> _execute;
    private readonly StaticModelCompilationRelationship? _relationship;
    private readonly StaticModelRemovalEligibilityAssessment? _eligibility;

    public StaticModelRemovalEditorViewModel(
        EditorMapDocument document,
        CompiledMapBundle bundle,
        EditorStaticModel renderModel,
        StaticModelCorrespondenceCatalog catalog,
        Func<bool> areEditorsEnabled,
        Action<IMapEditCommand> execute)
    {
        _document = document ??
            throw new ArgumentNullException(nameof(document));
        ArgumentNullException.ThrowIfNull(bundle);
        _renderModel = renderModel ??
            throw new ArgumentNullException(nameof(renderModel));
        ArgumentNullException.ThrowIfNull(catalog);
        _areEditorsEnabled = areEditorsEnabled ??
            throw new ArgumentNullException(nameof(areEditorsEnabled));
        _execute = execute ??
            throw new ArgumentNullException(nameof(execute));
        if (!renderModel.IsImported ||
            renderModel.Representation !=
            StaticModelRepresentation.Render)
        {
            throw new ArgumentException(
                "Compiled static-model removal is initiated from an imported " +
                "render representation.",
                nameof(renderModel));
        }

        catalog.TryGetByRenderObjectId(
            renderModel.Id,
            out _relationship);
        if (_relationship is not null)
        {
            _eligibility =
                StaticModelRemovalEligibilityEvaluator.Evaluate(
                    bundle,
                    catalog,
                    _relationship);
        }
        RemoveCompiledPairCommand = new ViewModelCommand(
            RemoveCompiledPair,
            () => CanRemoveCompiledPair);
    }

    public ViewModelCommand RemoveCompiledPairCommand { get; }

    public bool IsEligible =>
        _eligibility?.IsPatchEligible == true;

    public string StatusText =>
        _relationship is null
            ? "No Exact Pair"
            : IsEligible
                ? "Patch Saveable"
                : "Blocked";

    public string PairText =>
        _relationship is null
            ? "No ExactBundleUnique Gfx/Col relationship."
            : $"Gfx #{_relationship.GfxSourceOrdinal} + " +
              $"{SplitWords(_relationship.CollisionAssetKind.ToString())} " +
              $"#{_relationship.ClipSourceOrdinal}";

    public string EvidenceText =>
        _eligibility?.Evidence ??
        "Removal is unavailable without a mutual, exact-bundle Gfx/Col " +
        "relationship.";

    public string AvailabilityText
    {
        get
        {
            if (_relationship is null || _eligibility is null)
            {
                return "Unavailable: equal names, origins, proximity, or " +
                    "ordinals are not accepted as compiled identity.";
            }
            if (!_eligibility.IsPatchEligible)
            {
                return "Unavailable: dependency ownership, Gfx AABB/shadow, " +
                    "or Clip tree cardinality cannot be rebuilt safely.";
            }
            if (_renderModel.CompiledDisposition ==
                StaticModelCompiledDisposition.Removed)
            {
                return "The pair is removed from the compiled projection. " +
                    "Undo restores both semantic rows.";
            }
            if (!TryGetBaselineCollision(out _))
            {
                return "Unavailable: the exact collision counterpart is no " +
                    "longer baseline-present.";
            }
            if (_renderModel.Transform !=
                _renderModel.ImportedTransform)
            {
                return "Unavailable: undo the static-model translation " +
                    "before changing cardinality.";
            }
            if (!_areEditorsEnabled())
            {
                return "Unavailable while compiled Save As validation or " +
                    "reopen-required state locks editing.";
            }
            return "Removes both compiled rows and deterministically rebuilds " +
                "Gfx AABB/shadow ordinals and Clip tree child ranges.";
        }
    }

    public bool CanRemoveCompiledPair =>
        _areEditorsEnabled() &&
        _eligibility?.IsPatchEligible == true &&
        _renderModel.CompiledDisposition ==
            StaticModelCompiledDisposition.BaselinePresent &&
        _renderModel.Transform == _renderModel.ImportedTransform &&
        TryGetBaselineCollision(out _);

    internal void Refresh()
    {
        OnPropertyChanged(nameof(IsEligible));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(PairText));
        OnPropertyChanged(nameof(EvidenceText));
        OnPropertyChanged(nameof(AvailabilityText));
        OnPropertyChanged(nameof(CanRemoveCompiledPair));
        RemoveCompiledPairCommand.RaiseCanExecuteChanged();
    }

    private void RemoveCompiledPair()
    {
        if (!CanRemoveCompiledPair ||
            _eligibility is null)
        {
            return;
        }
        _execute(new RemoveCompiledStaticModelCommand(_eligibility));
    }

    private bool TryGetBaselineCollision(
        out EditorStaticModel? collision)
    {
        collision = null;
        if (_relationship is null ||
            !_document.TryGetObject(
                _relationship.CollisionObjectId,
                out EditorMapObject? candidate) ||
            candidate is not EditorStaticModel
            {
                Representation: StaticModelRepresentation.Collision,
                CompiledDisposition:
                    StaticModelCompiledDisposition.BaselinePresent
            } typed ||
            typed.Transform != typed.ImportedTransform)
        {
            return false;
        }

        collision = typed;
        return true;
    }

    private static string SplitWords(string value) =>
        string.Concat(value.Select((character, index) =>
            index > 0 && char.IsUpper(character)
                ? $" {character}"
                : character.ToString()));
}
