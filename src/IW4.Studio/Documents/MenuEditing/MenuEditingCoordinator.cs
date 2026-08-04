using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Studio.Documents;

namespace IW4.Studio.Documents.MenuEditing;

/// <summary>
/// Document-scoped Menu authority and edit router. It rebuilds all target
/// occurrences from a race-safe editing-session capture whenever resolution
/// matters and retains no visual selection, property staging, or mutable Menu
/// graph of its own.
/// </summary>
public sealed partial class MenuEditingCoordinator : IDisposable
{
    private static readonly MenuBodyEmitter OwnerValidator = new();
    private readonly FastFileEditingSession _editingSession;
    private readonly AssetAuthoringAdapterRegistry _adapters;
    private readonly MenuAuthorityCapture _capture;
    private int _disposed;

    public MenuEditingCoordinator(
        FastFileEditingSession editingSession,
        AssetAuthoringAdapterRegistry adapters)
    {
        _editingSession =
            editingSession ?? throw new ArgumentNullException(nameof(editingSession));
        _adapters = adapters ?? throw new ArgumentNullException(nameof(adapters));
        _capture = new MenuAuthorityCapture(_editingSession, _adapters);
        RequireMenuAdapter<MenuDraft>(XAssetType.Menu);
        RequireMenuAdapter<MenuFileDraft>(XAssetType.MenuFile);
        _editingSession.TargetRowsChanged += OnTargetRowsChanged;
    }

    public event EventHandler<MenuEditingCoordinatorChangedEventArgs>? Changed;

    public Guid DocumentId => _editingSession.Document.DocumentId;

    public MenuAuthorityResolutionSnapshot ResolveMenu(string menuName)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(menuName);
        CapturedMenuAuthorityState state = _capture.Capture();
        return Resolve(state, menuName);
    }

    /// <summary>
    /// Resolves a top-level target Menu row to the first full authority for
    /// its logical name. A reference row can therefore resolve to an inline
    /// MenuFile authority or to a read-only dependency provider.
    /// </summary>
    public MenuAuthorityResolutionSnapshot ResolveTopLevelMenu(
        TargetZoneRowIdentity rowIdentity)
    {
        ThrowIfDisposed();
        CapturedMenuAuthorityState state = _capture.Capture();
        return Resolve(state, TopLevelMenuName(state, rowIdentity));
    }

    /// <summary>
    /// Resolves one stable MenuFile registration to the same first-full
    /// authority used by top-level Menu rows.
    /// </summary>
    public MenuAuthorityResolutionSnapshot ResolveMenuFileRegistration(
        TargetZoneRowIdentity menuFileRowIdentity,
        MenuRegistrationId registrationId)
    {
        ThrowIfDisposed();
        CapturedMenuAuthorityState state = _capture.Capture();
        return Resolve(
            state,
            MenuFileRegistrationName(
                state,
                menuFileRowIdentity,
                registrationId));
    }

    public MenuFileEditorSnapshot ReadMenuFile(
        TargetZoneRowIdentity rowIdentity)
    {
        ThrowIfDisposed();
        return _capture.Capture().RequireMenuFileRow(rowIdentity).Snapshot;
    }

    public MenuAuthorityEditResult ApplyMenuEdit(
        MenuAuthorityResolutionSnapshot expectedResolution,
        MenuEdit edit)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(expectedResolution);
        ArgumentNullException.ThrowIfNull(edit);
        CapturedMenuAuthorityState state = _capture.Capture();
        MenuAuthorityResolutionSnapshot current =
            RequireCurrentEditableResolution(
                state,
                expectedResolution,
                expectedResolution.RequestedName);
        return ApplyMenuEdit(state, expectedResolution, current, edit);
    }

    public MenuAuthorityEditResult ApplyTopLevelMenuEdit(
        TargetZoneRowIdentity rowIdentity,
        MenuAuthorityResolutionSnapshot expectedResolution,
        MenuEdit edit)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(expectedResolution);
        ArgumentNullException.ThrowIfNull(edit);
        CapturedMenuAuthorityState state = _capture.Capture();
        RequireExpectedRevision(state, expectedResolution);
        string currentName = TopLevelMenuName(state, rowIdentity);
        MenuAuthorityResolutionSnapshot current =
            RequireCurrentEditableResolution(
                state,
                expectedResolution,
                currentName);
        return ApplyMenuEdit(state, expectedResolution, current, edit);
    }

    public MenuAuthorityEditResult ApplyMenuFileRegistrationEdit(
        TargetZoneRowIdentity menuFileRowIdentity,
        MenuRegistrationId registrationId,
        MenuAuthorityResolutionSnapshot expectedResolution,
        MenuEdit edit)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(expectedResolution);
        ArgumentNullException.ThrowIfNull(edit);
        CapturedMenuAuthorityState state = _capture.Capture();
        RequireExpectedRevision(state, expectedResolution);
        string currentName = RequireCurrentRegistrationName(
            state,
            menuFileRowIdentity,
            registrationId,
            expectedResolution);
        MenuAuthorityResolutionSnapshot current =
            RequireCurrentEditableResolution(
                state,
                expectedResolution,
                currentName);
        return ApplyMenuEdit(state, expectedResolution, current, edit);
    }

    /// <summary>
    /// Applies an ordered MenuFile registration-list edit. Nested Menu edits
    /// are rejected here because they must first resolve and route through the
    /// document authority.
    /// </summary>
    public MenuFileEditResult ApplyMenuFileEdit(
        TargetZoneRowIdentity rowIdentity,
        MenuFileEdit edit)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(edit);
        if (edit is EditMenuFileRegistrationMenuEdit)
        {
            throw new InvalidOperationException(
                "Nested Menu edits must use ApplyMenuFileRegistrationEdit so the document authority is respected.");
        }

        CapturedMenuAuthorityState state = _capture.Capture();
        CapturedMenuFileRow capturedRow = state.RequireMenuFileRow(rowIdentity);
        int? targetRegistrationIndex = RegistrationIndex(
            capturedRow.Snapshot,
            edit);
        IAssetAuthoringAdapter adapter = _adapters.RequireAdapter(
            XAssetType.MenuFile);
        bool changed = _editingSession.MutateAuthoredDraftAtRevision(
            state.Revision,
            rowIdentity,
            adapter,
            draft =>
            {
                var menuFile = (MenuFileDraft)draft;
                menuFile.Apply(RebindRegistrationEdit(
                    menuFile.Snapshot,
                    edit,
                    targetRegistrationIndex));
            });
        MenuFileEditorSnapshot snapshot = _capture.Capture()
            .RequireMenuFileRow(rowIdentity)
            .Snapshot;
        if (changed)
        {
            RaiseChanged(
                MenuEditingCoordinatorChangeKind.MenuFileEdited,
                rowIdentity,
                normalizedMenuName: null,
                resolution: null);
        }

        return new MenuFileEditResult(changed, snapshot);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _editingSession.TargetRowsChanged -= OnTargetRowsChanged;
    }
}
