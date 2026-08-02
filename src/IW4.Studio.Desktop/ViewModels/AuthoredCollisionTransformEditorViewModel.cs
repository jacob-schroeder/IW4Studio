using IW4.Studio.Desktop.Rendering.WorldViewport;
using IW4.Studio.MapEditor.Compilation.Collision;
using IW4.Studio.MapEditor.Editing.Collision;
using IW4.Studio.MapEditor.Editing.Commands;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.Editing.SavePlanning;

namespace IW4.Studio.Desktop.ViewModels;

/// <summary>
/// Direct-manipulation tool for canonical authored collision. Pointer updates
/// remain one transient draft; Apply replaces the immutable source with one
/// semantic command and therefore creates exactly one undo record.
/// </summary>
public sealed class AuthoredCollisionTransformEditorViewModel :
    ObservableObject,
    IWorldViewportTranslationTool,
    IDisposable
{
    private readonly EditorMapDocument _document;
    private readonly MapObjectId _objectId;
    private readonly MapEditorEditingContext _editingContext;
    private readonly Func<bool> _areEditorsEnabled;
    private readonly Action<IMapEditCommand> _execute;
    private EditorAuthoredCollisionObject _authored;
    private bool _isManipulating;
    private bool _disposed;

    public AuthoredCollisionTransformEditorViewModel(
        EditorMapDocument document,
        EditorAuthoredCollisionObject authored,
        MapEditorEditingContext editingContext,
        Func<bool> areEditorsEnabled,
        Action<IMapEditCommand> execute)
    {
        _document =
            document ?? throw new ArgumentNullException(nameof(document));
        _authored =
            authored ?? throw new ArgumentNullException(nameof(authored));
        _editingContext = editingContext ??
            throw new ArgumentNullException(nameof(editingContext));
        _areEditorsEnabled = areEditorsEnabled ??
            throw new ArgumentNullException(nameof(areEditorsEnabled));
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        if (!ReferenceEquals(_editingContext.Document, _document))
        {
            throw new ArgumentException(
                "The editing context belongs to another map document.",
                nameof(editingContext));
        }
        if (!_document.TryGetObject(
                authored.Id,
                out EditorMapObject? owned) ||
            !ReferenceEquals(owned, authored))
        {
            throw new ArgumentException(
                "Authored collision must belong to the edited document.",
                nameof(authored));
        }

        _objectId = authored.Id;
        ApplyChangesCommand = new ViewModelCommand(
            ApplyChanges,
            () => CanApplyChanges);
        CancelChangesCommand = new ViewModelCommand(
            CancelChanges,
            () => CanCancelChanges);
        RemoveSourceCommand = new ViewModelCommand(
            RemoveSource,
            () => CanRemoveSource);
    }

    public event EventHandler? DraftChanged;

    public ViewModelCommand ApplyChangesCommand { get; }

    public ViewModelCommand CancelChangesCommand { get; }

    public ViewModelCommand RemoveSourceCommand { get; }

    public MapObjectId TargetObjectId => _objectId;

    public int? RenderStaticModelSourceOrdinal => null;

    public MapVector3 DraftOrigin =>
        DraftState?.CandidateOrigin ??
        AuthoredCollisionSourceTransforms.GetTranslationAnchor(
            _authored.Source);

    public MapBounds? Bounds =>
        DraftState?.CandidateBounds ??
        AuthoredCollisionSourceTransforms.GetBounds(_authored.Source);

    public bool CanManipulate =>
        !_disposed &&
        _areEditorsEnabled() &&
        IsCurrentDocumentObject;

    public bool HasDraftChanges => DraftState is not null;

    public bool CanApplyChanges =>
        CanManipulate && HasDraftChanges;

    public bool CanCancelChanges => HasDraftChanges;

    public bool CanRemoveSource =>
        CanManipulate && !HasDraftChanges;

    public bool IsManipulating => _isManipulating;

    public string SourceIdentityText =>
        $"{SplitWords(_authored.Source.GeometryKind.ToString())} · " +
        $"{SplitWords(_authored.Source.Ownership.Category.ToString())}";

    public string ClassificationText =>
        SplitWords(
            MapEditImpactTaxonomy
                .AuthoredCollisionGeometry()
                .Classification
                .ToString());

    public string DraftStatusText =>
        IsManipulating
            ? "Moving collision in viewport"
            : HasDraftChanges
                ? "Unapplied collision move"
                : "Collision position is committed";

    public string DraftOriginText => DraftOrigin.ToString();

    public string SaveBoundaryText =>
        MapEditImpactTaxonomy
            .AuthoredCollisionGeometry()
            .SaveBlocker ??
        "Authored collision requires a full rebuild.";

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

        _editingContext.SetAuthoredCollisionTranslationDraft(
            _authored,
            origin);
        Refresh();
    }

    public void EndManipulation()
    {
        if (_disposed || !_isManipulating)
            return;

        _isManipulating = false;
        OnPropertyChanged(nameof(IsManipulating));
        OnPropertyChanged(nameof(DraftStatusText));
    }

    public void ApplyChanges()
    {
        if (!CanApplyChanges || DraftState is not { } draft)
            return;

        _execute(
            new ReplaceAuthoredCollisionSourceCommand(
                _document,
                draft.CandidateSource));
        if (_disposed)
            return;

        _editingContext.ClearAuthoredCollisionTranslationDraft(_objectId);
        EndManipulation();
        Refresh();
    }

    public void CancelChanges()
    {
        if (_disposed)
            return;

        _editingContext.ClearAuthoredCollisionTranslationDraft(_objectId);
        _isManipulating = false;
        Refresh();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _isManipulating = false;
        _editingContext.ClearAuthoredCollisionTranslationDraft(_objectId);
    }

    internal void Refresh()
    {
        if (_disposed)
            return;

        if (_document.TryGetObject(
                _objectId,
                out EditorMapObject? current) &&
            current is EditorAuthoredCollisionObject authored)
        {
            _authored = authored;
        }

        OnPropertyChanged(nameof(DraftOrigin));
        OnPropertyChanged(nameof(Bounds));
        OnPropertyChanged(nameof(CanManipulate));
        OnPropertyChanged(nameof(HasDraftChanges));
        OnPropertyChanged(nameof(CanApplyChanges));
        OnPropertyChanged(nameof(CanCancelChanges));
        OnPropertyChanged(nameof(CanRemoveSource));
        OnPropertyChanged(nameof(IsManipulating));
        OnPropertyChanged(nameof(SourceIdentityText));
        OnPropertyChanged(nameof(DraftStatusText));
        OnPropertyChanged(nameof(DraftOriginText));
        ApplyChangesCommand.RaiseCanExecuteChanged();
        CancelChangesCommand.RaiseCanExecuteChanged();
        RemoveSourceCommand.RaiseCanExecuteChanged();
        DraftChanged?.Invoke(this, EventArgs.Empty);
    }

    private AuthoredCollisionTranslationDraftState? DraftState =>
        _editingContext.AuthoredCollisionTranslationDraft is
            { ObjectId: var id } draft &&
        id == _objectId
            ? draft
            : null;

    private bool IsCurrentDocumentObject =>
        _document.TryGetObject(
            _objectId,
            out EditorMapObject? current) &&
        current is EditorAuthoredCollisionObject;

    private void RemoveSource()
    {
        if (!CanRemoveSource)
            return;

        _execute(
            new RemoveAuthoredCollisionSourceCommand(
                _document,
                _objectId));
    }

    private static string SplitWords(string value) =>
        string.Concat(value.Select((character, index) =>
            index > 0 && char.IsUpper(character)
                ? $" {character}"
                : character.ToString()));
}
