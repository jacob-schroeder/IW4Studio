using IW4.Studio.MapEditor.Compilation.StaticModels;
using IW4.Studio.MapEditor.Editing.Commands;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.Objects;

namespace IW4.Studio.Desktop.ViewModels;

/// <summary>
/// Inspector for the conservative compiled-presence operation. It reports
/// exact-bundle correspondence independently from editor-only visibility and
/// from the still-blocked static-model translation workflow.
/// </summary>
public sealed class StaticModelSuppressionEditorViewModel : ObservableObject
{
    private readonly EditorMapDocument _document;
    private readonly EditorStaticModel _renderModel;
    private readonly Func<bool> _areEditorsEnabled;
    private readonly Action<IMapEditCommand> _execute;
    private readonly StaticModelCompilationRelationship? _relationship;
    private readonly StaticModelCorrespondenceAssessment? _assessment;

    public StaticModelSuppressionEditorViewModel(
        EditorMapDocument document,
        EditorStaticModel renderModel,
        StaticModelCorrespondenceCatalog catalog,
        Func<bool> areEditorsEnabled,
        Action<IMapEditCommand> execute)
    {
        _document = document ??
            throw new ArgumentNullException(nameof(document));
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
                "Compiled static-model suppression is initiated from the " +
                "imported render representation of an exact render/collision " +
                "pair.",
                nameof(renderModel));
        }
        if (catalog.DocumentId != document.Id)
        {
            throw new ArgumentException(
                "The correspondence catalog belongs to another map document.",
                nameof(catalog));
        }

        catalog.TryGetByRenderObjectId(
            renderModel.Id,
            out _relationship);
        catalog.TryGetAssessment(
            renderModel.Id,
            out _assessment);
        SuppressCompiledPairCommand = new ViewModelCommand(
            SuppressCompiledPair,
            () => CanSuppressCompiledPair);
    }

    public ViewModelCommand SuppressCompiledPairCommand { get; }

    public string CorrespondenceStatusText =>
        _assessment is null
            ? "Not Assessed"
            : SplitWords(_assessment.Status.ToString());

    public string CorrespondenceHeadingText =>
        _relationship is null
            ? "No exact compiled pair"
            : "Exact-bundle render/collision pair";

    public string CorrespondenceEvidenceText =>
        _relationship?.Evidence ??
        _assessment?.Evidence ??
        "No correspondence evidence is available for this imported row.";

    public string CollisionPairText =>
        _relationship is null
            ? "Collision pair: not proven"
            : $"Collision pair: " +
              $"{SplitWords(_relationship.CollisionAssetKind.ToString())} " +
              $"static model #{_relationship.ClipSourceOrdinal}";

    public string CompiledDispositionText =>
        $"Compiled disposition: " +
        $"{SplitWords(_renderModel.CompiledDisposition.ToString())}";

    public string AvailabilityText
    {
        get
        {
            if (_relationship is null)
            {
                return "Unavailable: this render row has no mutual, " +
                    "one-to-one correspondence proven for the exact " +
                    "imported bundle.";
            }
            if (_renderModel.CompiledDisposition ==
                StaticModelCompiledDisposition.Suppressed)
            {
                return "The compiled render and collision pair is " +
                    "suppressed. Undo restores both rows atomically.";
            }
            if (!TryGetBaselineCollisionModel(out _))
            {
                return "Unavailable: the paired collision row is missing " +
                    "or no longer baseline-present.";
            }
            if (_renderModel.Transform !=
                _renderModel.ImportedTransform)
            {
                return "Unavailable: undo the preview-only translation " +
                    "before suppressing the compiled pair.";
            }
            if (!_areEditorsEnabled())
            {
                return "Unavailable while compiled Save As validation or " +
                    "reopen-required state locks editing.";
            }

            return "Patch-saveable conservative operation: render and " +
                "collision tombstones are staged and validated atomically.";
        }
    }

    public bool HasExactBundleCorrespondence =>
        _relationship is not null;

    public bool CanSuppressCompiledPair =>
        _areEditorsEnabled() &&
        _relationship is not null &&
        _renderModel.CompiledDisposition ==
            StaticModelCompiledDisposition.BaselinePresent &&
        _renderModel.Transform == _renderModel.ImportedTransform &&
        TryGetBaselineCollisionModel(out _);

    internal void Refresh()
    {
        OnPropertyChanged(nameof(CorrespondenceStatusText));
        OnPropertyChanged(nameof(CorrespondenceHeadingText));
        OnPropertyChanged(nameof(CorrespondenceEvidenceText));
        OnPropertyChanged(nameof(CollisionPairText));
        OnPropertyChanged(nameof(CompiledDispositionText));
        OnPropertyChanged(nameof(AvailabilityText));
        OnPropertyChanged(nameof(HasExactBundleCorrespondence));
        OnPropertyChanged(nameof(CanSuppressCompiledPair));
        SuppressCompiledPairCommand.RaiseCanExecuteChanged();
    }

    private void SuppressCompiledPair()
    {
        if (_relationship is null ||
            !CanSuppressCompiledPair)
        {
            return;
        }

        _execute(new SuppressCompiledStaticModelCommand(_relationship));
    }

    private bool TryGetBaselineCollisionModel(
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
            } typed)
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
