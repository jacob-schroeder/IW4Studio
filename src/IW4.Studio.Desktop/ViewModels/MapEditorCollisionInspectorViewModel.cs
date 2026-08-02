using IW4.Studio.MapEditor.Compilation.Collision;
using IW4.Studio.MapEditor.Compilation.StaticModels;
using IW4.Studio.MapEditor.Editing.Collision;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.Editing.SavePlanning;

namespace IW4.Studio.Desktop.ViewModels;

/// <summary>
/// Contextual, read-only description of collision authority. It deliberately
/// reports explicit correspondence evidence and never invents a graphics
/// counterpart or ownership relationship for brush or triangle topology.
/// </summary>
public sealed class MapEditorCollisionInspectorViewModel : ObservableObject
{
    private readonly EditorMapObject _collision;
    private readonly StaticModelCorrespondenceCatalog
        _staticModelCorrespondences;
    private readonly MapAssetKind? _collisionAssetKind;

    public MapEditorCollisionInspectorViewModel(
        EditorMapObject collision,
        StaticModelCorrespondenceCatalog staticModelCorrespondences,
        MapAssetKind? collisionAssetKind)
    {
        ArgumentNullException.ThrowIfNull(collision);
        if (!MapEditorCollisionBrowserViewModel.IsCollisionObject(collision))
        {
            throw new ArgumentException(
                "A collision inspector requires a collision semantic object.",
                nameof(collision));
        }

        _collision = collision;
        _staticModelCorrespondences = staticModelCorrespondences ??
            throw new ArgumentNullException(
                nameof(staticModelCorrespondences));
        _collisionAssetKind = collisionAssetKind;
        CompilationContract =
            MapEditorCollisionCompilationAssessment.Create(
                collision,
                staticModelCorrespondences);
    }

    public MapEditorCollisionCompilationAssessment CompilationContract
    {
        get;
    }

    public string CategoryText =>
        _collision switch
        {
            EditorCollisionObject
            {
                CollisionKind: CollisionObjectKind.Brush
            } => "Collision Brush Volume",
            EditorCollisionObject =>
                "Collision Triangle",
            EditorStaticModel =>
                "Static-Model Collision",
            EditorAuthoredCollisionObject
            {
                Source.GeometryKind:
                    CollisionGeometryKind.ConvexBrush
            } => "Authored Collision Brush",
            EditorAuthoredCollisionObject
            {
                Source.GeometryKind:
                    CollisionGeometryKind.TriangleMesh
            } => "Authored Collision Mesh",
            EditorAuthoredCollisionObject =>
                "Authored Static-Model Collision",
            _ => "Collision"
        };

    public string SourceIdentityText =>
        _collision is EditorAuthoredCollisionObject authored
            ? $"{AuthorityText} · {SplitWords(
                authored.Source.Ownership.Category.ToString())}"
            : $"{AuthorityText} · source row #{SourceOrdinal}";

    public bool IsReadOnly =>
        _collision is not EditorAuthoredCollisionObject;

    public string AuthorityBadgeText =>
        IsReadOnly
            ? "IMPORTED · READ ONLY"
            : "AUTHORED SOURCE";

    public string AuthorityText
    {
        get
        {
            if (_collision is EditorAuthoredCollisionObject)
                return "Canonical editor source";

            MapAssetKind? assetKind =
                _collisionAssetKind ??
                _staticModelCorrespondences.CollisionAssetKind;
            return assetKind is { } value
                ? SplitWords(value.ToString())
                : "Collision source (authority unresolved)";
        }
    }

    public string BoundsText =>
        _collision switch
        {
            EditorCollisionObject value =>
                value.Bounds.Value?.ToString() ?? "Unavailable",
            EditorStaticModel value =>
                value.Bounds.Value?.ToString() ?? "Unavailable",
            EditorAuthoredCollisionObject value =>
                AuthoredCollisionSourceTransforms
                    .GetBounds(value.Source)
                    .ToString(),
            _ => "Unavailable"
        };

    public bool HasContents =>
        _collision is EditorCollisionObject ||
        _collision is EditorAuthoredCollisionObject
        {
            Source: AuthoredConvexBrushCollisionSource or
                AuthoredIndexedTriangleMeshCollisionSource
        };

    public string ContentsText =>
        _collision switch
        {
            EditorCollisionObject { Contents.Value: { } contents } =>
                $"0x{contents:X8}",
            EditorAuthoredCollisionObject
            {
                Source: AuthoredConvexBrushCollisionSource brush
            } => $"0x{brush.Contents:X8}",
            EditorAuthoredCollisionObject
            {
                Source: AuthoredIndexedTriangleMeshCollisionSource mesh
            } => string.Join(
                ", ",
                mesh.Materials
                    .Select(value => unchecked((uint)value.Contents))
                    .Distinct()
                    .Order()
                    .Select(value => $"0x{value:X8}")),
            _ => "Unavailable"
        };

    public string TopologyText =>
        _collision switch
        {
            EditorCollisionObject
            {
                CollisionKind: CollisionObjectKind.Brush
            } value =>
                $"{value.SupportingRecordCount.Value:N0} brush sides",
            EditorCollisionObject value =>
                $"{value.SupportingRecordCount.Value:N0} triangle vertices",
            EditorStaticModel value =>
                value.ModelName.Value ?? "(unresolved model)",
            EditorAuthoredCollisionObject
            {
                Source: AuthoredConvexBrushCollisionSource brush
            } => $"{brush.Faces.Count:N0} canonical faces",
            EditorAuthoredCollisionObject
            {
                Source: AuthoredIndexedTriangleMeshCollisionSource mesh
            } =>
                $"{mesh.Triangles.Count:N0} triangles · " +
                $"{mesh.Vertices.Count:N0} vertices",
            EditorAuthoredCollisionObject
            {
                Source: AuthoredPairedStaticModelCollisionSource model
            } => model.ExactSerializedModelName,
            _ => "Unavailable"
        };

    public string ViewportRepresentationText =>
        _collision switch
        {
            EditorCollisionObject
            {
                CollisionKind: CollisionObjectKind.Triangle,
                Bounds.Value: { IsFinite: true }
            } => "Exact imported triangle wireframe.",
            EditorCollisionObject
            {
                CollisionKind: CollisionObjectKind.Triangle
            } => "No viewport wireframe: retained triangle bounds are " +
                 "unavailable or non-finite.",
            EditorCollisionObject
            {
                Bounds.Value: { IsFinite: true }
            } =>
                "Bounds proxy only; not a plane-clipped brush hull.",
            EditorCollisionObject =>
                "No viewport proxy: retained brush bounds are unavailable " +
                "or non-finite.",
            EditorStaticModel
            {
                Bounds.Value: { IsFinite: true }
            } =>
                "Bounds proxy only; not a rendered collision hull.",
            EditorStaticModel =>
                "No viewport proxy: retained static-model bounds are " +
                "unavailable or non-finite.",
            EditorAuthoredCollisionObject =>
                "Canonical authored bounds follow the live translation " +
                "draft. Exact collision geometry remains isolated in the " +
                "offline M3 candidate.",
            _ => "Viewport representation unavailable."
        };

    public string RelationshipHeadingText
    {
        get
        {
            if (_collision is EditorAuthoredCollisionObject authored)
            {
                return authored.Source.Ownership.Category ==
                       CollisionOwnershipCategory.StandaloneWorld
                    ? "EXPLICIT STANDALONE OWNER"
                    : "EXPLICIT COUNTERPART OWNER";
            }
            if (_collision is EditorCollisionObject)
                return "NO PROVEN OWNER RELATIONSHIP";
            if (_collision is EditorStaticModel
                {
                    AuthoredDuplicatePair: not null
                })
            {
                return "EXPLICIT RENDER LINK";
            }

            return _staticModelCorrespondences.TryGetByCollisionObjectId(
                       _collision.Id,
                       out StaticModelCompilationRelationship? relationship) &&
                   relationship is not null
                ? "EXPLICIT RENDER LINK"
                : "NO PROVEN RENDER LINK";
        }
    }

    public string RelationshipText
    {
        get
        {
            if (_collision is EditorAuthoredCollisionObject authored)
            {
                return authored.Source.Ownership.Counterpart is
                    { } counterpart
                    ? $"Canonical source retains explicit " +
                      $"{SplitWords(counterpart.Kind.ToString())} " +
                      $"counterpart {counterpart.ObjectId}."
                    : "Canonical source explicitly owns standalone world " +
                      "collision and requires no graphics counterpart.";
            }
            if (_collision is EditorCollisionObject)
            {
                return "No graphics counterpart or standalone owner " +
                       "relationship is proven for this imported topology. " +
                       "The editor does not infer one.";
            }
            if (_collision is EditorStaticModel
                {
                    AuthoredDuplicatePair: { } pair
                })
            {
                return $"Authored pair with render object " +
                       $"{pair.RenderObjectId}.";
            }
            if (_staticModelCorrespondences.TryGetByCollisionObjectId(
                    _collision.Id,
                    out StaticModelCompilationRelationship? relationship) &&
                relationship is not null)
            {
                return $"Exact-bundle correspondence to render object " +
                       $"{relationship.RenderObjectId}.";
            }
            if (_staticModelCorrespondences.TryGetAssessment(
                    _collision.Id,
                    out StaticModelCorrespondenceAssessment? assessment) &&
                assessment is not null)
            {
                return assessment.Evidence;
            }

            return "No graphics counterpart relationship is proven or " +
                   "inferred.";
        }
    }

    public string ProvenanceSummaryText =>
        string.Join(
            " · ",
            _collision.Properties
                .Select(value => SplitWords(value.Provenance.ToString()))
                .Distinct(StringComparer.Ordinal));

    public string EditingBoundaryText =>
        IsReadOnly
            ? "Imported collision remains inspection-only. Create a " +
              "canonical authored source to move, reshape, or remove " +
              "collision without mutating retained compiled topology."
            : "Use Translate and drag in the world viewport. Apply creates " +
              "one undoable source edit; Cancel discards the draft. The " +
              "offline M3 candidate accepts the source, while FastFile " +
              "output remains blocked until M4 consumer acceptance and M7 " +
              "linking.";

    public void Refresh()
    {
        OnPropertyChanged(nameof(BoundsText));
        OnPropertyChanged(nameof(TopologyText));
        OnPropertyChanged(nameof(RelationshipHeadingText));
        OnPropertyChanged(nameof(RelationshipText));
        OnPropertyChanged(nameof(ProvenanceSummaryText));
        OnPropertyChanged(nameof(SourceIdentityText));
        OnPropertyChanged(nameof(IsReadOnly));
        OnPropertyChanged(nameof(AuthorityBadgeText));
        OnPropertyChanged(nameof(ContentsText));
        OnPropertyChanged(nameof(ViewportRepresentationText));
        OnPropertyChanged(nameof(EditingBoundaryText));
    }

    private int SourceOrdinal =>
        _collision switch
        {
            EditorCollisionObject value => value.SourceOrdinal.Value,
            EditorStaticModel value => value.SourceOrdinal.Value,
            _ => -1
        };

    private static string SplitWords(string value) =>
        string.Concat(value.Select((character, index) =>
            index > 0 && char.IsUpper(character)
                ? $" {character}"
                : character.ToString()));
}
