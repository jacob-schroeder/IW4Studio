using System.Collections.ObjectModel;
using IW4.FastFiles.Zone;

namespace IW4.Studio.Documents;

/// <summary>
/// Type-agnostic detached-draft contract. Implementations must create and
/// clone values without retaining mutable arrays or collections from runtime
/// assets, XZone memory, or the target source snapshot.
/// </summary>
public interface ITargetZoneDraftAdapter<TDraft>
    where TDraft : notnull
{
    /// <summary>Creates an independent baseline from immutable target source.</summary>
    TDraft CreateBaseline(TargetZoneRowSource source);

    /// <summary>Returns a deep detached copy of a draft value.</summary>
    TDraft Clone(TDraft draft);

    /// <summary>Compares draft values semantically rather than by UI event.</summary>
    bool SemanticallyEquals(TDraft baseline, TDraft current);
}

/// <summary>
/// Ordered target authoring state. It is keyed exclusively by Step 04's
/// document-and-row identity and never by a pool address or active provider.
/// </summary>
public sealed class TargetZoneDocument
{
    private readonly IReadOnlyList<WorkspaceAssetCatalogEntry> _rows;
    private readonly IReadOnlyDictionary<TargetZoneRowIdentity, WorkspaceAssetCatalogEntry> _rowsByIdentity;

    public TargetZoneDocument(FastFileWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        WorkspaceAssetCatalogEntry[] rows = workspace.AssetCatalog.TargetEntries.ToArray();
        if (rows.Length != workspace.TargetSource.Rows.Count)
        {
            throw new InvalidDataException(
                "Workspace catalog target rows do not match the immutable target source count.");
        }

        var rowsByIdentity = new Dictionary<TargetZoneRowIdentity, WorkspaceAssetCatalogEntry>();
        for (int index = 0; index < rows.Length; index++)
        {
            WorkspaceAssetCatalogEntry entry = rows[index];
            TargetZoneRowIdentity identity = entry.TargetRowIdentity
                ?? throw new InvalidDataException(
                    $"Target catalog entry {index} has no stable target-row identity.");
            if (identity.DocumentId != workspace.Document.DocumentId ||
                identity.SerializedIndex != index ||
                entry.TargetRow is null ||
                !rowsByIdentity.TryAdd(identity, entry))
            {
                throw new InvalidDataException(
                    "Workspace catalog target rows do not preserve unique contiguous target identities.");
            }
        }

        DocumentId = workspace.Document.DocumentId;
        _rows = Array.AsReadOnly(rows);
        _rowsByIdentity = new ReadOnlyDictionary<TargetZoneRowIdentity, WorkspaceAssetCatalogEntry>(rowsByIdentity);
    }

    public Guid DocumentId { get; }

    /// <summary>Exact serialized target rows, always in source order.</summary>
    public IReadOnlyList<WorkspaceAssetCatalogEntry> Rows => _rows;

    public bool TryGetRow(
        TargetZoneRowIdentity identity,
        out WorkspaceAssetCatalogEntry? row) =>
        _rowsByIdentity.TryGetValue(identity, out row);

    public WorkspaceAssetCatalogEntry GetRow(TargetZoneRowIdentity identity) =>
        TryGetRow(identity, out WorkspaceAssetCatalogEntry? row)
            ? row!
            : throw new KeyNotFoundException(
                $"Target row {identity.DocumentId}/{identity.SerializedIndex} is not part of this authoring document.");
}

/// <summary>
/// Reviewable semantic change for one target row. The change set contains no
/// UI events and no runtime asset references.
/// </summary>
public sealed record AssetRowChange(
    TargetZoneRowIdentity RowIdentity,
    XAssetType SerializedType,
    string? OriginalSerializedName,
    WorkspaceAssetOrigin Origin,
    long FirstChangedRevision,
    long LastChangedRevision);

/// <summary>
/// Immutable, deterministically ordered semantic changes for prompt and save
/// consumers. The typed draft values remain accessible only through a save
/// snapshot, where each is copied again before it is returned.
/// </summary>
public sealed class AssetChangeSet
{
    private readonly IReadOnlyList<AssetRowChange> _changes;
    private readonly IReadOnlyDictionary<TargetZoneRowIdentity, AssetRowChange> _changesByRow;

    internal AssetChangeSet(IEnumerable<AssetRowChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        AssetRowChange[] ordered = changes
            .OrderBy(change => change.RowIdentity.SerializedIndex)
            .ToArray();
        var changesByRow = new Dictionary<TargetZoneRowIdentity, AssetRowChange>();
        foreach (AssetRowChange change in ordered)
        {
            if (change.RowIdentity.DocumentId == Guid.Empty ||
                change.RowIdentity.SerializedIndex < 0 ||
                change.FirstChangedRevision <= 0 ||
                change.LastChangedRevision < change.FirstChangedRevision ||
                !changesByRow.TryAdd(change.RowIdentity, change))
            {
                throw new InvalidDataException(
                    "Asset change-set entries must have unique stable rows and monotonic revisions.");
            }
        }

        _changes = Array.AsReadOnly(ordered);
        _changesByRow = new ReadOnlyDictionary<TargetZoneRowIdentity, AssetRowChange>(changesByRow);
    }

    public int ChangedRowCount => _changes.Count;

    public bool IsEmpty => _changes.Count == 0;

    public IReadOnlyList<AssetRowChange> Changes => _changes;

    public bool TryGetChange(
        TargetZoneRowIdentity identity,
        out AssetRowChange? change) =>
        _changesByRow.TryGetValue(identity, out change);
}

/// <summary>
/// Immutable point-in-time save input. It can outlive the editing session and
/// returns a fresh adapter clone for every typed draft read.
/// </summary>
public sealed class FastFileEditingSaveSnapshot
{
    private readonly IReadOnlyDictionary<TargetZoneRowIdentity, IEditingSessionDraftCapture> _drafts;

    internal FastFileEditingSaveSnapshot(
        Guid sessionId,
        Guid documentId,
        long revision,
        AssetChangeSet changeSet,
        IReadOnlyDictionary<TargetZoneRowIdentity, IEditingSessionDraftCapture> drafts)
    {
        if (sessionId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(sessionId));
        if (documentId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(documentId));
        if (revision < 0)
            throw new ArgumentOutOfRangeException(nameof(revision));
        ArgumentNullException.ThrowIfNull(changeSet);
        ArgumentNullException.ThrowIfNull(drafts);
        if (changeSet.ChangedRowCount != drafts.Count ||
            changeSet.Changes.Any(change => !drafts.ContainsKey(change.RowIdentity)))
        {
            throw new InvalidDataException(
                "A save snapshot must contain exactly one detached draft for every semantic change.");
        }

        SessionId = sessionId;
        DocumentId = documentId;
        Revision = revision;
        ChangeSet = changeSet;
        _drafts = new ReadOnlyDictionary<TargetZoneRowIdentity, IEditingSessionDraftCapture>(
            new Dictionary<TargetZoneRowIdentity, IEditingSessionDraftCapture>(drafts));
    }

    internal Guid SessionId { get; }

    public Guid DocumentId { get; }

    public long Revision { get; }

    public AssetChangeSet ChangeSet { get; }

    public bool TryGetDraft<TDraft>(
        TargetZoneRowIdentity identity,
        out TDraft draft)
        where TDraft : notnull
    {
        if (_drafts.TryGetValue(identity, out IEditingSessionDraftCapture? capture) &&
            capture is EditingSessionDraftCapture<TDraft> typed)
        {
            draft = typed.CloneDraft();
            return true;
        }

        draft = default!;
        return false;
    }

    internal bool TryGetCapture(
        TargetZoneRowIdentity identity,
        out IEditingSessionDraftCapture? capture) =>
        _drafts.TryGetValue(identity, out capture);

    internal bool TryGetDraftObject(TargetZoneRowIdentity identity, out object? draft)
    {
        if (_drafts.TryGetValue(identity, out IEditingSessionDraftCapture? capture))
        {
            draft = capture.CloneObject();
            return true;
        }

        draft = null;
        return false;
    }
}

/// <summary>The last committed Save As baseline. It is kept
/// separate from the immutable opened-source workspace so an N+1 edit can
/// survive acknowledgement of an N snapshot without replacing its runtime.</summary>
public sealed record SavedDocumentState
{
    public SavedDocumentState(string physicalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalPath);
        PhysicalPath = Path.GetFullPath(physicalPath);
    }

    public string PhysicalPath { get; }
}

/// <summary>
/// Exclusive source-session lease for a compiled-map transaction. Ordinary
/// Studio Save As retains its snapshot N/N+1 behavior; only this explicit
/// lease temporarily blocks authoring mutations.
/// </summary>
public sealed class FastFileCompiledMapSaveLease : IDisposable
{
    private readonly FastFileEditingSession _session;
    private readonly Guid _leaseId;
    private int _disposed;

    internal FastFileCompiledMapSaveLease(
        FastFileEditingSession session,
        Guid leaseId,
        Guid documentId,
        long revision)
    {
        _session =
            session ?? throw new ArgumentNullException(nameof(session));
        if (leaseId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(leaseId));
        if (documentId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(documentId));
        if (revision < 0)
            throw new ArgumentOutOfRangeException(nameof(revision));

        _leaseId = leaseId;
        DocumentId = documentId;
        Revision = revision;
    }

    public Guid DocumentId { get; }

    public long Revision { get; }

    public bool IsActive =>
        Volatile.Read(ref _disposed) == 0;

    public FastFileEditingSaveSnapshot CaptureForSave()
    {
        ThrowIfDisposed();
        return _session.CaptureForCompiledMapSave(
            _leaseId,
            Revision);
    }

    public void MarkRevisionSaved(
        FastFileEditingSaveSnapshot snapshot,
        SavedDocumentState savedDocumentState)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(savedDocumentState);
        _session.MarkCompiledMapRevisionSaved(
            _leaseId,
            Revision,
            snapshot,
            savedDocumentState);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _session.ReleaseCompiledMapSaveLease(
            _leaseId,
            Revision);
    }

    private void ThrowIfDisposed()
    {
        if (!IsActive)
        {
            throw new ObjectDisposedException(
                nameof(FastFileCompiledMapSaveLease));
        }
    }
}

/// <summary>
/// Mutable authoring layer over an immutable workspace. Mutation, capture,
/// acknowledgement, and disposal are serialized by one short critical
/// section. A save can therefore acknowledge revision N after revision N+1
/// has been edited; only the content actually captured at N becomes clean.
/// </summary>
public sealed class FastFileEditingSession : IDisposable
{
    private readonly object _gate = new();
    private readonly Guid _sessionId = Guid.NewGuid();
    private readonly Dictionary<TargetZoneRowIdentity, IEditingSessionDraftState> _drafts = [];
    private readonly HashSet<TargetZoneRowIdentity> _openDrafts = [];
    private readonly CancellationTokenSource _cancellation = new();
    private readonly CancellationToken _cancellationToken;
    private AssetChangeSet _changeSet = new([]);
    private long _revision;
    private long _lastSavedRevision;
    private SavedDocumentState _savedDocumentState;
    private TargetZoneRowIdentity? _selectedRow;
    private Guid? _compiledMapSaveLeaseId;
    private bool _disposed;

    public FastFileEditingSession(FastFileWorkspace workspace)
    {
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        Document = new TargetZoneDocument(workspace);
        _savedDocumentState = new SavedDocumentState(
            workspace.TargetSource.PhysicalPath);
        _cancellationToken = _cancellation.Token;
    }

    public FastFileWorkspace Workspace { get; }

    public TargetZoneDocument Document { get; }

    public CancellationToken CancellationToken => _cancellationToken;

    public long Revision
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                return _revision;
            }
        }
    }

    public long LastSavedRevision
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                return _lastSavedRevision;
            }
        }
    }

    public SavedDocumentState SavedDocumentState
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                return _savedDocumentState;
            }
        }
    }

    public AssetChangeSet ChangeSet
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                return _changeSet;
            }
        }
    }

    public bool IsDirty
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                return !_changeSet.IsEmpty;
            }
        }
    }

    public bool IsCompiledMapSaveInProgress
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                return _compiledMapSaveLeaseId is not null;
            }
        }
    }

    public TargetZoneRowIdentity? SelectedRow
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                return _selectedRow;
            }
        }
    }

    public void SelectRow(TargetZoneRowIdentity? identity)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (identity is { } selected)
                _ = Document.GetRow(selected);

            _selectedRow = identity;
        }
    }

    /// <summary>
    /// Lazily creates an editable detached draft and marks its editor open.
    /// Opening, viewing, and closing drafts are never semantic mutations.
    /// </summary>
    public TDraft OpenDraft<TDraft>(
        TargetZoneRowIdentity identity,
        ITargetZoneDraftAdapter<TDraft> adapter)
        where TDraft : notnull
    {
        ArgumentNullException.ThrowIfNull(adapter);

        lock (_gate)
        {
            ThrowIfDisposed();
            DraftState<TDraft> state = GetOrCreateDraft(identity, adapter);
            _openDrafts.Add(identity);
            return state.CloneCurrent();
        }
    }

    /// <summary>
    /// Returns a detached current copy. The stored draft cannot be changed by
    /// a caller except through <see cref="MutateDraft{TDraft}"/>.
    /// </summary>
    public TDraft ReadDraft<TDraft>(
        TargetZoneRowIdentity identity,
        ITargetZoneDraftAdapter<TDraft> adapter)
        where TDraft : notnull
    {
        ArgumentNullException.ThrowIfNull(adapter);

        lock (_gate)
        {
            ThrowIfDisposed();
            return GetExistingDraft<TDraft>(identity, adapter).CloneCurrent();
        }
    }

    public bool IsDraftOpen(TargetZoneRowIdentity identity)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            return _openDrafts.Contains(identity);
        }
    }

    /// <summary>
    /// Closes an editor while retaining its detached draft. Reopening it uses
    /// the same draft and never alters revision or dirty state.
    /// </summary>
    public void CloseDraft(TargetZoneRowIdentity identity)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _openDrafts.Remove(identity);
        }
    }

    /// <summary>
    /// Applies one serialized semantic mutation. The mutation receives a
    /// detached working copy and the session stores another clone afterwards,
    /// so retained caller references cannot bypass revision tracking.
    /// </summary>
    public bool MutateDraft<TDraft>(
        TargetZoneRowIdentity identity,
        ITargetZoneDraftAdapter<TDraft> adapter,
        Action<TDraft> mutation)
        where TDraft : notnull
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(mutation);

        lock (_gate)
        {
            ThrowIfDisposed();
            RequireNoCompiledMapSave();
            DraftState<TDraft> state = GetOrCreateDraft(identity, adapter);
            TDraft working = state.CloneCurrent();
            mutation(working);
            if (state.SemanticallyEqualsCurrent(working))
                return false;

            long revision = NextRevision();
            state.SetCurrent(working, revision);
            RebuildChangeSet();
            return true;
        }
    }

    /// <summary>
    /// Applies one serialized semantic mutation only when the session still
    /// has the revision used to validate the replacement. The revision check
    /// and detached-draft replacement share the session lock, closing the
    /// check-then-mutate race for compilation and patch orchestration.
    /// </summary>
    public bool MutateDraftAtRevision<TDraft>(
        long expectedRevision,
        TargetZoneRowIdentity identity,
        ITargetZoneDraftAdapter<TDraft> adapter,
        Action<TDraft> mutation)
        where TDraft : notnull
    {
        if (expectedRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(mutation);

        lock (_gate)
        {
            ThrowIfDisposed();
            RequireNoCompiledMapSave();
            if (_revision != expectedRevision)
            {
                throw new InvalidOperationException(
                    $"Editing-session revision changed from {expectedRevision} " +
                    $"to {_revision}; discard the validated replacement and replan.");
            }

            DraftState<TDraft> state = GetOrCreateDraft(identity, adapter);
            TDraft working = state.CloneCurrent();
            mutation(working);
            if (state.SemanticallyEqualsCurrent(working))
                return false;

            long revision = NextRevision();
            state.SetCurrent(working, revision);
            RebuildChangeSet();
            return true;
        }
    }

    /// <summary>
    /// Applies a typed authoring-adapter mutation to the current detached
    /// draft at an exact revision. Unlike the generic draft API, this bridge
    /// can compose with a draft captured through another registry instance
    /// because compatibility is established by serialized type and declared
    /// draft type instead of adapter object identity.
    /// </summary>
    public bool MutateAuthoredDraftAtRevision(
        long expectedRevision,
        TargetZoneRowIdentity identity,
        IAssetAuthoringAdapter adapter,
        Action<object> mutation)
    {
        if (expectedRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(mutation);

        lock (_gate)
        {
            ThrowIfDisposed();
            RequireNoCompiledMapSave();
            if (_revision != expectedRevision)
            {
                throw new InvalidOperationException(
                    $"Editing-session revision changed from {expectedRevision} " +
                    $"to {_revision}; discard the validated replacement and replan.");
            }

            WorkspaceAssetCatalogEntry entry = RequireEditableRow(identity);
            if (entry.AssetType != adapter.AssetType)
            {
                throw new InvalidOperationException(
                    $"Target row {identity.SerializedIndex} is {entry.AssetType}, " +
                    $"not the supplied {adapter.AssetType} authoring type.");
            }

            if (!_drafts.TryGetValue(
                    identity,
                    out IEditingSessionDraftState? state))
            {
                var sessionAdapter =
                    new RegisteredAssetAuthoringAdapter(adapter);
                state = GetOrCreateDraft<object>(
                    identity,
                    sessionAdapter);
            }
            if (state.DraftType != adapter.DraftType)
            {
                throw new InvalidOperationException(
                    $"Target row {identity.SerializedIndex} has detached draft " +
                    $"type '{state.DraftType.FullName}', not the supplied " +
                    $"'{adapter.DraftType.FullName}'.");
            }

            object working = state.CloneCurrentObject();
            mutation(working);
            if (!adapter.DraftType.IsInstanceOfType(working))
            {
                throw new InvalidDataException(
                    $"The authored mutation for target row " +
                    $"{identity.SerializedIndex} produced incompatible draft " +
                    $"type '{working?.GetType().FullName ?? "null"}'.");
            }
            if (state.SemanticallyEqualsCurrentObject(working))
                return false;

            long revision = NextRevision();
            state.SetCurrentObject(working, revision);
            RebuildChangeSet();
            return true;
        }
    }

    public bool RevertOne(TargetZoneRowIdentity identity)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            RequireNoCompiledMapSave();
            _ = RequireEditableRow(identity);
            if (!_drafts.TryGetValue(identity, out IEditingSessionDraftState? state) ||
                !state.IsChanged)
            {
                return false;
            }

            long revision = NextRevision();
            state.RestoreSavedBaseline(revision);
            RebuildChangeSet();
            return true;
        }
    }

    public bool RevertAll()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            RequireNoCompiledMapSave();
            IEditingSessionDraftState[] changed = _drafts.Values
                .Where(state => state.IsChanged)
                .OrderBy(state => state.Identity.SerializedIndex)
                .ToArray();
            if (changed.Length == 0)
                return false;

            foreach (IEditingSessionDraftState state in changed)
                _ = RequireEditableRow(state.Identity);

            long revision = NextRevision();
            foreach (IEditingSessionDraftState state in changed)
                state.RestoreSavedBaseline(revision);
            RebuildChangeSet();
            return true;
        }
    }

    /// <summary>
    /// Creates a race-safe save input. The returned snapshot has its own deep
    /// draft copies and may be consumed after further session mutations.
    /// </summary>
    public FastFileEditingSaveSnapshot CaptureForSave()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            return CaptureForSaveUnsafe();
        }
    }

    /// <summary>
    /// Acquires exclusive mutation ownership for a compiled-map transaction
    /// and captures the source revision atomically with the lease.
    /// </summary>
    public FastFileCompiledMapSaveLease AcquireCompiledMapSaveLease()
    {
        Guid leaseId;
        long revision;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_compiledMapSaveLeaseId is not null)
            {
                throw new InvalidOperationException(
                    "A compiled-map save already owns this Studio editing " +
                    "session.");
            }

            leaseId = Guid.NewGuid();
            revision = _revision;
            _compiledMapSaveLeaseId = leaseId;
        }

        return new FastFileCompiledMapSaveLease(
            this,
            leaseId,
            Document.DocumentId,
            revision);
    }

    /// <summary>
    /// Creates a disposable compilation session seeded from the exact
    /// detached drafts in one save capture. The source session is never
    /// mutated; callers may layer additional changes onto the returned
    /// session and discard it on any validation failure.
    /// </summary>
    public FastFileEditingSession CreateStagingSession(
        FastFileEditingSaveSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_gate)
        {
            ThrowIfDisposed();
            if (snapshot.SessionId != _sessionId ||
                snapshot.DocumentId != Document.DocumentId)
            {
                throw new InvalidOperationException(
                    "The save snapshot does not belong to this editing " +
                    "session and target document.");
            }
            if (snapshot.Revision > _revision)
            {
                throw new InvalidDataException(
                    "A staging snapshot cannot refer to a future document " +
                    "revision.");
            }

            var staging = new FastFileEditingSession(Workspace);
            try
            {
                foreach (AssetRowChange change in snapshot.ChangeSet.Changes)
                {
                    if (!snapshot.TryGetCapture(
                            change.RowIdentity,
                            out IEditingSessionDraftCapture? capture) ||
                        capture is null)
                    {
                        throw new InvalidDataException(
                            $"Captured change for target row " +
                            $"{change.RowIdentity.SerializedIndex} has no " +
                            "detached draft.");
                    }
                    capture.SeedInto(staging, change);
                }
                return staging;
            }
            catch
            {
                staging.Dispose();
                throw;
            }
        }
    }

    /// <summary>
    /// Acknowledges exactly one captured revision. If edits occurred after
    /// capture, their new current values are compared to the acknowledged
    /// saved baseline and remain dirty.
    /// </summary>
    public void MarkRevisionSaved(FastFileEditingSaveSnapshot snapshot)
        => MarkRevisionSaved(snapshot, savedDocumentState: null);

    /// <summary>Records a durable, fresh-runtime-validated Save As baseline
    /// while acknowledging only the captured revision.</summary>
    public void MarkRevisionSaved(
        FastFileEditingSaveSnapshot snapshot,
        SavedDocumentState? savedDocumentState)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_gate)
        {
            ThrowIfDisposed();
            RequireNoCompiledMapSave();
            MarkRevisionSavedUnsafe(
                snapshot,
                savedDocumentState);
        }
    }

    /// <summary>
    /// Cancels outstanding session work and makes the session unavailable for
    /// further mutation. It is equivalent to disposing the session.
    /// </summary>
    public void Cancel() => Dispose();

    /// <summary>
    /// Cancels outstanding session work and releases detached draft state.
    /// Existing save snapshots remain independently usable.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _cancellation.Cancel();
            _drafts.Clear();
            _openDrafts.Clear();
            _selectedRow = null;
        }

        _cancellation.Dispose();
    }

    private DraftState<TDraft> GetOrCreateDraft<TDraft>(
        TargetZoneRowIdentity identity,
        ITargetZoneDraftAdapter<TDraft> adapter)
        where TDraft : notnull
    {
        WorkspaceAssetCatalogEntry entry = RequireEditableRow(identity);
        if (_drafts.TryGetValue(identity, out IEditingSessionDraftState? existing))
            return RequireDraftType(existing, adapter, identity);

        TargetZoneRowSource source = entry.TargetRow
            ?? throw new InvalidDataException(
                $"Editable target row {identity.SerializedIndex} has no detached source row.");
        TDraft baseline = RequireDraftValue(adapter.CreateBaseline(source), "baseline", identity);
        var state = new DraftState<TDraft>(identity, entry, adapter, baseline);
        _drafts.Add(identity, state);
        return state;
    }

    private DraftState<TDraft> GetExistingDraft<TDraft>(
        TargetZoneRowIdentity identity,
        ITargetZoneDraftAdapter<TDraft> adapter)
        where TDraft : notnull
    {
        if (!_drafts.TryGetValue(identity, out IEditingSessionDraftState? existing))
        {
            throw new InvalidOperationException(
                $"Target row {identity.SerializedIndex} has no detached draft. Open or mutate it first.");
        }

        return RequireDraftType(existing, adapter, identity);
    }

    private DraftState<TDraft> RequireDraftType<TDraft>(
        IEditingSessionDraftState state,
        ITargetZoneDraftAdapter<TDraft> adapter,
        TargetZoneRowIdentity identity)
        where TDraft : notnull
    {
        if (state is not DraftState<TDraft> typed || !ReferenceEquals(typed.Adapter, adapter))
        {
            throw new InvalidOperationException(
                $"Target row {identity.SerializedIndex} already has a draft bound to a different adapter or draft type.");
        }

        return typed;
    }

    private WorkspaceAssetCatalogEntry RequireEditableRow(TargetZoneRowIdentity identity)
    {
        WorkspaceAssetCatalogEntry entry = Document.GetRow(identity);
        WorkspaceAssetAccess expected = WorkspaceAssetAccessPolicy.Decide(
            entry.Origin,
            entry.ResolvedProvider is not null);
        if (entry.Access != expected ||
            entry.Access != WorkspaceAssetAccess.Editable ||
            entry.Origin != WorkspaceAssetOrigin.TargetOwnedDefinition ||
            entry.TargetRow?.AuthoredDefinition is null)
        {
            throw new InvalidOperationException(
                $"Target row {identity.SerializedIndex} is {entry.Access} ({entry.Origin}) and cannot be mutated.");
        }

        return entry;
    }

    private long NextRevision()
    {
        if (_revision == long.MaxValue)
            throw new InvalidOperationException("The editing-session revision cannot advance beyond Int64.MaxValue.");

        return ++_revision;
    }

    private void RebuildChangeSet()
    {
        _changeSet = new AssetChangeSet(_drafts.Values
            .Where(state => state.IsChanged)
            .Select(state => state.CreateChange()));
    }

    internal void SeedCapturedDraft<TDraft>(
        AssetRowChange change,
        ITargetZoneDraftAdapter<TDraft> adapter,
        TDraft capturedDraft)
        where TDraft : notnull
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(capturedDraft);

        lock (_gate)
        {
            ThrowIfDisposed();
            WorkspaceAssetCatalogEntry entry =
                RequireEditableRow(change.RowIdentity);
            if (entry.AssetType != change.SerializedType ||
                entry.Origin != change.Origin ||
                !string.Equals(
                    entry.OriginalName,
                    change.OriginalSerializedName,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Captured change metadata does not match target row " +
                    $"{change.RowIdentity.SerializedIndex}.");
            }
            if (_drafts.ContainsKey(change.RowIdentity))
            {
                throw new InvalidDataException(
                    $"The staging session already contains target row " +
                    $"{change.RowIdentity.SerializedIndex}.");
            }

            TDraft baseline = RequireDraftValue(
                adapter.CreateBaseline(entry.TargetRow!),
                "baseline",
                change.RowIdentity);
            var state = new DraftState<TDraft>(
                change.RowIdentity,
                entry,
                adapter,
                baseline);
            if (state.SemanticallyEqualsCurrent(capturedDraft))
            {
                throw new InvalidDataException(
                    $"Captured change for target row " +
                    $"{change.RowIdentity.SerializedIndex} is semantically " +
                    "equal to its immutable baseline.");
            }

            long revision = NextRevision();
            state.SetCurrent(capturedDraft, revision);
            _drafts.Add(change.RowIdentity, state);
            RebuildChangeSet();
        }
    }

    internal FastFileEditingSaveSnapshot CaptureForCompiledMapSave(
        Guid leaseId,
        long revision)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            RequireCompiledMapSaveLease(
                leaseId,
                revision);
            return CaptureForSaveUnsafe();
        }
    }

    internal void MarkCompiledMapRevisionSaved(
        Guid leaseId,
        long revision,
        FastFileEditingSaveSnapshot snapshot,
        SavedDocumentState savedDocumentState)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            RequireCompiledMapSaveLease(
                leaseId,
                revision);
            MarkRevisionSavedUnsafe(
                snapshot,
                savedDocumentState);
        }
    }

    internal void ReleaseCompiledMapSaveLease(
        Guid leaseId,
        long revision)
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            RequireCompiledMapSaveLease(
                leaseId,
                revision);
            _compiledMapSaveLeaseId = null;
        }
    }

    private FastFileEditingSaveSnapshot CaptureForSaveUnsafe()
    {
        var captures =
            new Dictionary<
                TargetZoneRowIdentity,
                IEditingSessionDraftCapture>();
        foreach (IEditingSessionDraftState state in
                 _drafts.Values.Where(state => state.IsChanged))
        {
            captures.Add(
                state.Identity,
                state.CaptureForSave());
        }

        return new FastFileEditingSaveSnapshot(
            _sessionId,
            Document.DocumentId,
            _revision,
            _changeSet,
            captures);
    }

    private void MarkRevisionSavedUnsafe(
        FastFileEditingSaveSnapshot snapshot,
        SavedDocumentState? savedDocumentState)
    {
        if (snapshot.SessionId != _sessionId ||
            snapshot.DocumentId != Document.DocumentId)
        {
            throw new InvalidOperationException(
                "The save snapshot does not belong to this editing session " +
                "and target document.");
        }
        if (snapshot.Revision > _revision)
        {
            throw new InvalidDataException(
                "A save acknowledgement cannot refer to a future document " +
                "revision.");
        }
        if (snapshot.Revision < _lastSavedRevision)
        {
            throw new InvalidOperationException(
                "A stale save acknowledgement cannot replace a newer saved " +
                "baseline.");
        }
        if (snapshot.Revision == _lastSavedRevision)
        {
            if (savedDocumentState is not null)
                _savedDocumentState = savedDocumentState;
            return;
        }

        foreach (AssetRowChange change in snapshot.ChangeSet.Changes)
        {
            if (!_drafts.TryGetValue(
                    change.RowIdentity,
                    out IEditingSessionDraftState? state) ||
                !snapshot.TryGetCapture(
                    change.RowIdentity,
                    out IEditingSessionDraftCapture? capture) ||
                capture is null)
            {
                throw new InvalidDataException(
                    "A save snapshot change no longer has matching detached " +
                    "draft provenance.");
            }

            state.AcknowledgeSavedBaseline(
                capture,
                snapshot.Revision);
        }

        _lastSavedRevision = snapshot.Revision;
        if (savedDocumentState is not null)
            _savedDocumentState = savedDocumentState;
        RebuildChangeSet();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FastFileEditingSession));
    }

    private void RequireNoCompiledMapSave()
    {
        if (_compiledMapSaveLeaseId is not null)
        {
            throw new InvalidOperationException(
                "Studio draft mutation and revert are unavailable while a " +
                "compiled-map save is in progress.");
        }
    }

    private void RequireCompiledMapSaveLease(
        Guid leaseId,
        long revision)
    {
        if (leaseId == Guid.Empty ||
            _compiledMapSaveLeaseId != leaseId)
        {
            throw new InvalidOperationException(
                "The compiled-map Studio save lease is no longer active.");
        }
        if (_revision != revision)
        {
            throw new InvalidDataException(
                $"The compiled-map Studio save lease captured revision " +
                $"{revision}, but the editing session is at {_revision}.");
        }
    }

    private static TDraft RequireDraftValue<TDraft>(
        TDraft draft,
        string role,
        TargetZoneRowIdentity identity)
        where TDraft : notnull
    {
        if (draft is null)
        {
            throw new InvalidDataException(
                $"Draft adapter produced a null {role} for target row {identity.SerializedIndex}.");
        }

        return draft;
    }

    private interface IEditingSessionDraftState
    {
        TargetZoneRowIdentity Identity { get; }
        Type DraftType { get; }
        bool IsChanged { get; }
        object CloneCurrentObject();
        bool SemanticallyEqualsCurrentObject(object candidate);
        void SetCurrentObject(object current, long revision);
        void RestoreSavedBaseline(long revision);
        AssetRowChange CreateChange();
        IEditingSessionDraftCapture CaptureForSave();
        void AcknowledgeSavedBaseline(IEditingSessionDraftCapture capture, long acknowledgedRevision);
    }

    private sealed class DraftState<TDraft> : IEditingSessionDraftState
        where TDraft : notnull
    {
        private readonly WorkspaceAssetCatalogEntry _entry;
        private TDraft _savedBaseline;
        private TDraft _current;
        private long? _firstChangedRevision;
        private long _lastMutationRevision;

        public DraftState(
            TargetZoneRowIdentity identity,
            WorkspaceAssetCatalogEntry entry,
            ITargetZoneDraftAdapter<TDraft> adapter,
            TDraft baseline)
        {
            Identity = identity;
            _entry = entry;
            Adapter = adapter;
            _savedBaseline = CloneRequired(baseline, "saved baseline");
            _current = CloneRequired(baseline, "current draft");
        }

        public TargetZoneRowIdentity Identity { get; }

        public ITargetZoneDraftAdapter<TDraft> Adapter { get; }

        public Type DraftType =>
            Adapter is IDeclaredDraftTypeAdapter declared
                ? declared.DraftType
                : typeof(TDraft);

        public bool IsChanged => !Adapter.SemanticallyEquals(_savedBaseline, _current);

        public TDraft CloneCurrent() => CloneRequired(_current, "current draft");

        public object CloneCurrentObject() => CloneCurrent();

        public bool SemanticallyEqualsCurrent(TDraft candidate) =>
            Adapter.SemanticallyEquals(_current, candidate);

        public bool SemanticallyEqualsCurrentObject(object candidate) =>
            SemanticallyEqualsCurrent(
                RequireObjectType(candidate, "comparison draft"));

        public void SetCurrent(TDraft current, long revision)
        {
            _current = CloneRequired(current, "current draft");
            _lastMutationRevision = revision;
            UpdateChangeBounds(revision);
        }

        public void SetCurrentObject(object current, long revision) =>
            SetCurrent(
                RequireObjectType(current, "replacement draft"),
                revision);

        public void RestoreSavedBaseline(long revision)
        {
            _current = CloneRequired(_savedBaseline, "current draft");
            _lastMutationRevision = revision;
            UpdateChangeBounds(revision);
        }

        public AssetRowChange CreateChange()
        {
            if (!IsChanged || _firstChangedRevision is not { } firstChanged)
            {
                throw new InvalidOperationException(
                    $"Target row {Identity.SerializedIndex} has no semantic change to describe.");
            }

            return new AssetRowChange(
                Identity,
                _entry.AssetType,
                _entry.OriginalName,
                _entry.Origin,
                firstChanged,
                _lastMutationRevision);
        }

        public IEditingSessionDraftCapture CaptureForSave() =>
            new EditingSessionDraftCapture<TDraft>(Adapter, CloneRequired(_current, "save snapshot"));

        public void AcknowledgeSavedBaseline(
            IEditingSessionDraftCapture capture,
            long acknowledgedRevision)
        {
            if (capture is not EditingSessionDraftCapture<TDraft> typed ||
                !ReferenceEquals(typed.Adapter, Adapter))
            {
                throw new InvalidDataException(
                    $"Save snapshot draft provenance does not match target row {Identity.SerializedIndex}.");
            }

            _savedBaseline = CloneRequired(typed.CloneDraft(), "acknowledged saved baseline");
            if (IsChanged)
            {
                if (_lastMutationRevision <= acknowledgedRevision)
                {
                    throw new InvalidDataException(
                        $"Target row {Identity.SerializedIndex} differs after acknowledgement without a later mutation.");
                }

                _firstChangedRevision = _lastMutationRevision;
            }
            else
            {
                _firstChangedRevision = null;
            }
        }

        private void UpdateChangeBounds(long revision)
        {
            if (IsChanged)
            {
                _firstChangedRevision ??= revision;
            }
            else
            {
                _firstChangedRevision = null;
            }
        }

        private TDraft CloneRequired(TDraft draft, string role)
        {
            TDraft clone = Adapter.Clone(draft);
            return clone is null
                ? throw new InvalidDataException(
                    $"Draft adapter returned null while cloning the {role} for target row {Identity.SerializedIndex}.")
                : clone;
        }

        private static TDraft RequireObjectType(
            object value,
            string role) =>
            value is TDraft typed
                ? typed
                : throw new InvalidDataException(
                    $"Detached {role} type " +
                    $"'{value?.GetType().FullName ?? "null"}' does not match " +
                    $"'{typeof(TDraft).FullName}'.");
    }
}

internal interface IEditingSessionDraftCapture
{
    Type DraftType { get; }
    object CloneObject();
    void SeedInto(
        FastFileEditingSession target,
        AssetRowChange change);
}

internal sealed class EditingSessionDraftCapture<TDraft> : IEditingSessionDraftCapture
    where TDraft : notnull
{
    private readonly ITargetZoneDraftAdapter<TDraft> _adapter;
    private readonly TDraft _draft;

    public EditingSessionDraftCapture(
        ITargetZoneDraftAdapter<TDraft> adapter,
        TDraft draft)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _draft = draft is null
            ? throw new ArgumentNullException(nameof(draft))
            : _adapter.Clone(draft);
    }

    public Type DraftType =>
        _adapter is IDeclaredDraftTypeAdapter declared
            ? declared.DraftType
            : typeof(TDraft);

    public ITargetZoneDraftAdapter<TDraft> Adapter => _adapter;

    public TDraft CloneDraft()
    {
        TDraft clone = _adapter.Clone(_draft);
        return clone is null
            ? throw new InvalidDataException("Draft adapter returned null while cloning a save snapshot draft.")
            : clone;
    }

    public object CloneObject() => CloneDraft();

    public void SeedInto(
        FastFileEditingSession target,
        AssetRowChange change)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(change);
        target.SeedCapturedDraft(
            change,
            _adapter,
            CloneDraft());
    }
}
