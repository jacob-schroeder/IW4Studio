using System.Collections.ObjectModel;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.Editing.Provenance;

namespace IW4.Studio.MapEditor.Compilation.Import;

public sealed record MapImportCount(string Category, int Count);

public sealed record MapProvenanceCount(
    MapValueProvenance Provenance,
    int Count);

public sealed class MapImportAudit
{
    private readonly IReadOnlyList<MapImportCount> _semanticCounts;
    private readonly IReadOnlyList<MapProvenanceCount> _provenanceCounts;
    private readonly IReadOnlyList<string> _unresolvedCrossAssetJoins;
    private readonly IReadOnlyList<string> _diagnostics;

    internal MapImportAudit(
        EditorMapDocument document,
        IEnumerable<string> unresolvedCrossAssetJoins,
        IEnumerable<string> diagnostics,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(unresolvedCrossAssetJoins);
        ArgumentNullException.ThrowIfNull(diagnostics);
        cancellationToken.ThrowIfCancellationRequested();

        _semanticCounts = Array.AsReadOnly<MapImportCount>(
        [
            new("World surfaces", document.WorldSurfaces.Count),
            new("Render static models", document.StaticModels.Count(value =>
                value.Representation == StaticModelRepresentation.Render)),
            new("Collision static models", document.StaticModels.Count(value =>
                value.Representation == StaticModelRepresentation.Collision)),
            new("Collision brushes", document.Collision.Count(value =>
                value.CollisionKind == CollisionObjectKind.Brush)),
            new("Collision triangles", document.Collision.Count(value =>
                value.CollisionKind == CollisionObjectKind.Triangle)),
            new(
                "MapEnt entities (exact syntax projection)",
                document.Entities.Count),
            new("Primary lights", document.PrimaryLights.Count),
            new("FX glass objects", document.Glass.Count(value =>
                value.Representation != GlassRepresentation.GameplayPiece)),
            new("Gameplay glass objects", document.Glass.Count(value =>
                value.Representation == GlassRepresentation.GameplayPiece)),
            new("Spatial debug objects", document.SpatialDebugObjects.Count)
        ]);

        cancellationToken.ThrowIfCancellationRequested();
        IEnumerable<MapValueProvenance> provenance = document.Objects
            .SelectMany(value => value.Properties)
            .Select(value => value.Provenance)
            .Concat(document.Environment.Values.Select(value => value.Provenance));
        if (document.EntitySource is not null)
            provenance = provenance.Append(document.EntitySource.Provenance);
        _provenanceCounts = Array.AsReadOnly(
            provenance
                .GroupBy(value => value)
                .OrderBy(group => group.Key)
                .Select(group => new MapProvenanceCount(
                    group.Key,
                    group.Count()))
                .ToArray());
        cancellationToken.ThrowIfCancellationRequested();
        _unresolvedCrossAssetJoins = Array.AsReadOnly(
            unresolvedCrossAssetJoins
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray());
        _diagnostics = Array.AsReadOnly(
            diagnostics
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray());
    }

    public IReadOnlyList<MapImportCount> SemanticCounts => _semanticCounts;
    public IReadOnlyList<MapProvenanceCount> ProvenanceCounts => _provenanceCounts;
    public IReadOnlyList<string> UnresolvedCrossAssetJoins =>
        _unresolvedCrossAssetJoins;
    public IReadOnlyList<string> Diagnostics => _diagnostics;
}
