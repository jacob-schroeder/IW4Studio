using System.Collections.ObjectModel;
using IW4.Studio.MapEditor.Compilation.Collision;
using IW4.Studio.MapEditor.Editing.Collision;
using IW4.Studio.MapEditor.Editing.Commands;
using IW4.Studio.MapEditor.Editing.Entities;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.MapEntsSyntax;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.Editing.Provenance;

namespace IW4.Studio.MapEditor.Editing.Documents;

/// <summary>
/// Persistence relationship between a semantic map document and the compiled
/// baseline from which it was imported.
/// </summary>
public enum MapDocumentPersistenceDisposition
{
    ImportedBaseline,
    CommittedOutputRequiresReopen
}

/// <summary>
/// Exclusive semantic-document lease held across compiled candidate creation,
/// verification, commit, and acknowledgement.
/// </summary>
public sealed class MapCompiledSaveLease : IDisposable
{
    private readonly MapCommandHistory _history;
    private readonly Guid _leaseId;
    private int _committed;
    private int _disposed;

    internal MapCompiledSaveLease(
        MapCommandHistory history,
        Guid leaseId,
        MapDocumentId documentId,
        long documentRevision)
    {
        _history =
            history ?? throw new ArgumentNullException(nameof(history));
        if (leaseId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(leaseId));
        if (documentId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(documentId));
        if (documentRevision < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(documentRevision));
        }

        _leaseId = leaseId;
        DocumentId = documentId;
        DocumentRevision = documentRevision;
    }

    public MapDocumentId DocumentId { get; }

    public long DocumentRevision { get; }

    public bool IsCommitted =>
        Volatile.Read(ref _committed) != 0;

    public bool IsActive =>
        Volatile.Read(ref _disposed) == 0;

    internal void MarkCommitted()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(
                nameof(MapCompiledSaveLease));
        }
        if (Interlocked.CompareExchange(
                ref _committed,
                1,
                0) != 0)
        {
            throw new InvalidOperationException(
                "The compiled-save lease has already been committed.");
        }

        try
        {
            _history.CommitCompiledSaveLease(
                _leaseId,
                DocumentRevision);
        }
        catch
        {
            Volatile.Write(ref _committed, 0);
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _history.ReleaseCompiledSaveLease(
            _leaseId,
            DocumentRevision);
    }
}

public sealed record EditorEnvironmentValue(
    string Name,
    string Value,
    MapValueProvenance Provenance,
    SourceBindingId SourceBinding);

public sealed class EditorEnvironment
{
    private readonly IReadOnlyList<EditorEnvironmentValue> _values;

    public EditorEnvironment(IEnumerable<EditorEnvironmentValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = Array.AsReadOnly(values.ToArray());
    }

    public IReadOnlyList<EditorEnvironmentValue> Values => _values;
}

public sealed class EditorMapEntitySource
{
    private readonly byte[] _baselineBytes;
    private MapEntsSyntaxDocument _syntax;

    public EditorMapEntitySource(
        string? name,
        ReadOnlySpan<byte> rawBytes,
        SourceBindingId sourceBinding)
        : this(
            name,
            MapEntsSyntaxParser.Parse(rawBytes),
            sourceBinding)
    {
    }

    public EditorMapEntitySource(
        string? name,
        MapEntsSyntaxDocument syntax,
        SourceBindingId sourceBinding)
    {
        ArgumentNullException.ThrowIfNull(syntax);
        if (sourceBinding.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(sourceBinding));

        Name = name;
        _syntax = syntax;
        _baselineBytes = syntax.Serialize();
        BaselineDigest = syntax.ContentDigest;
        SourceBinding = sourceBinding;
    }

    public string? Name { get; }
    public int RawByteCount => _syntax.ByteLength;
    public MapValueProvenance Provenance => MapValueProvenance.ExactSerialized;
    public SourceBindingId SourceBinding { get; }
    public string BaselineDigest { get; }
    public string CurrentDigest => _syntax.ContentDigest;
    public MapEntsSyntaxDocument Syntax => _syntax;
    public byte[] GetBaselineBytesCopy() => _baselineBytes.ToArray();
    public byte[] GetRawBytesCopy() => _syntax.Serialize();

    internal bool HasSyntax(MapEntsSyntaxDocument syntax) =>
        _syntax.HasSameBytes(syntax);

    internal void SetSyntax(MapEntsSyntaxDocument syntax)
    {
        ArgumentNullException.ThrowIfNull(syntax);
        _syntax = syntax;
    }
}

/// <summary>
/// Aggregate semantic editing document. All mutation is mediated by its typed
/// command history; it owns no runtime BaseAsset, OpenGL resource, pool
/// address, or serialized row-index identity.
/// </summary>
public sealed class EditorMapDocument
{
    private readonly IReadOnlyList<EditorWorldSurface> _worldSurfaces;
    private IReadOnlyList<EditorStaticModel> _staticModels;
    private readonly IReadOnlyList<EditorCollisionObject> _collision;
    private IReadOnlyList<EditorAuthoredCollisionObject> _authoredCollision;
    private IReadOnlyList<EditorEntity> _entities;
    private readonly IReadOnlyList<EditorPrimaryLight> _primaryLights;
    private readonly IReadOnlyList<EditorGlassObject> _glass;
    private readonly IReadOnlyList<EditorSpatialObject> _spatialDebugObjects;
    private IReadOnlyList<EditorMapObject> _objects;
    private IReadOnlyDictionary<MapObjectId, EditorMapObject>
        _objectsById;
    private int _persistenceDisposition;

    public EditorMapDocument(
        MapDocumentId id,
        string mapIdentity,
        EditorEnvironment environment,
        EditorMapEntitySource? entitySource,
        IEnumerable<EditorWorldSurface> worldSurfaces,
        IEnumerable<EditorStaticModel> staticModels,
        IEnumerable<EditorCollisionObject> collision,
        IEnumerable<EditorEntity> entities,
        IEnumerable<EditorPrimaryLight> primaryLights,
        IEnumerable<EditorGlassObject> glass,
        IEnumerable<EditorSpatialObject> spatialDebugObjects,
        IEnumerable<EditorAuthoredCollisionObject>? authoredCollision = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapIdentity);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(worldSurfaces);
        ArgumentNullException.ThrowIfNull(staticModels);
        ArgumentNullException.ThrowIfNull(collision);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(primaryLights);
        ArgumentNullException.ThrowIfNull(glass);
        ArgumentNullException.ThrowIfNull(spatialDebugObjects);
        if (id.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(id));

        Id = id;
        MapIdentity = mapIdentity;
        Environment = environment;
        EntitySource = entitySource;
        _worldSurfaces = Copy(worldSurfaces);
        _staticModels = Copy(staticModels);
        _collision = Copy(collision);
        _authoredCollision = Copy(authoredCollision ?? []);
        _entities = Copy(entities);
        _primaryLights = Copy(primaryLights);
        _glass = Copy(glass);
        _spatialDebugObjects = Copy(spatialDebugObjects);
        ValidateEntityProjection(entitySource, _entities);
        ValidateStaticModelProjection(_staticModels);
        ValidateAuthoredCollisionProjection(
            _authoredCollision,
            _staticModels,
            _entities);
        (_objects, _objectsById) = CreateObjectProjection(
            _entities,
            _staticModels,
            _authoredCollision);
        History = new MapCommandHistory(this);
    }

    public MapDocumentId Id { get; }
    public string MapIdentity { get; }
    public long Revision => History.Revision;
    public bool IsDirty => History.IsDirty;
    public MapDocumentPersistenceDisposition PersistenceDisposition =>
        (MapDocumentPersistenceDisposition)Volatile.Read(
            ref _persistenceDisposition);
    public bool RequiresReopen =>
        PersistenceDisposition ==
        MapDocumentPersistenceDisposition.CommittedOutputRequiresReopen;
    public bool IsCompiledSaveInProgress =>
        History.IsCompiledSaveInProgress;
    public bool CanMutate => History.CanMutate;
    public MapCommandHistory History { get; }
    public EditorEnvironment Environment { get; }
    public EditorMapEntitySource? EntitySource { get; }
    public IReadOnlyList<EditorWorldSurface> WorldSurfaces => _worldSurfaces;
    public IReadOnlyList<EditorStaticModel> StaticModels => _staticModels;
    /// <summary>
    /// Immutable collision projection imported from the compiled baseline.
    /// Commands never replace or remove these objects.
    /// </summary>
    public IReadOnlyList<EditorCollisionObject> Collision => _collision;
    public IReadOnlyList<EditorCollisionObject> ImportedCollision =>
        _collision;
    /// <summary>
    /// Mutable semantic sources authored in Studio. They remain distinct
    /// from the imported compiled-collision projection.
    /// </summary>
    public IReadOnlyList<EditorAuthoredCollisionObject> AuthoredCollision =>
        _authoredCollision;
    public IReadOnlyList<EditorEntity> Entities => _entities;
    public IReadOnlyList<EditorPrimaryLight> PrimaryLights => _primaryLights;
    public IReadOnlyList<EditorGlassObject> Glass => _glass;
    public IReadOnlyList<EditorSpatialObject> SpatialDebugObjects => _spatialDebugObjects;
    public IReadOnlyList<EditorMapObject> Objects => _objects;

    /// <summary>
    /// Raised after a command transition commits. The revision and changed
    /// semantic identities form the stable seam for live scene projection.
    /// </summary>
    public event EventHandler<MapDocumentChangedEventArgs>? Changed;

    /// <summary>
    /// Raised when compiled-save lease or reopen-required state changes.
    /// </summary>
    public event EventHandler? PersistenceStateChanged;

    public bool TryGetObject(
        MapObjectId id,
        out EditorMapObject? value) =>
        _objectsById.TryGetValue(id, out value);

    public void MarkRevisionSaved(long revision) =>
        History.AcknowledgeSavedRevision(revision);

    public MapCompiledSaveLease AcquireCompiledSaveLease() =>
        History.AcquireCompiledSaveLease();

    internal void MarkCompiledSaveAsCommitted(long revision)
    {
        using MapCompiledSaveLease lease =
            AcquireCompiledSaveLease();
        if (lease.DocumentRevision != revision)
        {
            throw new InvalidOperationException(
                $"The compiled save expected map revision {revision}, but " +
                $"the document is at {lease.DocumentRevision}.");
        }
        lease.MarkCommitted();
    }

    internal void SetCompiledOutputRequiresReopen() =>
        Volatile.Write(
            ref _persistenceDisposition,
            (int)MapDocumentPersistenceDisposition
                .CommittedOutputRequiresReopen);

    internal EditorMapObject GetRequiredObject(MapObjectId id)
    {
        if (!_objectsById.TryGetValue(id, out EditorMapObject? value))
        {
            throw new KeyNotFoundException(
                $"Map document {Id} does not contain semantic object {id}.");
        }

        return value;
    }

    internal T GetRequiredObject<T>(MapObjectId id)
        where T : EditorMapObject
    {
        EditorMapObject value = GetRequiredObject(id);
        if (value is not T typed)
        {
            throw new InvalidOperationException(
                $"Semantic object {id} is {value.Kind}, not {typeof(T).Name}.");
        }

        return typed;
    }

    internal void PublishChanged(MapDocumentChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        Changed?.Invoke(this, change);
    }

    internal void PublishPersistenceStateChanged() =>
        PersistenceStateChanged?.Invoke(this, EventArgs.Empty);

    internal T ReadConsistent<T>(Func<long, T> reader) =>
        History.ReadConsistent(reader);

    internal void ApplyEntitySyntaxState(
        MapEntsSyntaxDocument expectedSyntax,
        MapEntsSyntaxDocument replacementSyntax,
        IReadOnlyList<EditorEntityState> expectedStates,
        IReadOnlyList<EditorEntityState> replacementStates)
    {
        ArgumentNullException.ThrowIfNull(expectedSyntax);
        ArgumentNullException.ThrowIfNull(replacementSyntax);
        ArgumentNullException.ThrowIfNull(expectedStates);
        ArgumentNullException.ThrowIfNull(replacementStates);
        if (EntitySource is null)
        {
            throw new InvalidOperationException(
                "The editor document has no MapEnt syntax source.");
        }
        if (expectedStates.Count != _entities.Count ||
            replacementStates.Count != _entities.Count)
        {
            throw new InvalidOperationException(
                "MapEnt semantic state cardinality does not match the document.");
        }
        if (!EntitySource.HasSyntax(expectedSyntax))
        {
            throw new InvalidOperationException(
                "The MapEnt syntax source changed outside the command journal.");
        }

        for (int index = 0; index < _entities.Count; index++)
        {
            if (!_entities[index].HasState(expectedStates[index]))
            {
                throw new InvalidOperationException(
                    $"Semantic MapEnt entity {index} changed outside the command journal.");
            }
        }

        // Validation above is complete; these reference assignments cannot
        // partially parse or allocate a new semantic projection.
        EntitySource.SetSyntax(replacementSyntax);
        for (int index = 0; index < _entities.Count; index++)
            _entities[index].SetState(replacementStates[index]);
    }

    internal EditorEntityCollectionState CaptureEntityCollectionState() =>
        new(EntitySource?.Syntax ??
            throw new InvalidOperationException(
                "The editor document has no MapEnt syntax source."),
            _entities);

    internal void ApplyEntityCollectionState(
        EditorEntityCollectionState expected,
        EditorEntityCollectionState replacement)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(replacement);
        if (EntitySource is null)
        {
            throw new InvalidOperationException(
                "The editor document has no MapEnt syntax source.");
        }
        if (!EntitySource.HasSyntax(expected.Syntax) ||
            expected.Entities.Count != _entities.Count ||
            expected.Entities.Where((value, index) =>
                    !ReferenceEquals(value, _entities[index]))
                .Any())
        {
            throw new InvalidOperationException(
                "The MapEnt syntax/entity collection changed outside the " +
                "command journal.");
        }

        ValidateEntityProjection(
            new EditorMapEntitySource(
                EntitySource.Name,
                replacement.Syntax,
                EntitySource.SourceBinding),
            replacement.Entities);
        ValidateAuthoredCollisionProjection(
            _authoredCollision,
            _staticModels,
            replacement.Entities);
        (IReadOnlyList<EditorMapObject> objects,
            IReadOnlyDictionary<MapObjectId, EditorMapObject> objectsById) =
            CreateObjectProjection(replacement.Entities);

        // All validation and allocation complete before the state swap.
        EntitySource.SetSyntax(replacement.Syntax);
        _entities = replacement.Entities;
        _objects = objects;
        _objectsById = objectsById;
    }

    internal EditorStaticModelCollectionState
        CaptureStaticModelCollectionState() =>
        new(_staticModels);

    internal void ApplyStaticModelCollectionState(
        EditorStaticModelCollectionState expected,
        EditorStaticModelCollectionState replacement)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(replacement);
        if (expected.StaticModels.Count != _staticModels.Count ||
            expected.StaticModels.Where((value, index) =>
                    !ReferenceEquals(value, _staticModels[index]))
                .Any())
        {
            throw new InvalidOperationException(
                "The static-model collection changed outside the command " +
                "journal.");
        }

        ValidateStaticModelProjection(replacement.StaticModels);
        ValidateAuthoredCollisionProjection(
            _authoredCollision,
            replacement.StaticModels,
            _entities);
        (IReadOnlyList<EditorMapObject> objects,
            IReadOnlyDictionary<MapObjectId, EditorMapObject> objectsById) =
            CreateObjectProjection(
                _entities,
                replacement.StaticModels);

        _staticModels = replacement.StaticModels;
        _objects = objects;
        _objectsById = objectsById;
    }

    internal EditorAuthoredCollisionCollectionState
        CaptureAuthoredCollisionCollectionState() =>
        new(_authoredCollision);

    internal void ApplyAuthoredCollisionCollectionState(
        EditorAuthoredCollisionCollectionState expected,
        EditorAuthoredCollisionCollectionState replacement)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(replacement);
        if (expected.AuthoredCollision.Count != _authoredCollision.Count ||
            expected.AuthoredCollision.Where((value, index) =>
                    !ReferenceEquals(value, _authoredCollision[index]))
                .Any())
        {
            throw new InvalidOperationException(
                "The authored-collision collection changed outside the " +
                "command journal.");
        }

        ValidateAuthoredCollisionProjection(
            replacement.AuthoredCollision,
            _staticModels,
            _entities);
        (IReadOnlyList<EditorMapObject> objects,
            IReadOnlyDictionary<MapObjectId, EditorMapObject> objectsById) =
            CreateObjectProjection(
                _entities,
                _staticModels,
                replacement.AuthoredCollision);

        _authoredCollision = replacement.AuthoredCollision;
        _objects = objects;
        _objectsById = objectsById;
    }

    private static IReadOnlyList<T> Copy<T>(IEnumerable<T> values)
        where T : notnull
    {
        T[] copy = values.ToArray();
        if (copy.Any(value => value is null))
            throw new InvalidDataException("Map document collections cannot contain null objects.");

        return Array.AsReadOnly(copy);
    }

    private (
        IReadOnlyList<EditorMapObject> Objects,
        IReadOnlyDictionary<MapObjectId, EditorMapObject> ObjectsById)
        CreateObjectProjection(
            IReadOnlyList<EditorEntity> entities,
            IReadOnlyList<EditorStaticModel>? staticModels = null,
            IReadOnlyList<EditorAuthoredCollisionObject>?
                authoredCollision = null)
    {
        IReadOnlyList<EditorStaticModel> effectiveStaticModels =
            staticModels ?? _staticModels;
        IReadOnlyList<EditorAuthoredCollisionObject>
            effectiveAuthoredCollision =
            authoredCollision ?? _authoredCollision;
        var objects = new ReadOnlyCollection<EditorMapObject>(
        [
            .. _worldSurfaces,
            .. effectiveStaticModels,
            .. _collision,
            .. effectiveAuthoredCollision,
            .. entities,
            .. _primaryLights,
            .. _glass,
            .. _spatialDebugObjects
        ]);
        var objectIds = new HashSet<MapObjectId>();
        foreach (EditorMapObject value in objects)
        {
            if (!objectIds.Add(value.Id))
            {
                throw new InvalidDataException(
                    $"Semantic map objects contain duplicate ID {value.Id}.");
            }
        }
        foreach (EditorAuthoredCollisionObject authored in
                 effectiveAuthoredCollision)
        {
            if (objects
                .Where(value => !ReferenceEquals(value, authored))
                .SelectMany(value => value.SourceBindings)
                .Contains(authored.EditorProvenanceBinding))
            {
                throw new InvalidDataException(
                    $"Authored collision {authored.Id} reuses a provenance " +
                    "binding owned by another semantic object.");
            }
        }

        return (
            objects,
            new ReadOnlyDictionary<MapObjectId, EditorMapObject>(
                objects.ToDictionary(value => value.Id)));
    }

    private static void ValidateAuthoredCollisionProjection(
        IReadOnlyList<EditorAuthoredCollisionObject> authoredCollision,
        IReadOnlyList<EditorStaticModel> staticModels,
        IReadOnlyList<EditorEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(authoredCollision);
        ArgumentNullException.ThrowIfNull(staticModels);
        ArgumentNullException.ThrowIfNull(entities);
        if (authoredCollision.Select(value => value.Id).Distinct().Count() !=
            authoredCollision.Count)
        {
            throw new InvalidDataException(
                "Authored collision object identities must be unique.");
        }
        if (authoredCollision
                .Select(value => value.EditorProvenanceBinding)
                .Distinct()
                .Count() != authoredCollision.Count)
        {
            throw new InvalidDataException(
                "Authored collision editor-provenance bindings must be " +
                "unique.");
        }

        foreach (EditorAuthoredCollisionObject authored in authoredCollision)
        {
            if (authored.Source.ObjectId != authored.Id ||
                authored.Source.Provenance !=
                    CollisionSourceProvenance.Authored)
            {
                throw new InvalidDataException(
                    $"Authored collision {authored.Id} does not retain one " +
                    "canonical authored source identity.");
            }

            CollisionCounterpartIdentity? counterpart =
                authored.Source.Ownership.Counterpart;
            if (counterpart is null)
                continue;

            bool counterpartExists = counterpart.Value.Kind switch
            {
                CollisionCounterpartKind.RenderStaticModel =>
                    staticModels.Any(value =>
                        value.Id == counterpart.Value.ObjectId &&
                        value.Representation ==
                            StaticModelRepresentation.Render &&
                        value.CompiledDisposition !=
                            StaticModelCompiledDisposition.Removed),
                CollisionCounterpartKind.MapEntity =>
                    entities.Any(value =>
                        value.Id == counterpart.Value.ObjectId),
                _ => false
            };
            if (!counterpartExists)
            {
                throw new InvalidDataException(
                    $"Authored collision {authored.Id} references missing " +
                    $"{counterpart.Value.Kind} counterpart " +
                    $"{counterpart.Value.ObjectId}.");
            }
        }
    }

    private static void ValidateStaticModelProjection(
        IReadOnlyList<EditorStaticModel> staticModels)
    {
        ArgumentNullException.ThrowIfNull(staticModels);
        foreach (StaticModelRepresentation representation in
                 Enum.GetValues<StaticModelRepresentation>())
        {
            EditorStaticModel[] importedRows = staticModels
                .Where(value =>
                    value.Representation == representation &&
                    value.IsImported)
                .ToArray();
            if (importedRows.Select(value => value.SourceOrdinal.Value)
                    .Distinct()
                    .Count() != importedRows.Length)
            {
                throw new InvalidDataException(
                    $"{representation} imported static-model source " +
                    "ordinals must be unique.");
            }

            EditorStaticModel[] authoredRows = staticModels
                .Where(value =>
                    value.Representation == representation &&
                    value.LineageKind ==
                    StaticModelLineageKind.AuthoredDuplicate)
                .ToArray();
            if (authoredRows.Select(value => value.SourceOrdinal.Value)
                    .Distinct()
                    .Count() != authoredRows.Length)
            {
                throw new InvalidDataException(
                    $"{representation} authored static-model projected " +
                    "ordinals must be unique.");
            }
        }

        IGrouping<StaticModelDuplicationOperationId, EditorStaticModel>[]
            authoredPairs = staticModels
                .Where(value =>
                    value.LineageKind ==
                    StaticModelLineageKind.AuthoredDuplicate)
                .GroupBy(value =>
                    value.AuthoredDuplicatePair?.OperationId ??
                    throw new InvalidDataException(
                        "Authored static-model lineage has no shared pair " +
                        "state."))
                .ToArray();
        foreach (IGrouping<
                     StaticModelDuplicationOperationId,
                     EditorStaticModel> group in authoredPairs)
        {
            EditorStaticModel[] pair = group.ToArray();
            AuthoredStaticModelDuplicatePairState shared =
                pair[0].AuthoredDuplicatePair ??
                throw new InvalidDataException(
                    "Authored static-model lineage has no shared pair state.");
            if (pair.Length != 2 ||
                pair.Any(value =>
                    !ReferenceEquals(
                        value.AuthoredDuplicatePair,
                        shared)) ||
                pair.Count(value =>
                    value.Representation ==
                    StaticModelRepresentation.Render) != 1 ||
                pair.Count(value =>
                    value.Representation ==
                    StaticModelRepresentation.Collision) != 1 ||
                pair.Any(value =>
                    value.CompiledDisposition !=
                    StaticModelCompiledDisposition.AuthoredPending) ||
                pair.Any(value =>
                    value.Id != shared.ObjectId(value.Representation) ||
                    value.SourceOrdinal.Value !=
                    shared.ProjectedOrdinal(value.Representation)))
            {
                throw new InvalidDataException(
                    $"Authored static-model duplication operation {group.Key} " +
                    "must own exactly one coherent render/collision pair.");
            }
        }
    }

    private static void ValidateEntityProjection(
        EditorMapEntitySource? source,
        IReadOnlyList<EditorEntity> entities)
    {
        if (source is null)
        {
            if (entities.Count != 0)
            {
                throw new InvalidDataException(
                    "Semantic MapEnt entities require a byte-authoritative syntax source.");
            }

            return;
        }

        MapEntsSyntaxDocument syntax = source.Syntax;
        if (syntax.Entities.Count != entities.Count)
        {
            throw new InvalidDataException(
                "MapEnt syntax and semantic entity counts must match.");
        }

        var bindings = new HashSet<SourceBindingId>
        {
            source.SourceBinding
        };
        for (int entityIndex = 0;
             entityIndex < entities.Count;
             entityIndex++)
        {
            EditorEntity semantic = entities[entityIndex];
            MapEntsSyntaxEntity parsed = syntax.Entities[entityIndex];
            if (semantic.SyntaxOrdinal != parsed.Ordinal ||
                semantic.SourceOrdinal.Value != entityIndex)
            {
                throw new InvalidDataException(
                    $"Semantic MapEnt entity {entityIndex} does not retain its exact syntax ordinal.");
            }
            if (semantic.SourceByteOffset.Value != parsed.Span.Offset ||
                semantic.SourceByteLength.Value != parsed.Span.Length)
            {
                throw new InvalidDataException(
                    $"Semantic MapEnt entity {entityIndex} does not retain its exact syntax span.");
            }
            RequireSemanticEntityProvenance(
                semantic.SourceOrdinal.Provenance,
                $"entity {entityIndex} ordinal");
            RequireSemanticEntityProvenance(
                semantic.SourceByteOffset.Provenance,
                $"entity {entityIndex} byte offset");
            RequireSemanticEntityProvenance(
                semantic.SourceByteLength.Provenance,
                $"entity {entityIndex} byte length");
            RequireUniqueBinding(
                bindings,
                semantic.SourceOrdinal.SourceBinding,
                $"entity {entityIndex} ordinal");
            RequireUniqueBinding(
                bindings,
                semantic.SourceByteOffset.SourceBinding,
                $"entity {entityIndex} byte offset");
            RequireUniqueBinding(
                bindings,
                semantic.SourceByteLength.SourceBinding,
                $"entity {entityIndex} byte length");

            if (semantic.KeyValues.Count != parsed.Properties.Count)
            {
                throw new InvalidDataException(
                    $"Semantic MapEnt entity {entityIndex} property count does not match its syntax source.");
            }
            for (int propertyIndex = 0;
                 propertyIndex < semantic.KeyValues.Count;
                 propertyIndex++)
            {
                EditorEntityProperty semanticProperty =
                    semantic.KeyValues[propertyIndex];
                MapEntsSyntaxProperty parsedProperty =
                    parsed.Properties[propertyIndex];
                if (semanticProperty.Ordinal != parsedProperty.Ordinal ||
                    !string.Equals(
                        semanticProperty.Key,
                        parsedProperty.Key,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        semanticProperty.Value,
                        parsedProperty.Value,
                        StringComparison.Ordinal) ||
                    semanticProperty.Span != parsedProperty.Span ||
                    semanticProperty.KeyTokenSpan !=
                        parsedProperty.KeyTokenSpan ||
                    semanticProperty.KeyContentSpan !=
                        parsedProperty.KeyContentSpan ||
                    semanticProperty.ValueTokenSpan !=
                        parsedProperty.ValueTokenSpan ||
                    semanticProperty.ValueContentSpan !=
                        parsedProperty.ValueContentSpan)
                {
                    throw new InvalidDataException(
                        $"Semantic MapEnt entity {entityIndex} property {propertyIndex} does not exactly match its syntax source.");
                }

                RequireSemanticEntityProvenance(
                    semanticProperty.KeyProvenance,
                    $"entity {entityIndex} property {propertyIndex} key");
                RequireSemanticEntityProvenance(
                    semanticProperty.ValueProvenance,
                    $"entity {entityIndex} property {propertyIndex} value");
                RequireUniqueBinding(
                    bindings,
                    semanticProperty.KeySourceBinding,
                    $"entity {entityIndex} property {propertyIndex} key");
                RequireUniqueBinding(
                    bindings,
                    semanticProperty.ValueSourceBinding,
                    $"entity {entityIndex} property {propertyIndex} value");
            }

            string? expectedClassName =
                GetUniqueClassName(parsed.Properties);
            if (!string.Equals(
                    semantic.ClassName,
                    expectedClassName,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Semantic MapEnt entity {entityIndex} classname does not match its syntax source.");
            }

            if (syntax.CanEdit)
            {
                MapEntityCompilationAssessment expectedAssessment =
                    MapEntityConsumerCatalog.ConservativeIw4.Classify(
                        parsed.Properties.Select(value =>
                            new KeyValuePair<string, string>(
                                value.Key,
                                value.Value)));
                if (semantic.CompilationAssessment != expectedAssessment)
                {
                    throw new InvalidDataException(
                        $"Semantic MapEnt entity {entityIndex} has stale or unverified compiled-consumer evidence.");
                }
            }
            else if (semantic.CompilationAssessment.Relationship !=
                     MapEntityCompilationRelationship.Unknown)
            {
                throw new InvalidDataException(
                    $"Malformed MapEnt entity {entityIndex} must fail closed with an unknown compiled-consumer relationship.");
            }
        }
    }

    private static string? GetUniqueClassName(
        IEnumerable<MapEntsSyntaxProperty> properties)
    {
        string[] values = properties
            .Where(value => string.Equals(
                value.Key,
                "classname",
                StringComparison.OrdinalIgnoreCase))
            .Select(value => value.Value)
            .ToArray();
        return values.Length == 1 &&
               !string.IsNullOrWhiteSpace(values[0])
            ? values[0]
            : null;
    }

    private static void RequireSemanticEntityProvenance(
        MapValueProvenance provenance,
        string field)
    {
        if (provenance is not (
                MapValueProvenance.ExactDecodedRuntime or
                MapValueProvenance.Authored))
        {
            throw new InvalidDataException(
                $"Semantic MapEnt {field} requires exact decoded-runtime or " +
                "authored provenance.");
        }
    }

    private static void RequireUniqueBinding(
        ISet<SourceBindingId> bindings,
        SourceBindingId binding,
        string field)
    {
        if (!bindings.Add(binding))
        {
            throw new InvalidDataException(
                $"Semantic MapEnt {field} does not have a distinct source binding.");
        }
    }
}

internal sealed record EditorEntityCollectionState
{
    public EditorEntityCollectionState(
        MapEntsSyntaxDocument syntax,
        IEnumerable<EditorEntity> entities)
    {
        Syntax = syntax ??
            throw new ArgumentNullException(nameof(syntax));
        ArgumentNullException.ThrowIfNull(entities);
        EditorEntity[] copy = entities.ToArray();
        if (copy.Any(value => value is null))
        {
            throw new ArgumentException(
                "MapEnt collection state cannot contain null entities.",
                nameof(entities));
        }
        Entities = Array.AsReadOnly(copy);
    }

    public MapEntsSyntaxDocument Syntax { get; }
    public IReadOnlyList<EditorEntity> Entities { get; }
}

internal sealed record EditorStaticModelCollectionState
{
    public EditorStaticModelCollectionState(
        IEnumerable<EditorStaticModel> staticModels)
    {
        ArgumentNullException.ThrowIfNull(staticModels);
        EditorStaticModel[] copy = staticModels.ToArray();
        if (copy.Any(value => value is null))
        {
            throw new ArgumentException(
                "Static-model collection state cannot contain null objects.",
                nameof(staticModels));
        }

        StaticModels = Array.AsReadOnly(copy);
    }

    public IReadOnlyList<EditorStaticModel> StaticModels { get; }
}

internal sealed record EditorAuthoredCollisionCollectionState
{
    public EditorAuthoredCollisionCollectionState(
        IEnumerable<EditorAuthoredCollisionObject> authoredCollision)
    {
        ArgumentNullException.ThrowIfNull(authoredCollision);
        EditorAuthoredCollisionObject[] copy =
            authoredCollision.ToArray();
        if (copy.Any(value => value is null))
        {
            throw new ArgumentException(
                "Authored-collision collection state cannot contain null " +
                "objects.",
                nameof(authoredCollision));
        }

        AuthoredCollision = Array.AsReadOnly(copy);
    }

    public IReadOnlyList<EditorAuthoredCollisionObject> AuthoredCollision
    {
        get;
    }
}
