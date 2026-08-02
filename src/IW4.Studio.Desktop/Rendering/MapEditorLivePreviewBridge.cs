using System.Numerics;
using IW4.Render.EditorPreview;
using IW4.Render.Geometry;
using IW4.Render.Picking;
using IW4.Studio.MapEditor.Editing.Commands;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.Editing.Projection;

namespace IW4.Studio.Desktop.Rendering;

/// <summary>
/// Projects one semantic editor document into the immutable Live Preview
/// contract. Primary-light values and the complete imported Gfx static-model
/// catalog are consumed by the renderer. The map document stays authoritative
/// and no compiled asset is mutated.
/// </summary>
public sealed class MapEditorLivePreviewBridge :
    IMapEditorLivePreviewSource,
    IMapEditorLivePreviewSelectionSource,
    IMapEditorLivePreviewPickSink,
    IDisposable
{
    private readonly object _gate = new();
    private readonly object _projectionPublicationGate = new();
    private readonly IEditorSceneProjector _projector;
    private EditorSceneSnapshot _semanticSnapshot;
    private MapRenderLiveSceneProjection _renderProjection;
    private MapObjectId? _selection;
    private bool _disposed;

    public MapEditorLivePreviewBridge(
        EditorMapDocument document,
        IEditorSceneProjector? projector = null)
    {
        Document = document ??
            throw new ArgumentNullException(nameof(document));
        _projector = projector ?? new EditorSceneProjector();
        _semanticSnapshot = _projector.Project(document);
        _renderProjection = Adapt(_semanticSnapshot);
        document.Changed += Document_Changed;
    }

    public EditorMapDocument Document { get; }

    public EditorSceneSnapshot CurrentSemanticSnapshot
    {
        get
        {
            lock (_gate)
                return _semanticSnapshot;
        }
    }

    public MapRenderLiveSceneProjection CurrentProjection
    {
        get
        {
            lock (_gate)
                return _renderProjection;
        }
    }

    public MapObjectId? CurrentSelection
    {
        get
        {
            lock (_gate)
                return _selection;
        }
    }

    public event EventHandler<MapEditorLivePreviewChangedEventArgs>?
        ProjectionChanged;

    public event EventHandler<
        MapEditorLivePreviewSelectionChangedEventArgs>? SelectionChanged;

    public void SetSelection(MapObjectId? selection)
    {
        ThrowIfDisposed();
        if (selection is { } objectId)
        {
            EditorSceneSnapshot snapshot;
            lock (_gate)
                snapshot = _semanticSnapshot;
            if (!snapshot.TryGetObject(objectId, out _))
            {
                throw new ArgumentException(
                    $"Map object {objectId} is not present in the current editor scene.",
                    nameof(selection));
            }
        }

        PublishSelection(selection);
    }

    bool IMapEditorLivePreviewPickSink.PublishPick(
        MapRenderPickHit? hit)
    {
        ThrowIfDisposed();
        if (hit is { } value &&
            MapEditorLivePreviewPickResolver.TryResolve(
                Document,
                value,
                out MapObjectId selection))
        {
            PublishSelection(selection);
            return true;
        }

        PublishSelection(null);
        return false;
    }

    public void Dispose()
    {
        lock (_projectionPublicationGate)
        {
            if (_disposed)
                return;

            _disposed = true;
            Document.Changed -= Document_Changed;
        }
    }

    private void Document_Changed(
        object? sender,
        MapDocumentChangedEventArgs e)
    {
        lock (_projectionPublicationGate)
        {
            if (_disposed)
                return;

            EditorSceneSnapshot previous;
            lock (_gate)
                previous = _semanticSnapshot;
            if (e.Revision <= previous.Revision)
                return;

            EditorSceneSnapshot next =
                _projector.Project(Document, previous);
            MapRenderLiveSceneProjection adapted = Adapt(next);
            bool selectionCleared = false;
            lock (_gate)
            {
                if (next.Revision <= _semanticSnapshot.Revision)
                    return;

                _semanticSnapshot = next;
                _renderProjection = adapted;
                if (_selection is { } selected &&
                    !next.TryGetObject(selected, out _))
                {
                    _selection = null;
                    selectionCleared = true;
                }
            }

            ProjectionChanged?.Invoke(
                this,
                new MapEditorLivePreviewChangedEventArgs(adapted));
            if (selectionCleared)
            {
                SelectionChanged?.Invoke(
                    this,
                    new MapEditorLivePreviewSelectionChangedEventArgs(null));
            }
        }
    }

    private static MapRenderLiveSceneProjection Adapt(
        EditorSceneSnapshot snapshot) =>
        new(
            snapshot.Revision,
            snapshot.PrimaryLights.Select(light =>
                new MapRenderLivePrimaryLight(
                    light.SourceOrdinal,
                    new Vector3(
                        light.Color.X,
                        light.Color.Y,
                        light.Color.Z),
                    light.Exponent,
                    light.CosHalfFovInner,
                    light.Visibility ==
                        EditorObjectVisibility.Visible)),
            snapshot.StaticModels
                // The renderer-facing catalog must remain a complete,
                // baseline-ordinal Gfx table. Authored duplicates have no
                // loaded draw group until their compiled output is reopened.
                .Where(model =>
                    model.Representation ==
                        StaticModelRepresentation.Render &&
                    model.CompiledDisposition !=
                        StaticModelCompiledDisposition.AuthoredPending)
                .Select(model =>
                    new MapRenderLiveStaticModelTranslation(
                        model.SourceOrdinal,
                        new Vector3(
                            model.Origin.X,
                            model.Origin.Y,
                            model.Origin.Z),
                        model.Visibility ==
                            EditorObjectVisibility.Visible &&
                        model.CompiledDisposition ==
                            StaticModelCompiledDisposition
                                .BaselinePresent)));

    private void PublishSelection(MapObjectId? selection)
    {
        bool changed;
        lock (_gate)
        {
            changed = _selection != selection;
            _selection = selection;
        }
        if (!changed)
            return;

        SelectionChanged?.Invoke(
            this,
            new MapEditorLivePreviewSelectionChangedEventArgs(selection));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(
                nameof(MapEditorLivePreviewBridge));
        }
    }
}

/// <summary>
/// Resolves renderer-local pick indices through the imported semantic
/// ordinals. No name, transform, proximity, or list-position heuristic is
/// accepted as object identity.
/// </summary>
internal static class MapEditorLivePreviewPickResolver
{
    internal static bool TryResolve(
        EditorMapDocument document,
        MapRenderPickHit hit,
        out MapObjectId objectId)
    {
        ArgumentNullException.ThrowIfNull(document);

        IEnumerable<MapObjectId> matches = hit.Kind switch
        {
            MapRenderPickKind.GfxSurface
                when hit.ObjectIndex == hit.SurfaceIndex =>
                document.WorldSurfaces
                    .Where(surface =>
                        surface.SourceOrdinal.Value ==
                        hit.SurfaceIndex)
                    .Select(surface => surface.Id),
            MapRenderPickKind.StaticModel =>
                document.StaticModels
                    .Where(model =>
                        model.Representation ==
                            StaticModelRepresentation.Render &&
                        model.SourceOrdinal.Value == hit.ObjectIndex)
                    .Select(model => model.Id),
            MapRenderPickKind.CollisionTriangle =>
                document.Collision
                    .Where(collision =>
                        collision.CollisionKind ==
                            CollisionObjectKind.Triangle &&
                        collision.SourceOrdinal.Value ==
                            hit.ObjectIndex)
                    .Select(collision => collision.Id),
            MapRenderPickKind.CollisionBrushBounds =>
                document.Collision
                    .Where(collision =>
                        collision.CollisionKind ==
                            CollisionObjectKind.Brush &&
                        collision.SourceOrdinal.Value ==
                            hit.ObjectIndex)
                    .Select(collision => collision.Id),
            MapRenderPickKind.CollisionStaticModelBounds =>
                document.StaticModels
                    .Where(model =>
                        model.Representation ==
                            StaticModelRepresentation.Collision &&
                        model.SourceOrdinal.Value == hit.ObjectIndex)
                    .Select(model => model.Id),
            _ => []
        };

        MapObjectId[] resolved = matches.Take(2).ToArray();
        if (resolved.Length == 1)
        {
            objectId = resolved[0];
            return true;
        }

        objectId = default;
        return false;
    }
}
