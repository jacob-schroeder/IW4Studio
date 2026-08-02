using System.Collections.ObjectModel;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.Editing.Collision;

namespace IW4.Studio.MapEditor.Editing.Projection;

/// <summary>
/// Stable identity of immutable renderer content associated with one semantic
/// object. It intentionally excludes document revision and editor state.
/// </summary>
public readonly record struct EditorSceneContentIdentity(
    MapObjectId SemanticObjectId);

/// <summary>
/// Identity of one projected object-state version. Content identity remains
/// stable while this identity changes on a semantic or editor-state edit.
/// </summary>
public readonly record struct EditorSceneStateIdentity
{
    public EditorSceneStateIdentity(
        MapObjectId semanticObjectId,
        long revision)
    {
        if (semanticObjectId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(semanticObjectId));
        if (revision < 0)
            throw new ArgumentOutOfRangeException(nameof(revision));

        SemanticObjectId = semanticObjectId;
        Revision = revision;
    }

    public MapObjectId SemanticObjectId { get; }
    public long Revision { get; }
}

public readonly record struct EditorSceneSnapshotIdentity
{
    public EditorSceneSnapshotIdentity(
        MapDocumentId documentId,
        long revision)
    {
        if (documentId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(documentId));
        if (revision < 0)
            throw new ArgumentOutOfRangeException(nameof(revision));

        DocumentId = documentId;
        Revision = revision;
    }

    public MapDocumentId DocumentId { get; }
    public long Revision { get; }
}

public sealed record EditorSceneObject(
    MapObjectId ObjectId,
    MapObjectKind Kind,
    string DisplayName,
    EditorObjectVisibility Visibility,
    MapBounds? Bounds,
    EditorSceneContentIdentity ContentIdentity,
    EditorSceneStateIdentity StateIdentity);

/// <summary>
/// Complete semantic primary-light row. Hidden lights remain present so a
/// renderer can validate ordinal/count correspondence independently from
/// editor visibility.
/// </summary>
public sealed record EditorScenePrimaryLight(
    MapObjectId ObjectId,
    int SourceOrdinal,
    EditorObjectVisibility Visibility,
    byte LightType,
    byte CanUseShadowMap,
    byte Exponent,
    byte Unused,
    MapVector3 Color,
    MapVector3 Direction,
    MapVector3 Origin,
    float Radius,
    float CosHalfFovOuter,
    float CosHalfFovInner,
    float CosHalfFovExpanded,
    float RotationLimit,
    float TranslationLimit,
    string? DefinitionName,
    EditorSceneContentIdentity ContentIdentity,
    EditorSceneStateIdentity StateIdentity);

/// <summary>
/// Complete semantic static-model row. Render and collision representations
/// remain distinct because ordinal or transform similarity is not proof that
/// they describe the same compiled object.
/// </summary>
public sealed record EditorSceneStaticModel(
    MapObjectId ObjectId,
    StaticModelRepresentation Representation,
    int SourceOrdinal,
    EditorObjectVisibility Visibility,
    StaticModelCompiledDisposition CompiledDisposition,
    string? ModelName,
    MapVector3 Origin,
    float? Scale,
    MapBounds? Bounds,
    EditorSceneContentIdentity ContentIdentity,
    EditorSceneStateIdentity StateIdentity);

/// <summary>
/// Immutable, backend-neutral projection of one editor-document revision.
/// </summary>
public sealed class EditorSceneSnapshot
{
    private readonly IReadOnlyList<EditorSceneObject> _objects;
    private readonly IReadOnlyList<EditorSceneStaticModel> _staticModels;
    private readonly IReadOnlyList<EditorScenePrimaryLight> _primaryLights;
    private readonly IReadOnlyList<MapObjectId> _changedObjects;
    private readonly IReadOnlyList<MapObjectId> _removedObjects;
    private readonly IReadOnlyDictionary<MapObjectId, EditorSceneObject>
        _objectsById;
    private readonly IReadOnlyDictionary<MapObjectId, EditorSceneStaticModel>
        _staticModelsById;

    internal EditorSceneSnapshot(
        EditorSceneSnapshotIdentity identity,
        IEnumerable<EditorSceneObject> objects,
        IEnumerable<EditorSceneStaticModel> staticModels,
        IEnumerable<EditorScenePrimaryLight> primaryLights,
        IEnumerable<MapObjectId> changedObjects,
        IEnumerable<MapObjectId> removedObjects)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(staticModels);
        ArgumentNullException.ThrowIfNull(primaryLights);
        ArgumentNullException.ThrowIfNull(changedObjects);
        ArgumentNullException.ThrowIfNull(removedObjects);

        EditorSceneObject[] objectCopy = objects.ToArray();
        EditorSceneStaticModel[] staticModelCopy =
            staticModels.ToArray();
        EditorScenePrimaryLight[] lightCopy = primaryLights.ToArray();
        MapObjectId[] changedCopy = changedObjects.Distinct().ToArray();
        MapObjectId[] removedCopy = removedObjects.Distinct().ToArray();
        if (objectCopy.Any(value => value is null) ||
            staticModelCopy.Any(value => value is null) ||
            lightCopy.Any(value => value is null))
        {
            throw new InvalidDataException(
                "Editor scene snapshots cannot contain null rows.");
        }

        var byId = new Dictionary<MapObjectId, EditorSceneObject>();
        foreach (EditorSceneObject value in objectCopy)
        {
            if (!byId.TryAdd(value.ObjectId, value))
            {
                throw new InvalidDataException(
                    $"Editor scene projection contains duplicate object {value.ObjectId}.");
            }
        }

        MapObjectId[] lightObjectIds = objectCopy
            .Where(value => value.Kind == MapObjectKind.PrimaryLight)
            .Select(value => value.ObjectId)
            .ToArray();
        if (lightCopy.Select(value => value.ObjectId).Distinct().Count() !=
                lightCopy.Length ||
            !lightObjectIds.ToHashSet().SetEquals(
                lightCopy.Select(value => value.ObjectId)))
        {
            throw new InvalidDataException(
                "Projected primary-light rows must match every primary-light scene object exactly once.");
        }

        MapObjectId[] staticModelObjectIds = objectCopy
            .Where(value =>
                value.Kind is MapObjectKind.RenderStaticModel or
                    MapObjectKind.CollisionStaticModel)
            .Select(value => value.ObjectId)
            .ToArray();
        if (staticModelCopy
                .Select(value => value.ObjectId)
                .Distinct()
                .Count() != staticModelCopy.Length ||
            !staticModelObjectIds.ToHashSet().SetEquals(
                staticModelCopy.Select(value => value.ObjectId)))
        {
            throw new InvalidDataException(
                "Projected static-model rows must match every render and collision static-model scene object exactly once.");
        }
        foreach (EditorSceneStaticModel value in staticModelCopy)
        {
            MapObjectKind expectedKind = value.Representation ==
                StaticModelRepresentation.Render
                    ? MapObjectKind.RenderStaticModel
                    : MapObjectKind.CollisionStaticModel;
            if (byId[value.ObjectId].Kind != expectedKind)
            {
                throw new InvalidDataException(
                    $"Projected static-model representation for {value.ObjectId} does not match its semantic object kind.");
            }
        }

        Identity = identity;
        _objects = Array.AsReadOnly(objectCopy);
        _staticModels = Array.AsReadOnly(staticModelCopy);
        _primaryLights = Array.AsReadOnly(lightCopy);
        _changedObjects = Array.AsReadOnly(changedCopy);
        _removedObjects = Array.AsReadOnly(removedCopy);
        _objectsById =
            new ReadOnlyDictionary<MapObjectId, EditorSceneObject>(byId);
        _staticModelsById =
            new ReadOnlyDictionary<MapObjectId, EditorSceneStaticModel>(
                staticModelCopy.ToDictionary(value => value.ObjectId));
    }

    public EditorSceneSnapshotIdentity Identity { get; }
    public MapDocumentId DocumentId => Identity.DocumentId;
    public long Revision => Identity.Revision;
    public IReadOnlyList<EditorSceneObject> Objects => _objects;
    public IReadOnlyList<EditorSceneStaticModel> StaticModels =>
        _staticModels;
    public IReadOnlyList<EditorScenePrimaryLight> PrimaryLights =>
        _primaryLights;
    public IReadOnlyList<MapObjectId> ChangedObjects => _changedObjects;
    public IReadOnlyList<MapObjectId> RemovedObjects => _removedObjects;

    public bool TryGetObject(
        MapObjectId objectId,
        out EditorSceneObject? value) =>
        _objectsById.TryGetValue(objectId, out value);

    public bool TryGetStaticModel(
        MapObjectId objectId,
        out EditorSceneStaticModel? value) =>
        _staticModelsById.TryGetValue(objectId, out value);
}

public interface IEditorSceneProjector
{
    EditorSceneSnapshot Project(
        EditorMapDocument document,
        EditorSceneSnapshot? previous = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Projects semantic map state without depending on OpenGL, compiled asset
/// rows, or mutable renderer resources. Unchanged rows are reused from the
/// previous snapshot and retain stable content identities.
/// </summary>
public sealed class EditorSceneProjector : IEditorSceneProjector
{
    public EditorSceneSnapshot Project(
        EditorMapDocument document,
        EditorSceneSnapshot? previous = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        return document.ReadConsistent(revision =>
            ProjectRevision(
                document,
                revision,
                previous,
                cancellationToken));
    }

    private static EditorSceneSnapshot ProjectRevision(
        EditorMapDocument document,
        long revision,
        EditorSceneSnapshot? previous,
        CancellationToken cancellationToken)
    {
        bool canReconcile =
            previous is not null &&
            previous.DocumentId == document.Id &&
            previous.Revision <= revision;
        HashSet<MapObjectId> changed = canReconcile
            ? document.History
                .GetChangedObjectsSince(previous!.Revision)
                .ToHashSet()
            : document.Objects.Select(value => value.Id).ToHashSet();
        var previousObjects = canReconcile
            ? previous!.Objects.ToDictionary(value => value.ObjectId)
            : [];
        var previousLights = canReconcile
            ? previous!.PrimaryLights.ToDictionary(value => value.ObjectId)
            : [];
        var previousStaticModels = canReconcile
            ? previous!.StaticModels.ToDictionary(value => value.ObjectId)
            : [];

        var projectedObjects =
            new List<EditorSceneObject>(document.Objects.Count);
        for (int index = 0; index < document.Objects.Count; index++)
        {
            if ((index & 1023) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            EditorMapObject source = document.Objects[index];
            if (!changed.Contains(source.Id) &&
                previousObjects.TryGetValue(
                    source.Id,
                    out EditorSceneObject? unchanged))
            {
                projectedObjects.Add(unchanged);
                continue;
            }

            EditorSceneContentIdentity contentIdentity =
                previousObjects.TryGetValue(
                    source.Id,
                    out EditorSceneObject? prior)
                    ? prior.ContentIdentity
                    : new EditorSceneContentIdentity(source.Id);
            projectedObjects.Add(new EditorSceneObject(
                source.Id,
                source.Kind,
                source.DisplayName,
                source.Visibility,
                GetBounds(source),
                contentIdentity,
                new EditorSceneStateIdentity(
                    source.Id,
                    revision)));
        }

        var projectedLights =
            new List<EditorScenePrimaryLight>(
                document.PrimaryLights.Count);
        IReadOnlyDictionary<MapObjectId, EditorSceneObject>
            projectedObjectsById =
                projectedObjects.ToDictionary(value => value.ObjectId);
        for (int index = 0; index < document.PrimaryLights.Count; index++)
        {
            if ((index & 255) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            EditorPrimaryLight source = document.PrimaryLights[index];
            if (!changed.Contains(source.Id) &&
                previousLights.TryGetValue(
                    source.Id,
                    out EditorScenePrimaryLight? unchanged))
            {
                projectedLights.Add(unchanged);
                continue;
            }

            EditorSceneObject projectedObject =
                projectedObjectsById[source.Id];
            projectedLights.Add(new EditorScenePrimaryLight(
                source.Id,
                source.SourceOrdinal.Value,
                source.Visibility,
                source.LightType.Value,
                source.CanUseShadowMap.Value,
                source.Exponent.Value,
                source.Unused.Value,
                source.Color.Value,
                source.Direction.Value,
                source.Origin.Value,
                source.Radius.Value,
                source.CosHalfFovOuter.Value,
                source.CosHalfFovInner.Value,
                source.CosHalfFovExpanded.Value,
                source.RotationLimit.Value,
                source.TranslationLimit.Value,
                source.DefinitionName.Value,
                projectedObject.ContentIdentity,
                projectedObject.StateIdentity));
        }

        var projectedStaticModels =
            new List<EditorSceneStaticModel>(
                document.StaticModels.Count);
        for (int index = 0;
             index < document.StaticModels.Count;
             index++)
        {
            if ((index & 1023) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            EditorStaticModel source = document.StaticModels[index];
            if (!changed.Contains(source.Id) &&
                previousStaticModels.TryGetValue(
                    source.Id,
                    out EditorSceneStaticModel? unchanged))
            {
                projectedStaticModels.Add(unchanged);
                continue;
            }

            EditorSceneObject projectedObject =
                projectedObjectsById[source.Id];
            projectedStaticModels.Add(new EditorSceneStaticModel(
                source.Id,
                source.Representation,
                source.SourceOrdinal.Value,
                source.Visibility,
                source.CompiledDisposition,
                source.ModelName.Value,
                source.Origin.Value,
                source.Scale.Value,
                source.Bounds.Value,
                projectedObject.ContentIdentity,
                projectedObject.StateIdentity));
        }

        HashSet<MapObjectId> currentIds =
            document.Objects.Select(value => value.Id).ToHashSet();
        MapObjectId[] removed = canReconcile
            ? previous!.Objects
                .Where(value => !currentIds.Contains(value.ObjectId))
                .Select(value => value.ObjectId)
                .ToArray()
            : [];
        changed.UnionWith(removed);

        return new EditorSceneSnapshot(
            new EditorSceneSnapshotIdentity(
                document.Id,
                revision),
            projectedObjects,
            projectedStaticModels,
            projectedLights,
            changed,
            removed);
    }

    private static MapBounds? GetBounds(EditorMapObject value) =>
        value switch
        {
            EditorWorldSurface surface => surface.Bounds.Value,
            EditorStaticModel model => model.Bounds.Value,
            EditorCollisionObject collision => collision.Bounds.Value,
            EditorAuthoredCollisionObject authored =>
                AuthoredCollisionSourceTransforms.GetBounds(authored.Source),
            EditorSpatialObject spatial => spatial.Bounds.Value,
            _ => null
        };
}
