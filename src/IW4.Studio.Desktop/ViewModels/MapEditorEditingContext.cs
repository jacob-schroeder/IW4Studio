using IW4.Studio.MapEditor.Editing.Commands;
using IW4.Studio.MapEditor.Editing.Collision;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.MapEntsSyntax;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.Compilation.Collision;

namespace IW4.Studio.Desktop.ViewModels;

/// <summary>
/// Stable identity for one pending UI field edit. The syntax ordinal, rather
/// than a key lookup, keeps duplicate MapEnt keys independently addressable.
/// </summary>
public readonly record struct MapEntityPropertyDraftId(
    MapObjectId EntityId,
    MapEntPropertyOrdinal PropertyOrdinal,
    MapEntPropertyField Field);

public sealed record MapEntityPropertyDraftCommitResult(
    bool Succeeded,
    int CommittedFieldCount,
    IReadOnlyList<string> Diagnostics)
{
    public static MapEntityPropertyDraftCommitResult Success(
        int committedFieldCount) =>
        new(
            Succeeded: true,
            committedFieldCount,
            Diagnostics: []);

    public static MapEntityPropertyDraftCommitResult Failure(
        IEnumerable<string> diagnostics) =>
        new(
            Succeeded: false,
            CommittedFieldCount: 0,
            Array.AsReadOnly(
                diagnostics
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()));
}

/// <summary>
/// One unapplied viewport translation. The committed origin remains the
/// semantic document value; the candidate is renderer-only until the user
/// explicitly applies it as one map-edit command.
/// </summary>
public sealed record StaticModelTranslationDraftState(
    MapObjectId ObjectId,
    int SourceOrdinal,
    MapVector3 CommittedOrigin,
    MapVector3 CandidateOrigin);

/// <summary>
/// One unapplied authored-collision translation. Both values are immutable
/// canonical source objects. The candidate is editor state only until Apply
/// emits one <see cref="ReplaceAuthoredCollisionSourceCommand"/>.
/// </summary>
public sealed record AuthoredCollisionTranslationDraftState(
    MapObjectId ObjectId,
    AuthoredCollisionSource CommittedSource,
    AuthoredCollisionSource CandidateSource)
{
    public MapVector3 CommittedOrigin =>
        AuthoredCollisionSourceTransforms.GetTranslationAnchor(
            CommittedSource);

    public MapVector3 CandidateOrigin =>
        AuthoredCollisionSourceTransforms.GetTranslationAnchor(
            CandidateSource);

    public MapBounds CandidateBounds =>
        AuthoredCollisionSourceTransforms.GetBounds(CandidateSource);
}

/// <summary>
/// Document-scoped Desktop editing state. It owns text drafts independently
/// from any inspector or window instance and projects the document's
/// authoritative compiled-save mutation boundary into the UI.
/// </summary>
public sealed class MapEditorEditingContext : IDisposable
{
    private readonly object _gate = new();
    private readonly EditorMapDocument _document;
    private readonly Dictionary<MapEntityPropertyDraftId, string> _drafts = [];
    private readonly Dictionary<MapEntityPropertyDraftId, string>
        _validationMessages = [];
    private StaticModelTranslationDraftState? _staticModelTranslationDraft;
    private AuthoredCollisionTranslationDraftState?
        _authoredCollisionTranslationDraft;
    private bool _disposed;

    public MapEditorEditingContext(EditorMapDocument document)
    {
        _document = document ??
            throw new ArgumentNullException(nameof(document));
        _document.Changed += Document_Changed;
        _document.PersistenceStateChanged +=
            Document_PersistenceStateChanged;
    }

    public event EventHandler? StateChanged;

    public EditorMapDocument Document => _document;

    public bool IsCompiledSaveInProgress =>
        _document.IsCompiledSaveInProgress;

    public bool AreMutationsAllowed =>
        _document.CanMutate;

    public bool HasPropertyDrafts
    {
        get
        {
            lock (_gate)
                return _drafts.Count != 0;
        }
    }

    public int PropertyDraftCount
    {
        get
        {
            lock (_gate)
                return _drafts.Count;
        }
    }

    public bool HasStaticModelTranslationDraft
    {
        get
        {
            lock (_gate)
                return _staticModelTranslationDraft is not null;
        }
    }

    public StaticModelTranslationDraftState? StaticModelTranslationDraft
    {
        get
        {
            lock (_gate)
                return _staticModelTranslationDraft;
        }
    }

    public bool HasAuthoredCollisionTranslationDraft
    {
        get
        {
            lock (_gate)
                return _authoredCollisionTranslationDraft is not null;
        }
    }

    public AuthoredCollisionTranslationDraftState?
        AuthoredCollisionTranslationDraft
    {
        get
        {
            lock (_gate)
                return _authoredCollisionTranslationDraft;
        }
    }

    /// <summary>
    /// Translation is a single contextual interaction domain. Requiring one
    /// active target prevents an invisible draft from surviving selection
    /// changes or being committed by the wrong tool.
    /// </summary>
    public bool HasTranslationDraft =>
        HasStaticModelTranslationDraft ||
        HasAuthoredCollisionTranslationDraft;

    public int UnsavedChangeCount => checked(
        _document.History.PendingJournal.Count +
        PropertyDraftCount +
        (HasTranslationDraft ? 1 : 0));

    public bool HasUnsavedChanges =>
        UnsavedChangeCount != 0;

    public void SetStaticModelTranslationDraft(
        EditorStaticModel model,
        MapVector3 candidateOrigin)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!candidateOrigin.IsFinite)
        {
            throw new ArgumentOutOfRangeException(
                nameof(candidateOrigin),
                "A static-model translation draft must be finite.");
        }

        EditorStaticModel owned = GetOwnedRenderStaticModel(model);
        bool changed;
        lock (_gate)
        {
            ThrowIfDisposed();
            EnsureMutationsAllowed();
            StaticModelTranslationDraftState? current =
                _staticModelTranslationDraft;
            if (_authoredCollisionTranslationDraft is not null)
            {
                throw new InvalidOperationException(
                    "Apply or cancel the active authored-collision move " +
                    "before moving a static model.");
            }
            if (current is not null &&
                current.ObjectId != owned.Id)
            {
                throw new InvalidOperationException(
                    "Apply or cancel the active static-model move before " +
                    "moving another object.");
            }

            MapVector3 committedOrigin =
                current?.CommittedOrigin ?? owned.Transform.Origin;
            if (SameExact(candidateOrigin, committedOrigin))
            {
                changed = _staticModelTranslationDraft is not null;
                _staticModelTranslationDraft = null;
            }
            else
            {
                var replacement = new StaticModelTranslationDraftState(
                    owned.Id,
                    owned.SourceOrdinal.Value,
                    committedOrigin,
                    candidateOrigin);
                changed = current != replacement;
                _staticModelTranslationDraft = replacement;
            }
        }

        if (changed)
            StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetAuthoredCollisionTranslationDraft(
        EditorAuthoredCollisionObject authored,
        MapVector3 candidateOrigin)
    {
        ArgumentNullException.ThrowIfNull(authored);
        if (!candidateOrigin.IsFinite)
        {
            throw new ArgumentOutOfRangeException(
                nameof(candidateOrigin),
                "An authored-collision translation draft must be finite.");
        }

        EditorAuthoredCollisionObject owned =
            GetOwnedAuthoredCollision(authored);
        bool changed;
        lock (_gate)
        {
            ThrowIfDisposed();
            EnsureMutationsAllowed();
            if (_staticModelTranslationDraft is not null)
            {
                throw new InvalidOperationException(
                    "Apply or cancel the active static-model move before " +
                    "moving authored collision.");
            }

            AuthoredCollisionTranslationDraftState? current =
                _authoredCollisionTranslationDraft;
            if (current is not null && current.ObjectId != owned.Id)
            {
                throw new InvalidOperationException(
                    "Apply or cancel the active authored-collision move " +
                    "before moving another object.");
            }

            AuthoredCollisionSource committed =
                current?.CommittedSource ?? owned.Source;
            MapVector3 committedOrigin =
                AuthoredCollisionSourceTransforms.GetTranslationAnchor(
                    committed);
            if (SameExact(candidateOrigin, committedOrigin))
            {
                changed =
                    _authoredCollisionTranslationDraft is not null;
                _authoredCollisionTranslationDraft = null;
            }
            else
            {
                AuthoredCollisionSource candidate =
                    AuthoredCollisionSourceTransforms.Translate(
                        committed,
                        candidateOrigin - committedOrigin);
                var replacement =
                    new AuthoredCollisionTranslationDraftState(
                        owned.Id,
                        committed,
                        candidate);
                changed =
                    current is null ||
                    !SameExact(
                        current.CandidateOrigin,
                        replacement.CandidateOrigin);
                _authoredCollisionTranslationDraft = replacement;
            }
        }

        if (changed)
            StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearAuthoredCollisionTranslationDraft(
        MapObjectId objectId)
    {
        bool changed;
        lock (_gate)
        {
            ThrowIfDisposed();
            changed =
                _authoredCollisionTranslationDraft?.ObjectId == objectId;
            if (changed)
                _authoredCollisionTranslationDraft = null;
        }

        if (changed)
            StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearStaticModelTranslationDraft(MapObjectId objectId)
    {
        bool changed;
        lock (_gate)
        {
            ThrowIfDisposed();
            changed =
                _staticModelTranslationDraft?.ObjectId == objectId;
            if (changed)
                _staticModelTranslationDraft = null;
        }

        if (changed)
            StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public string ReadPropertyField(
        EditorEntity entity,
        MapEntPropertyOrdinal propertyOrdinal,
        MapEntPropertyField field)
    {
        EditorEntity owned = GetOwnedEntity(entity);
        ValidateField(field);
        var id = new MapEntityPropertyDraftId(
            owned.Id,
            propertyOrdinal,
            field);
        lock (_gate)
        {
            ThrowIfDisposed();
            return _drafts.TryGetValue(id, out string? draft)
                ? draft
                : ReadCommittedField(
                    owned,
                    propertyOrdinal,
                    field);
        }
    }

    public string? ReadValidationMessage(
        EditorEntity entity,
        MapEntPropertyOrdinal propertyOrdinal)
    {
        EditorEntity owned = GetOwnedEntity(entity);
        lock (_gate)
        {
            ThrowIfDisposed();
            string[] messages = Enum
                .GetValues<MapEntPropertyField>()
                .Select(field => new MapEntityPropertyDraftId(
                    owned.Id,
                    propertyOrdinal,
                    field))
                .Where(_validationMessages.ContainsKey)
                .Select(id => _validationMessages[id])
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return messages.Length == 0
                ? null
                : string.Join(" ", messages);
        }
    }

    public void SetPropertyDraft(
        EditorEntity entity,
        MapEntPropertyOrdinal propertyOrdinal,
        MapEntPropertyField field,
        string replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        EditorEntity owned = GetOwnedEntity(entity);
        ValidateField(field);
        var id = new MapEntityPropertyDraftId(
            owned.Id,
            propertyOrdinal,
            field);
        bool changed;
        lock (_gate)
        {
            ThrowIfDisposed();
            EnsureMutationsAllowed();
            string committed = ReadCommittedField(
                owned,
                propertyOrdinal,
                field);
            changed = string.Equals(
                committed,
                replacement,
                StringComparison.Ordinal)
                ? _drafts.Remove(id)
                : SetDictionaryValue(_drafts, id, replacement);
            changed |= _validationMessages.Remove(id);
        }

        if (changed)
            StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Validates all UI drafts against a projected syntax snapshot, then emits
    /// only typed MapEnt commands immediately before Save As captures the
    /// authoritative document revision.
    /// </summary>
    public MapEntityPropertyDraftCommitResult CommitPropertyDrafts()
    {
        KeyValuePair<MapEntityPropertyDraftId, string>[] snapshot;
        lock (_gate)
        {
            ThrowIfDisposed();
            EnsureMutationsAllowed();
            snapshot = _drafts
                .OrderBy(value => GetEntityOrdinal(value.Key.EntityId))
                .ThenBy(value => value.Key.PropertyOrdinal.Value)
                .ThenBy(value => value.Key.Field)
                .ToArray();
        }

        if (snapshot.Length == 0)
            return MapEntityPropertyDraftCommitResult.Success(0);

        IReadOnlyList<DraftValidationFailure> failures =
            ValidateDrafts(snapshot);
        if (failures.Count != 0)
        {
            lock (_gate)
            {
                foreach (MapEntityPropertyDraftId id in snapshot.Select(
                             value => value.Key))
                {
                    _validationMessages.Remove(id);
                }
                foreach (DraftValidationFailure failure in failures)
                    _validationMessages[failure.Id] = failure.Message;
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
            return MapEntityPropertyDraftCommitResult.Failure(
                failures.Select(value => value.Message));
        }

        lock (_gate)
        {
            foreach (MapEntityPropertyDraftId id in snapshot.Select(
                         value => value.Key))
            {
                _validationMessages.Remove(id);
            }
        }

        int committedCount = 0;
        try
        {
            foreach ((MapEntityPropertyDraftId id, string replacement)
                     in snapshot)
            {
                EditorEntity entity = GetRequiredEntity(id.EntityId);
                string current = ReadCommittedField(
                    entity,
                    id.PropertyOrdinal,
                    id.Field);
                if (string.Equals(
                        current,
                        replacement,
                        StringComparison.Ordinal))
                {
                    RemoveDraft(id);
                    continue;
                }

                _document.History.Execute(
                    new SetMapEntityPropertyCommand(
                        _document,
                        id.EntityId,
                        id.PropertyOrdinal,
                        id.Field,
                        replacement));
                committedCount++;
            }
        }
        catch (Exception exception)
            when (exception is not OutOfMemoryException)
        {
            string diagnostic =
                $"Could not commit a MapEnt property draft: {exception.Message}";
            StateChanged?.Invoke(this, EventArgs.Empty);
            return new MapEntityPropertyDraftCommitResult(
                Succeeded: false,
                committedCount,
                [diagnostic]);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        return MapEntityPropertyDraftCommitResult.Success(committedCount);
    }

    public void EnsureMutationsAllowed()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_document.RequiresReopen)
            {
                throw new InvalidOperationException(
                    "The saved map output must be reopened before further editing.");
            }
            if (_document.IsCompiledSaveInProgress)
            {
                throw new InvalidOperationException(
                    "Map editing is unavailable while a compiled Save As is in progress.");
            }
            if (!_document.CanMutate)
            {
                throw new InvalidOperationException(
                    "The map document is not currently available for editing.");
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _drafts.Clear();
            _validationMessages.Clear();
            _staticModelTranslationDraft = null;
            _authoredCollisionTranslationDraft = null;
        }
        _document.Changed -= Document_Changed;
        _document.PersistenceStateChanged -=
            Document_PersistenceStateChanged;
    }

    private IReadOnlyList<DraftValidationFailure> ValidateDrafts(
        IReadOnlyList<KeyValuePair<MapEntityPropertyDraftId, string>>
            snapshot)
    {
        EditorMapEntitySource source = _document.EntitySource ??
            throw new InvalidOperationException(
                "The editor document has no byte-authoritative MapEnt source.");
        MapEntsSyntaxDocument projected = source.Syntax;
        var failures = new List<DraftValidationFailure>();
        foreach ((MapEntityPropertyDraftId id, string replacement)
                 in snapshot)
        {
            try
            {
                EditorEntity entity = GetRequiredEntity(id.EntityId);
                MapEntsPropertyEdit edit =
                    projected.PreparePropertyReplacement(
                        entity.SyntaxOrdinal,
                        id.PropertyOrdinal,
                        id.Field,
                        replacement);
                projected = edit.After;
            }
            catch (Exception exception)
                when (exception is
                      MapEntsEditRejectedException or
                      ArgumentOutOfRangeException or
                      InvalidOperationException)
            {
                failures.Add(new DraftValidationFailure(
                    id,
                    $"{FormatDraft(id)}: {exception.Message}"));
            }
        }

        return failures;
    }

    private void Document_Changed(
        object? sender,
        MapDocumentChangedEventArgs e)
    {
        bool changed = false;
        lock (_gate)
        {
            if (_disposed)
                return;

            foreach ((MapEntityPropertyDraftId id, string replacement)
                     in _drafts.ToArray())
            {
                if (!TryReadCommittedField(id, out string? committed) ||
                    string.Equals(
                        committed,
                        replacement,
                        StringComparison.Ordinal))
                {
                    changed |= _drafts.Remove(id);
                    changed |= _validationMessages.Remove(id);
                }
            }

            if (_staticModelTranslationDraft is { } transformDraft)
            {
                if (!_document.TryGetObject(
                        transformDraft.ObjectId,
                        out EditorMapObject? value) ||
                    value is not EditorStaticModel model ||
                    !SameExact(
                        model.Transform.Origin,
                        transformDraft.CommittedOrigin))
                {
                    _staticModelTranslationDraft = null;
                    changed = true;
                }
            }

            if (_authoredCollisionTranslationDraft is { } collisionDraft)
            {
                if (!_document.TryGetObject(
                        collisionDraft.ObjectId,
                        out EditorMapObject? value) ||
                    value is not EditorAuthoredCollisionObject authored ||
                    !ReferenceEquals(
                        authored.Source,
                        collisionDraft.CommittedSource))
                {
                    _authoredCollisionTranslationDraft = null;
                    changed = true;
                }
            }
        }

        if (changed)
            StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Document_PersistenceStateChanged(
        object? sender,
        EventArgs e)
    {
        lock (_gate)
        {
            if (_disposed)
                return;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RemoveDraft(MapEntityPropertyDraftId id)
    {
        bool changed;
        lock (_gate)
        {
            changed = _drafts.Remove(id);
            changed |= _validationMessages.Remove(id);
        }
        if (changed)
            StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private EditorEntity GetOwnedEntity(EditorEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (!_document.TryGetObject(
                entity.Id,
                out EditorMapObject? owned) ||
            !ReferenceEquals(owned, entity))
        {
            throw new ArgumentException(
                "The MapEnt entity belongs to another editor document.",
                nameof(entity));
        }

        return entity;
    }

    private EditorStaticModel GetOwnedRenderStaticModel(
        EditorStaticModel model)
    {
        if (!_document.TryGetObject(
                model.Id,
                out EditorMapObject? owned) ||
            !ReferenceEquals(owned, model) ||
            model.Representation != StaticModelRepresentation.Render ||
            !model.IsImported)
        {
            throw new ArgumentException(
                "The translation draft target must be an imported render " +
                "static model owned by this editor document.",
                nameof(model));
        }

        return model;
    }

    private EditorAuthoredCollisionObject GetOwnedAuthoredCollision(
        EditorAuthoredCollisionObject authored)
    {
        if (!_document.TryGetObject(
                authored.Id,
                out EditorMapObject? owned) ||
            !ReferenceEquals(owned, authored))
        {
            throw new ArgumentException(
                "The authored-collision translation target must be owned " +
                "by this editor document.",
                nameof(authored));
        }

        return authored;
    }

    private EditorEntity GetRequiredEntity(MapObjectId entityId)
    {
        if (!_document.TryGetObject(
                entityId,
                out EditorMapObject? value) ||
            value is not EditorEntity entity)
        {
            throw new InvalidOperationException(
                $"Map object {entityId} is not an entity in this document.");
        }

        return entity;
    }

    private int GetEntityOrdinal(MapObjectId entityId) =>
        GetRequiredEntity(entityId).SyntaxOrdinal.Value;

    private bool TryReadCommittedField(
        MapEntityPropertyDraftId id,
        out string? value)
    {
        value = null;
        if (!_document.TryGetObject(
                id.EntityId,
                out EditorMapObject? mapObject) ||
            mapObject is not EditorEntity entity ||
            (uint)id.PropertyOrdinal.Value >=
                (uint)entity.KeyValues.Count)
        {
            return false;
        }

        EditorEntityProperty property =
            entity.KeyValues[id.PropertyOrdinal.Value];
        value = id.Field switch
        {
            MapEntPropertyField.Key => property.Key,
            MapEntPropertyField.Value => property.Value,
            _ => null
        };
        return value is not null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static string ReadCommittedField(
        EditorEntity entity,
        MapEntPropertyOrdinal propertyOrdinal,
        MapEntPropertyField field)
    {
        EditorEntityProperty property = entity.GetProperty(propertyOrdinal);
        return field switch
        {
            MapEntPropertyField.Key => property.Key,
            MapEntPropertyField.Value => property.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
    }

    private static void ValidateField(MapEntPropertyField field)
    {
        if (!Enum.IsDefined(field))
            throw new ArgumentOutOfRangeException(nameof(field));
    }

    private static bool SetDictionaryValue<TKey, TValue>(
        IDictionary<TKey, TValue> values,
        TKey key,
        TValue value)
        where TKey : notnull
    {
        if (values.TryGetValue(key, out TValue? current) &&
            EqualityComparer<TValue>.Default.Equals(current, value))
        {
            return false;
        }

        values[key] = value;
        return true;
    }

    private static string FormatDraft(MapEntityPropertyDraftId id) =>
        $"Entity {id.EntityId}, property #{id.PropertyOrdinal.Value} " +
        id.Field.ToString().ToLowerInvariant();

    private static bool SameExact(MapVector3 left, MapVector3 right) =>
        SameBits(left.X, right.X) &&
        SameBits(left.Y, right.Y) &&
        SameBits(left.Z, right.Z);

    private static bool SameBits(float left, float right) =>
        BitConverter.SingleToInt32Bits(left) ==
        BitConverter.SingleToInt32Bits(right);

    private sealed record DraftValidationFailure(
        MapEntityPropertyDraftId Id,
        string Message);
}
