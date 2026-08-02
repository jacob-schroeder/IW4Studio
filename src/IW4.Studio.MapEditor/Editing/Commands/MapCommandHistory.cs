using System.Collections.ObjectModel;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.SavePlanning;

namespace IW4.Studio.MapEditor.Editing.Commands;

public enum MapCommandTransition
{
    Applied,
    Undone,
    Redone,
    NoChange,
    SavedRevisionAcknowledged
}

public enum MapCommandJournalDirection
{
    Apply,
    Revert
}

public sealed record MapCommandExecutionResult(
    MapCommandTransition Transition,
    long PreviousRevision,
    long Revision,
    IMapEditCommand? Command)
{
    public bool Changed => Revision != PreviousRevision;
}

public sealed record MapCommandJournalEntry(
    IMapEditCommand Command,
    MapPendingEdit Edit,
    MapCommandJournalDirection Direction);

public sealed class MapDocumentChangedEventArgs : EventArgs
{
    private readonly IReadOnlyList<MapObjectId> _changedObjects;

    internal MapDocumentChangedEventArgs(
        MapCommandTransition transition,
        long previousRevision,
        long revision,
        IMapEditCommand? command,
        IEnumerable<MapObjectId> changedObjects)
    {
        ArgumentNullException.ThrowIfNull(changedObjects);
        Transition = transition;
        PreviousRevision = previousRevision;
        Revision = revision;
        Command = command;
        _changedObjects = new ReadOnlyCollection<MapObjectId>(
            changedObjects.Distinct().ToArray());
    }

    public MapCommandTransition Transition { get; }
    public long PreviousRevision { get; }
    public long Revision { get; }
    public IMapEditCommand? Command { get; }
    public IReadOnlyList<MapObjectId> ChangedObjects => _changedObjects;
}

/// <summary>
/// Owns the document's reversible state graph. Revisions are monotonic
/// transition numbers, while state identities allow undo/redo and save
/// acknowledgements to determine dirty state without conflating the two.
/// </summary>
public sealed class MapCommandHistory
{
    private readonly object _gate = new();
    private readonly EditorMapDocument _document;
    private readonly StateNode _root;
    private readonly Stack<StateNode> _redo = new();
    private readonly Dictionary<long, StateNode> _statesByRevision = [];
    private readonly Dictionary<long, IReadOnlyList<MapObjectId>>
        _changedObjectsByRevision = [];
    private readonly HashSet<MapEditCommandId> _acceptedCommandIds = [];
    private StateNode _current;
    private StateNode _saved;
    private long _revision;
    private long _savedRevision;
    private Guid? _compiledSaveLeaseId;
    private bool _transitionInProgress;

    internal MapCommandHistory(EditorMapDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _root = new StateNode(
            parent: null,
            edit: null);
        _current = _root;
        _saved = _root;
        _statesByRevision.Add(0, _root);
    }

    public long Revision
    {
        get
        {
            lock (_gate)
                return _revision;
        }
    }

    public long SavedRevision
    {
        get
        {
            lock (_gate)
                return _savedRevision;
        }
    }

    public bool IsDirty
    {
        get
        {
            lock (_gate)
                return !ReferenceEquals(_current, _saved);
        }
    }

    public bool CanUndo
    {
        get
        {
            lock (_gate)
                return CanMutateUnsafe &&
                       _current.Parent is not null;
        }
    }

    public bool CanRedo
    {
        get
        {
            lock (_gate)
                return CanMutateUnsafe &&
                       _redo.Count != 0;
        }
    }

    public bool IsCompiledSaveInProgress
    {
        get
        {
            lock (_gate)
                return _compiledSaveLeaseId is not null;
        }
    }

    public bool CanMutate
    {
        get
        {
            lock (_gate)
                return CanMutateUnsafe;
        }
    }

    /// <summary>
    /// Commands currently applied to the semantic document, ordered from the
    /// imported baseline to the current state. Undone commands are excluded.
    /// </summary>
    public IReadOnlyList<IMapEditCommand> ActiveCommands
    {
        get
        {
            lock (_gate)
            {
                return Array.AsReadOnly(
                    GetPath(_current)
                        .Select(node => (IMapEditCommand)node.Edit!.Command)
                        .ToArray());
            }
        }
    }

    public IReadOnlyList<MapPendingEdit> AppliedEdits
    {
        get
        {
            lock (_gate)
            {
                return Array.AsReadOnly(
                    GetPath(_current)
                        .Select(node => node.Edit!.PendingEdit)
                        .ToArray());
            }
        }
    }

    /// <summary>
    /// Ordered semantic transitions between the last acknowledged saved state
    /// and the current state. Entries may be reversals when the user undoes
    /// past the saved state or branches from it.
    /// </summary>
    public IReadOnlyList<MapCommandJournalEntry> PendingJournal
    {
        get
        {
            lock (_gate)
                return Array.AsReadOnly(GetPendingJournal());
        }
    }

    public IReadOnlyList<MapPendingEdit> PendingEdits
    {
        get
        {
            lock (_gate)
            {
                return Array.AsReadOnly(
                    GetPendingJournal()
                        .Select(entry => entry.Edit)
                        .ToArray());
            }
        }
    }

    public IReadOnlyList<MapPendingEdit> SerializedPendingEdits
    {
        get
        {
            lock (_gate)
            {
                return Array.AsReadOnly(
                    GetPendingJournal()
                        .Where(entry =>
                            entry.Edit.Kind != MapEditKind.EditorOnly)
                        .Select(entry => entry.Edit)
                        .ToArray());
            }
        }
    }

    public MapCommandExecutionResult Execute(IMapEditCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command is not MapEditCommand executable)
        {
            throw new ArgumentException(
                "Only the closed IW4 map-command taxonomy can mutate an editor document.",
                nameof(command));
        }

        MapDocumentChangedEventArgs? change = null;
        MapCommandExecutionResult result;
        lock (_gate)
        {
            RequireNoTransition();
            RequireMutationAllowed();
            _transitionInProgress = true;
            try
            {
                if (_acceptedCommandIds.Contains(executable.Id))
                {
                    throw new InvalidOperationException(
                        $"Map edit command {executable.Id} has already been accepted by this history.");
                }

                PreparedMapEdit prepared = executable.Prepare(_document);
                if (prepared.IsNoChange)
                {
                    return new MapCommandExecutionResult(
                        MapCommandTransition.NoChange,
                        _revision,
                        _revision,
                        executable);
                }

                long nextRevision = checked(_revision + 1);
                prepared.Apply();

                var next = new StateNode(
                    _current,
                    prepared);
                _redo.Clear();
                _acceptedCommandIds.Add(executable.Id);
                _current = next;
                _revision = nextRevision;
                _statesByRevision.Add(_revision, _current);
                _changedObjectsByRevision.Add(
                    _revision,
                    Array.AsReadOnly(executable.TargetObjects.ToArray()));

                result = new MapCommandExecutionResult(
                    MapCommandTransition.Applied,
                    nextRevision - 1,
                    nextRevision,
                    executable);
                change = CreateChange(result, executable.TargetObjects);
            }
            finally
            {
                _transitionInProgress = false;
            }
        }

        _document.PublishChanged(change);
        return result;
    }

    public MapCommandExecutionResult Undo()
    {
        MapDocumentChangedEventArgs change;
        MapCommandExecutionResult result;
        lock (_gate)
        {
            RequireNoTransition();
            RequireMutationAllowed();
            if (_current.Parent is null)
            {
                return new MapCommandExecutionResult(
                    MapCommandTransition.NoChange,
                    _revision,
                    _revision,
                    Command: null);
            }

            _transitionInProgress = true;
            try
            {
                long nextRevision = checked(_revision + 1);
                StateNode undone = _current;
                undone.Edit!.Revert();
                _current = undone.Parent;
                _redo.Push(undone);
                long previousRevision = _revision;
                _revision = nextRevision;
                _statesByRevision.Add(_revision, _current);
                _changedObjectsByRevision.Add(
                    _revision,
                    Array.AsReadOnly(
                        undone.Edit.Command.TargetObjects.ToArray()));

                result = new MapCommandExecutionResult(
                    MapCommandTransition.Undone,
                    previousRevision,
                    nextRevision,
                    undone.Edit.Command);
                change = CreateChange(
                    result,
                    undone.Edit.Command.TargetObjects);
            }
            finally
            {
                _transitionInProgress = false;
            }
        }

        _document.PublishChanged(change);
        return result;
    }

    public MapCommandExecutionResult Redo()
    {
        MapDocumentChangedEventArgs change;
        MapCommandExecutionResult result;
        lock (_gate)
        {
            RequireNoTransition();
            RequireMutationAllowed();
            if (_redo.Count == 0)
            {
                return new MapCommandExecutionResult(
                    MapCommandTransition.NoChange,
                    _revision,
                    _revision,
                    Command: null);
            }

            _transitionInProgress = true;
            try
            {
                long nextRevision = checked(_revision + 1);
                StateNode redone = _redo.Peek();
                if (!ReferenceEquals(redone.Parent, _current))
                {
                    throw new InvalidOperationException(
                        "The redo state does not descend from the current semantic state.");
                }

                redone.Edit!.Apply();
                _redo.Pop();
                _current = redone;
                long previousRevision = _revision;
                _revision = nextRevision;
                _statesByRevision.Add(_revision, _current);
                _changedObjectsByRevision.Add(
                    _revision,
                    Array.AsReadOnly(
                        redone.Edit.Command.TargetObjects.ToArray()));

                result = new MapCommandExecutionResult(
                    MapCommandTransition.Redone,
                    previousRevision,
                    nextRevision,
                    redone.Edit.Command);
                change = CreateChange(
                    result,
                    redone.Edit.Command.TargetObjects);
            }
            finally
            {
                _transitionInProgress = false;
            }
        }

        _document.PublishChanged(change);
        return result;
    }

    /// <summary>
    /// Marks the semantic state captured at <paramref name="revision"/> as the
    /// persisted state. Later edits remain dirty; acknowledging a stale or
    /// unknown revision fails closed.
    /// </summary>
    public void AcknowledgeSavedRevision(long revision)
    {
        MapDocumentChangedEventArgs change;
        lock (_gate)
        {
            RequireNoTransition();
            if (!_statesByRevision.TryGetValue(revision, out StateNode? state))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(revision),
                    revision,
                    "The revision was not produced by this document history.");
            }

            _saved = state;
            _savedRevision = revision;
            change = new MapDocumentChangedEventArgs(
                MapCommandTransition.SavedRevisionAcknowledged,
                _revision,
                _revision,
                command: null,
                changedObjects: []);
        }

        _document.PublishChanged(change);
    }

    public void AcknowledgeCurrentRevisionSaved() =>
        AcknowledgeSavedRevision(Revision);

    public IReadOnlyList<MapObjectId> GetChangedObjectsSince(long revision)
    {
        lock (_gate)
        {
            if (revision < 0 || revision > _revision)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(revision),
                    revision,
                    $"Revision must be between 0 and {_revision}.");
            }

            var changed = new HashSet<MapObjectId>();
            for (long value = revision + 1; value <= _revision; value++)
            {
                foreach (MapObjectId objectId in
                         _changedObjectsByRevision[value])
                {
                    changed.Add(objectId);
                }
            }

            return Array.AsReadOnly(changed.ToArray());
        }
    }

    internal T ReadConsistent<T>(Func<long, T> reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        lock (_gate)
            return reader(_revision);
    }

    internal MapCompiledSaveLease AcquireCompiledSaveLease()
    {
        Guid leaseId;
        long revision;
        lock (_gate)
        {
            RequireNoTransition();
            if (_document.RequiresReopen)
            {
                throw new InvalidOperationException(
                    "This compiled-map baseline has already produced a " +
                    "committed output. Reopen that output before editing or " +
                    "saving again.");
            }
            if (_compiledSaveLeaseId is not null)
            {
                throw new InvalidOperationException(
                    "A compiled-map save is already in progress for this " +
                    "document.");
            }

            leaseId = Guid.NewGuid();
            revision = _revision;
            _compiledSaveLeaseId = leaseId;
        }

        _document.PublishPersistenceStateChanged();
        return new MapCompiledSaveLease(
            this,
            leaseId,
            _document.Id,
            revision);
    }

    internal void CommitCompiledSaveLease(
        Guid leaseId,
        long revision)
    {
        MapDocumentChangedEventArgs change;
        lock (_gate)
        {
            RequireCompiledSaveLease(leaseId, revision);
            if (!_statesByRevision.TryGetValue(
                    revision,
                    out StateNode? state))
            {
                throw new InvalidDataException(
                    "The compiled-save lease revision is no longer present " +
                    "in the document history.");
            }

            _saved = state;
            _savedRevision = revision;
            _document.SetCompiledOutputRequiresReopen();
            change = new MapDocumentChangedEventArgs(
                MapCommandTransition.SavedRevisionAcknowledged,
                _revision,
                _revision,
                command: null,
                changedObjects: []);
        }

        _document.PublishChanged(change);
        _document.PublishPersistenceStateChanged();
    }

    internal void ReleaseCompiledSaveLease(
        Guid leaseId,
        long revision)
    {
        lock (_gate)
        {
            RequireCompiledSaveLease(leaseId, revision);
            _compiledSaveLeaseId = null;
        }

        _document.PublishPersistenceStateChanged();
    }

    private MapCommandJournalEntry[] GetPendingJournal()
    {
        List<StateNode> savedPath = GetPath(_saved);
        List<StateNode> currentPath = GetPath(_current);
        int commonCount = 0;
        while (commonCount < savedPath.Count &&
               commonCount < currentPath.Count &&
               ReferenceEquals(
                   savedPath[commonCount],
                   currentPath[commonCount]))
        {
            commonCount++;
        }

        var entries = new List<MapCommandJournalEntry>(
            (savedPath.Count - commonCount) +
            (currentPath.Count - commonCount));
        for (int index = savedPath.Count - 1;
             index >= commonCount;
             index--)
        {
            PreparedMapEdit edit = savedPath[index].Edit!;
            entries.Add(new MapCommandJournalEntry(
                edit.Command,
                edit.PendingEdit,
                MapCommandJournalDirection.Revert));
        }

        for (int index = commonCount; index < currentPath.Count; index++)
        {
            PreparedMapEdit edit = currentPath[index].Edit!;
            entries.Add(new MapCommandJournalEntry(
                edit.Command,
                edit.PendingEdit,
                MapCommandJournalDirection.Apply));
        }

        return entries.ToArray();
    }

    private static List<StateNode> GetPath(StateNode state)
    {
        var path = new List<StateNode>();
        for (StateNode? current = state;
             current?.Parent is not null;
             current = current.Parent)
        {
            path.Add(current);
        }

        path.Reverse();
        return path;
    }

    private static MapDocumentChangedEventArgs CreateChange(
        MapCommandExecutionResult result,
        IEnumerable<MapObjectId> changedObjects) =>
        new(
            result.Transition,
            result.PreviousRevision,
            result.Revision,
            result.Command,
            changedObjects);

    private void RequireNoTransition()
    {
        if (_transitionInProgress)
        {
            throw new InvalidOperationException(
                "Map command history does not allow nested transitions.");
        }
    }

    private bool CanMutateUnsafe =>
        _compiledSaveLeaseId is null &&
        !_document.RequiresReopen;

    private void RequireMutationAllowed()
    {
        if (_compiledSaveLeaseId is not null)
        {
            throw new InvalidOperationException(
                "Map edits, undo, and redo are unavailable while a " +
                "compiled-map save is in progress.");
        }
        if (_document.RequiresReopen)
        {
            throw new InvalidOperationException(
                "Map edits, undo, and redo require reopening the committed " +
                "compiled-map output.");
        }
    }

    private void RequireCompiledSaveLease(
        Guid leaseId,
        long revision)
    {
        if (leaseId == Guid.Empty ||
            _compiledSaveLeaseId != leaseId)
        {
            throw new InvalidOperationException(
                "The compiled-save lease is no longer active for this " +
                "document.");
        }
        if (_revision != revision)
        {
            throw new InvalidDataException(
                $"The compiled-save lease captured map revision {revision}, " +
                $"but the document is at {_revision}.");
        }
    }

    private sealed class StateNode
    {
        public StateNode(
            StateNode? parent,
            PreparedMapEdit? edit)
        {
            Parent = parent;
            Edit = edit;
        }

        public StateNode? Parent { get; }
        public PreparedMapEdit? Edit { get; }
    }
}
