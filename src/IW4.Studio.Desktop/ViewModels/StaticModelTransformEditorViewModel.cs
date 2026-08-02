using IW4.Studio.MapEditor.Editing.Commands;
using IW4.Studio.MapEditor.Compilation.Bundles;
using IW4.Studio.MapEditor.Compilation.StaticModels;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.Editing.SavePlanning;
using IW4.Studio.Desktop.Rendering.WorldViewport;

namespace IW4.Studio.Desktop.ViewModels;

/// <summary>
/// Precision inspector for existing render-static-model translation. The
/// three coordinates form one draft so partially typed values never become
/// semantic edits. Applying the draft produces either a proof-gated atomic
/// Gfx/Col command or a clearly blocked paired-preview command; imported
/// baseline values are never mutated directly.
/// </summary>
public sealed class StaticModelTransformEditorViewModel :
    ObservableObject,
    IWorldViewportTranslationTool,
    IDisposable
{
    private readonly EditorMapDocument _document;
    private readonly CompiledMapBundle _bundle;
    private readonly EditorStaticModel _model;
    private readonly StaticModelCorrespondenceCatalog _catalog;
    private readonly StaticModelCompilationRelationship? _relationship;
    private readonly MapEditorEditingContext _editingContext;
    private readonly Func<bool> _areEditorsEnabled;
    private readonly Action<IMapEditCommand> _execute;
    private MapVector3 _observedOrigin;
    private decimal _x;
    private decimal _y;
    private decimal _z;
    private StaticModelTranslationEligibilityAssessment? _assessment;
    private bool _isManipulating;
    private bool _disposed;

    public StaticModelTransformEditorViewModel(
        EditorMapDocument document,
        CompiledMapBundle bundle,
        EditorStaticModel model,
        StaticModelCorrespondenceCatalog catalog,
        MapEditorEditingContext editingContext,
        Func<bool> areEditorsEnabled,
        Action<IMapEditCommand> execute)
    {
        _document = document ??
            throw new ArgumentNullException(nameof(document));
        _bundle = bundle ??
            throw new ArgumentNullException(nameof(bundle));
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _catalog = catalog ??
            throw new ArgumentNullException(nameof(catalog));
        _editingContext = editingContext ??
            throw new ArgumentNullException(nameof(editingContext));
        if (!ReferenceEquals(_editingContext.Document, _document))
        {
            throw new ArgumentException(
                "The editing context belongs to another map document.",
                nameof(editingContext));
        }
        _areEditorsEnabled = areEditorsEnabled ??
            throw new ArgumentNullException(nameof(areEditorsEnabled));
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        if (!model.IsImported ||
            model.Representation != StaticModelRepresentation.Render)
        {
            throw new ArgumentException(
                "Static-model transform inspection starts from an imported " +
                "render representation; an exact correspondence catalog " +
                "controls whether its collision counterpart may move " +
                "atomically.",
                nameof(model));
        }
        _catalog.TryGetByRenderObjectId(
            model.Id,
            out _relationship);
        _observedOrigin = model.Origin.Value;
        SynchronizeDraftToModel();
        ApplyPositionCommand = new ViewModelCommand(
            ApplyPosition,
            () => CanApplyPosition);
        CancelPositionCommand = new ViewModelCommand(
            CancelPosition,
            () => CanCancelPosition);
        RefreshAssessment();
    }

    public event EventHandler? DraftChanged;

    public ViewModelCommand ApplyPositionCommand { get; }
    public ViewModelCommand CancelPositionCommand { get; }

    public decimal X
    {
        get => _x;
        set => SetDraftCoordinate(
            ref _x,
            value,
            nameof(X));
    }

    public decimal Y
    {
        get => _y;
        set => SetDraftCoordinate(
            ref _y,
            value,
            nameof(Y));
    }

    public decimal Z
    {
        get => _z;
        set => SetDraftCoordinate(
            ref _z,
            value,
            nameof(Z));
    }

    public string SourceOrdinalText =>
        $"Gfx render static model #{_model.SourceOrdinal.Value}";

    public MapObjectId TargetObjectId => _model.Id;

    public int? RenderStaticModelSourceOrdinal =>
        _model.SourceOrdinal.Value;

    public MapVector3 DraftOrigin => new(
        checked((float)_x),
        checked((float)_y),
        checked((float)_z));

    public MapBounds? Bounds =>
        _model.ImportedTransform
            .WithOrigin(DraftOrigin)
            .Bounds;

    public bool CanManipulate =>
        !_disposed && _areEditorsEnabled();

    public bool IsManipulating => _isManipulating;

    public bool HasDraftChanges =>
        !SameExact(DraftOrigin, _model.Transform.Origin);

    public bool CanCancelPosition =>
        CanManipulate && HasDraftChanges;

    public string ClassificationText =>
        SplitWords(CurrentImpact.Classification.ToString());

    public string SaveBlockerText =>
        IsPatchSaveable
            ? "This destination preserves the imported Gfx cell/leaf, " +
              "probe, primary-light, light-grid, shadow, and collision-tree " +
              "contracts. Save As can stage the paired compiled translation."
            : CurrentBlocker;

    public bool IsPatchSaveable =>
        _assessment?.IsPatchEligible == true &&
        IsSemanticPairSynchronized() &&
        !HasUnsafePendingTranslationForPair();

    public bool IsCompiledSaveBlocked => !IsPatchSaveable;

    public bool CanApplyPosition =>
        CanManipulate &&
        (!SameExact(DraftOrigin, _model.Origin.Value) ||
         (_relationship is not null &&
          !IsSemanticPairSynchronized()));

    public string ApplyPositionText =>
        _relationship is not null &&
        !IsSemanticPairSynchronized()
            ? "Resynchronize pair"
            : "Apply changes";

    public string DraftStatusText =>
        IsManipulating
            ? "Moving in viewport"
            : HasDraftChanges
                ? "Unapplied viewport move"
                : "Position is committed";

    public string SaveBoundaryHeadingText =>
        IsPatchSaveable
            ? "COMPILED SAVE ELIGIBLE"
            : "COMPILED SAVE BLOCKED";

    public string PreviewScopeText =>
        IsPatchSaveable
            ? "Edit all three coordinates, then apply once. The viewport and " +
              "Live Preview consume the same semantic destination. Rotation, " +
              "scale, membership changes, and baked-light moves stay blocked."
            : _relationship is null
                ? "Apply changes the render preview only because no exact " +
                  "collision counterpart is proven. Compiled Save As remains " +
                  "blocked."
                : "Apply keeps the render and collision previews synchronized. " +
                  "This destination remains blocked from compiled Save As; " +
                  "undo or reopen before authoring a persistable move.";

    internal void Refresh()
    {
        if (!SameExact(_observedOrigin, _model.Origin.Value))
        {
            _observedOrigin = _model.Origin.Value;
            SynchronizeDraftToModel();
        }
        RefreshAssessment();
        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Y));
        OnPropertyChanged(nameof(Z));
        OnPropertyChanged(nameof(SourceOrdinalText));
        OnPropertyChanged(nameof(ClassificationText));
        OnPropertyChanged(nameof(SaveBlockerText));
        OnPropertyChanged(nameof(IsPatchSaveable));
        OnPropertyChanged(nameof(IsCompiledSaveBlocked));
        OnPropertyChanged(nameof(CanApplyPosition));
        OnPropertyChanged(nameof(CanCancelPosition));
        OnPropertyChanged(nameof(HasDraftChanges));
        OnPropertyChanged(nameof(IsManipulating));
        OnPropertyChanged(nameof(DraftOrigin));
        OnPropertyChanged(nameof(Bounds));
        OnPropertyChanged(nameof(CanManipulate));
        OnPropertyChanged(nameof(ApplyPositionText));
        OnPropertyChanged(nameof(DraftStatusText));
        OnPropertyChanged(nameof(SaveBoundaryHeadingText));
        OnPropertyChanged(nameof(PreviewScopeText));
        ApplyPositionCommand.RaiseCanExecuteChanged();
        CancelPositionCommand.RaiseCanExecuteChanged();
        DraftChanged?.Invoke(this, EventArgs.Empty);
    }

    public void BeginManipulation()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!CanManipulate || _isManipulating)
            return;

        _isManipulating = true;
        OnPropertyChanged(nameof(IsManipulating));
        OnPropertyChanged(nameof(DraftStatusText));
    }

    public void UpdateDraftOrigin(MapVector3 origin)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!CanManipulate)
            return;
        SetDraftOrigin(origin);
    }

    public void EndManipulation()
    {
        if (_disposed || !_isManipulating)
            return;

        _isManipulating = false;
        OnPropertyChanged(nameof(IsManipulating));
        OnPropertyChanged(nameof(DraftStatusText));
    }

    public void ApplyChanges() => ApplyPosition();

    public void CancelChanges() => CancelPosition();

    public void Dispose()
    {
        if (_disposed)
            return;

        CancelPosition();
        _disposed = true;
        _isManipulating = false;
    }

    private void SetDraftCoordinate(
        ref decimal field,
        decimal value,
        string propertyName)
    {
        float scalar = checked((float)value);
        if (!float.IsFinite(scalar))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Static-model origin components must be finite.");
        }
        if (field == value)
            return;

        field = value;
        OnPropertyChanged(propertyName);
        PublishDraft();
    }

    private void SetDraftOrigin(MapVector3 origin)
    {
        if (!origin.IsFinite)
        {
            throw new ArgumentOutOfRangeException(
                nameof(origin),
                "Static-model origin components must be finite.");
        }

        decimal x = ToDecimal(origin.X);
        decimal y = ToDecimal(origin.Y);
        decimal z = ToDecimal(origin.Z);
        if (_x == x && _y == y && _z == z)
            return;

        _x = x;
        _y = y;
        _z = z;
        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Y));
        OnPropertyChanged(nameof(Z));
        PublishDraft();
    }

    private void PublishDraft()
    {
        _editingContext.SetStaticModelTranslationDraft(
            _model,
            DraftOrigin);
        RefreshAssessment();
        OnPropertyChanged(nameof(ClassificationText));
        OnPropertyChanged(nameof(SaveBlockerText));
        OnPropertyChanged(nameof(IsPatchSaveable));
        OnPropertyChanged(nameof(IsCompiledSaveBlocked));
        OnPropertyChanged(nameof(CanApplyPosition));
        OnPropertyChanged(nameof(CanCancelPosition));
        OnPropertyChanged(nameof(HasDraftChanges));
        OnPropertyChanged(nameof(DraftOrigin));
        OnPropertyChanged(nameof(Bounds));
        OnPropertyChanged(nameof(ApplyPositionText));
        OnPropertyChanged(nameof(DraftStatusText));
        OnPropertyChanged(nameof(SaveBoundaryHeadingText));
        OnPropertyChanged(nameof(PreviewScopeText));
        ApplyPositionCommand.RaiseCanExecuteChanged();
        CancelPositionCommand.RaiseCanExecuteChanged();
        DraftChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyPosition()
    {
        if (!CanApplyPosition)
            return;

        MapVector3 replacement = DraftOrigin;
        StaticModelTranslationEligibilityAssessment? assessment =
            Evaluate(replacement);
        if (assessment?.Authorization is { } authorization &&
            IsSemanticPairSynchronized())
        {
            _execute(
                new TranslateCompiledStaticModelCommand(
                    authorization));
        }
        else if (_relationship is not null)
        {
            _execute(
                new PreviewPairedStaticModelTranslationCommand(
                    _relationship,
                    replacement));
        }
        else
        {
            _execute(new SetStaticModelOriginCommand(
                _model.Id,
                replacement));
        }

        _editingContext.ClearStaticModelTranslationDraft(_model.Id);
        EndManipulation();
    }

    private void CancelPosition()
    {
        if (_disposed)
            return;

        _editingContext.ClearStaticModelTranslationDraft(_model.Id);
        SynchronizeDraftToModel();
        RefreshAssessment();
        _isManipulating = false;
        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Y));
        OnPropertyChanged(nameof(Z));
        OnPropertyChanged(nameof(ClassificationText));
        OnPropertyChanged(nameof(SaveBlockerText));
        OnPropertyChanged(nameof(IsPatchSaveable));
        OnPropertyChanged(nameof(IsCompiledSaveBlocked));
        OnPropertyChanged(nameof(CanApplyPosition));
        OnPropertyChanged(nameof(CanCancelPosition));
        OnPropertyChanged(nameof(HasDraftChanges));
        OnPropertyChanged(nameof(IsManipulating));
        OnPropertyChanged(nameof(DraftOrigin));
        OnPropertyChanged(nameof(Bounds));
        OnPropertyChanged(nameof(ApplyPositionText));
        OnPropertyChanged(nameof(DraftStatusText));
        OnPropertyChanged(nameof(SaveBoundaryHeadingText));
        OnPropertyChanged(nameof(PreviewScopeText));
        ApplyPositionCommand.RaiseCanExecuteChanged();
        CancelPositionCommand.RaiseCanExecuteChanged();
        DraftChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SynchronizeDraftToModel()
    {
        MapVector3 origin = _model.Origin.Value;
        _x = ToDecimal(origin.X);
        _y = ToDecimal(origin.Y);
        _z = ToDecimal(origin.Z);
    }

    private static decimal ToDecimal(float value) =>
        Convert.ToDecimal(value);

    private static string SplitWords(string value) =>
        string.Concat(value.Select((character, index) =>
            index > 0 && char.IsUpper(character)
                ? $" {character}"
                : character.ToString()));

    private MapEditImpact CurrentImpact =>
        IsPatchSaveable
            ? MapEditImpactTaxonomy.CompiledStaticModelTranslation(
                _relationship!.CollisionAssetKind)
            : MapEditImpactTaxonomy.StaticModelTransform();

    private string CurrentBlocker
    {
        get
        {
            if (_relationship is null)
            {
                return _catalog.TryGetAssessment(
                        _model.Id,
                        out StaticModelCorrespondenceAssessment?
                            correspondence) &&
                    correspondence is not null
                    ? correspondence.Evidence
                    : "No mutual exact-bundle Gfx/Col relationship was " +
                      "proven for this render instance.";
            }
            if (!IsSemanticPairSynchronized())
            {
                return "The render and collision semantic origins are no " +
                    "longer synchronized. Resynchronize the preview pair, " +
                    "then undo or reopen the legacy render-only edits before " +
                    "attempting a compiled save.";
            }
            if (HasUnsafePendingTranslationForPair())
            {
                return "An earlier preview-only translation for this pair " +
                    "remains in the pending journal. The preview is " +
                    "synchronized, but compiled Save As stays blocked until " +
                    "that edit is undone or the original map is reopened.";
            }
            return _assessment?.Evidence ??
                MapEditImpactTaxonomy.StaticModelTransform().SaveBlocker!;
        }
    }

    private void RefreshAssessment() =>
        _assessment = Evaluate(DraftOrigin);

    private StaticModelTranslationEligibilityAssessment? Evaluate(
        MapVector3 destination) =>
        _relationship is null
            ? null
            : StaticModelTranslationEligibilityEvaluator.Evaluate(
                _bundle,
                _catalog,
                _relationship,
                destination);

    private bool IsSemanticPairSynchronized()
    {
        if (_relationship is null ||
            !_document.TryGetObject(
                _relationship.CollisionObjectId,
                out EditorMapObject? value) ||
            value is not EditorStaticModel collision ||
            collision.Representation !=
                StaticModelRepresentation.Collision)
        {
            return false;
        }

        MapVector3 renderOrigin = _model.Transform.Origin;
        MapVector3 collisionOrigin = collision.Transform.Origin;
        return SameBits(renderOrigin.X, collisionOrigin.X) &&
            SameBits(renderOrigin.Y, collisionOrigin.Y) &&
            SameBits(renderOrigin.Z, collisionOrigin.Z);
    }

    private bool HasUnsafePendingTranslationForPair()
    {
        if (_relationship is null)
            return false;

        return _document.History.PendingJournal.Any(entry =>
            entry.Command.Kind == MapEditKind.StaticModelTransform &&
            entry.Command is not TranslateCompiledStaticModelCommand &&
            (entry.Command.TargetObjects.Contains(
                 _relationship.RenderObjectId) ||
             entry.Command.TargetObjects.Contains(
                 _relationship.CollisionObjectId)));
    }

    private static bool SameBits(float left, float right) =>
        BitConverter.SingleToInt32Bits(left) ==
        BitConverter.SingleToInt32Bits(right);

    private static bool SameExact(
        MapVector3 left,
        MapVector3 right) =>
        SameBits(left.X, right.X) &&
        SameBits(left.Y, right.Y) &&
        SameBits(left.Z, right.Z);
}
