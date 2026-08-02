using System.Collections.ObjectModel;
using IW4.Studio.MapEditor.Compilation.Collision;
using IW4.Studio.MapEditor.Compilation.StaticModels;
using IW4.Studio.MapEditor.Editing.Collision;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.Editing.SavePlanning;

namespace IW4.Studio.Desktop.ViewModels;

/// <summary>
/// Editor-facing resolution state for the M0 collision ownership contract.
/// Unresolved is intentionally distinct from standalone world ownership:
/// absence of a counterpart is not evidence of independent ownership.
/// </summary>
public enum MapEditorCollisionOwnershipResolution
{
    Unresolved = 0,
    ExplicitStandaloneWorld = 1,
    ExplicitPairedStaticModel = 2,
    ExplicitBrushModelEntity = 3
}

public enum MapEditorCollisionCompilationGateKind
{
    Ownership = 0,
    SerializedDomainWidths = 1,
    ConsumerProvenLocalRecords = 2,
    IndexContributions = 3,
    ColMapEmission = 4
}

public enum MapEditorCollisionCompilationGateStatus
{
    Satisfied = 0,
    Blocked = 1
}

public enum MapEditorCollisionCompilationReadiness
{
    BlockedByOwnership = 0,
    BlockedByIndexContributions = 1,
    OfflineStructuralCandidate = 2
}

public sealed record MapEditorCollisionCompilationGate(
    MapEditorCollisionCompilationGateKind Kind,
    MapEditorCollisionCompilationGateStatus Status,
    string Detail);

/// <summary>
/// Read-only projection from one world-viewer collision selection into the
/// typed M0 compilation vocabulary. This assessment never changes the map
/// document and never promotes unproven topology to standalone ownership.
/// </summary>
public sealed class MapEditorCollisionCompilationAssessment
{
    private readonly IReadOnlyList<MapEditorCollisionCompilationGate> _gates;

    private MapEditorCollisionCompilationAssessment(
        CollisionGeometryKind geometryKind,
        CollisionSourceProvenance sourceProvenance,
        MapEditorCollisionOwnershipResolution ownershipResolution,
        CollisionOwnershipCategory? ownership,
        CollisionCounterpartIdentity? counterpart,
        MapEditorCollisionCompilationReadiness readiness,
        IEnumerable<MapEditorCollisionCompilationGate> gates)
    {
        ArgumentNullException.ThrowIfNull(gates);

        GeometryKind = geometryKind;
        SourceProvenance = sourceProvenance;
        OwnershipResolution = ownershipResolution;
        Ownership = ownership;
        Counterpart = counterpart;
        Readiness = readiness;
        _gates = new ReadOnlyCollection<
            MapEditorCollisionCompilationGate>(gates.ToArray());
    }

    public CollisionGeometryKind GeometryKind { get; }
    public CollisionSourceProvenance SourceProvenance { get; }
    public MapEditorCollisionOwnershipResolution OwnershipResolution
    {
        get;
    }
    public CollisionOwnershipCategory? Ownership { get; }
    public CollisionCounterpartIdentity? Counterpart { get; }
    public MapEditorCollisionCompilationReadiness Readiness { get; }
    public IReadOnlyList<MapEditorCollisionCompilationGate> Gates => _gates;

    public bool HasResolvedOwnership =>
        OwnershipResolution !=
            MapEditorCollisionOwnershipResolution.Unresolved;

    public bool IsCompilationReady =>
        _gates.All(value =>
            value.Status ==
                MapEditorCollisionCompilationGateStatus.Satisfied);

    public string ContractIdentityText =>
        CollisionCompilerContractManifest.Current.Contract.ToString();

    public string GeometryText =>
        GeometryKind switch
        {
            CollisionGeometryKind.ConvexBrush => "Convex Brush",
            CollisionGeometryKind.TriangleMesh => "Triangle Mesh",
            CollisionGeometryKind.StaticModelHull => "Static-Model Hull",
            _ => throw new InvalidOperationException(
                $"Unsupported collision geometry kind {GeometryKind}.")
        };

    public string ProvenanceText =>
        SourceProvenance switch
        {
            CollisionSourceProvenance.Imported => "Imported",
            CollisionSourceProvenance.Authored => "Authored",
            _ => throw new InvalidOperationException(
                $"Unsupported collision provenance {SourceProvenance}.")
        };

    public string OwnershipText =>
        Ownership switch
        {
            CollisionOwnershipCategory.PairedStaticModel =>
                "Paired Static Model",
            CollisionOwnershipCategory.StandaloneWorld =>
                "Standalone World Collision",
            CollisionOwnershipCategory.BrushModelEntity =>
                "Brush-Model Entity",
            null => "Unresolved — no owner inferred",
            _ => throw new InvalidOperationException(
                "The world viewer cannot display an ownership category " +
                "without explicit authority.")
        };

    public string ReadinessText =>
        Readiness ==
            MapEditorCollisionCompilationReadiness
                .OfflineStructuralCandidate
            ? "OFFLINE CANDIDATE"
            : "NOT COMPILATION READY";

    public string GateSummaryText =>
        string.Join(
            " · ",
            _gates.Select(value =>
                $"{GateName(value.Kind)} " +
                $"{value.Status.ToString().ToLowerInvariant()}"));

    public string ReadinessDetailText =>
        Readiness switch
        {
            MapEditorCollisionCompilationReadiness.BlockedByOwnership =>
                "Resolve an explicit semantic owner before index planning. " +
                "Standalone world ownership is never inferred.",
            MapEditorCollisionCompilationReadiness
                .BlockedByIndexContributions =>
                "Ownership is explicit. Complete source contributions and " +
                "shared ColMap topology remain blocked.",
            MapEditorCollisionCompilationReadiness
                .OfflineStructuralCandidate =>
                "Canonical authored geometry can enter the detached M3 " +
                "structural candidate. FastFile emission remains blocked " +
                "until M4 consumer acceptance and M7 linking.",
            _ => throw new InvalidOperationException(
                $"Unsupported collision readiness {Readiness}.")
        };

    public static MapEditorCollisionCompilationAssessment Create(
        EditorMapObject collision,
        StaticModelCorrespondenceCatalog staticModelCorrespondences)
    {
        ArgumentNullException.ThrowIfNull(collision);
        ArgumentNullException.ThrowIfNull(staticModelCorrespondences);
        if (!MapEditorCollisionBrowserViewModel.IsCollisionObject(collision))
        {
            throw new ArgumentException(
                "A collision compilation assessment requires a collision " +
                "semantic object.",
                nameof(collision));
        }

        if (collision is EditorAuthoredCollisionObject authored)
            return CreateAuthored(authored);

        CollisionGeometryKind geometryKind = collision switch
        {
            EditorCollisionObject
            {
                CollisionKind: CollisionObjectKind.Brush
            } => CollisionGeometryKind.ConvexBrush,
            EditorCollisionObject
            {
                CollisionKind: CollisionObjectKind.Triangle
            } => CollisionGeometryKind.TriangleMesh,
            EditorStaticModel
            {
                Representation: StaticModelRepresentation.Collision
            } => CollisionGeometryKind.StaticModelHull,
            _ => throw new ArgumentException(
                "Unsupported collision semantic object.",
                nameof(collision))
        };
        CollisionSourceProvenance provenance = collision switch
        {
            EditorStaticModel { IsImported: false } =>
                CollisionSourceProvenance.Authored,
            // EditorCollisionObject currently represents retained imported
            // ColMap topology. Future authored brush/triangle objects require
            // an explicit lineage field rather than property-value inference.
            _ => CollisionSourceProvenance.Imported
        };

        CollisionCounterpartIdentity? counterpart =
            ResolveExplicitStaticModelCounterpart(
                collision,
                staticModelCorrespondences);
        bool ownershipResolved = counterpart is not null;

        return new MapEditorCollisionCompilationAssessment(
            geometryKind,
            provenance,
            ownershipResolved
                ? MapEditorCollisionOwnershipResolution
                    .ExplicitPairedStaticModel
                : MapEditorCollisionOwnershipResolution.Unresolved,
            ownershipResolved
                ? CollisionOwnershipCategory.PairedStaticModel
                : null,
            counterpart,
            ownershipResolved
                ? MapEditorCollisionCompilationReadiness
                    .BlockedByIndexContributions
                : MapEditorCollisionCompilationReadiness
                    .BlockedByOwnership,
            CreateGates(ownershipResolved));
    }

    private static MapEditorCollisionCompilationAssessment CreateAuthored(
        EditorAuthoredCollisionObject authored)
    {
        CollisionSourceOwnership ownership = authored.Source.Ownership;
        MapEditorCollisionOwnershipResolution resolution =
            ownership.Category switch
            {
                CollisionOwnershipCategory.StandaloneWorld =>
                    MapEditorCollisionOwnershipResolution
                        .ExplicitStandaloneWorld,
                CollisionOwnershipCategory.PairedStaticModel =>
                    MapEditorCollisionOwnershipResolution
                        .ExplicitPairedStaticModel,
                CollisionOwnershipCategory.BrushModelEntity =>
                    MapEditorCollisionOwnershipResolution
                        .ExplicitBrushModelEntity,
                _ => throw new InvalidOperationException(
                    $"Unsupported authored collision ownership " +
                    $"{ownership.Category}.")
            };

        return new MapEditorCollisionCompilationAssessment(
            authored.Source.GeometryKind,
            CollisionSourceProvenance.Authored,
            resolution,
            ownership.Category,
            ownership.Counterpart,
            MapEditorCollisionCompilationReadiness
                .OfflineStructuralCandidate,
            CreateAuthoredGates(ownership.Category));
    }

    private static CollisionCounterpartIdentity?
        ResolveExplicitStaticModelCounterpart(
            EditorMapObject collision,
            StaticModelCorrespondenceCatalog staticModelCorrespondences)
    {
        if (collision is not EditorStaticModel staticModel)
            return null;

        if (staticModel.AuthoredDuplicatePair is { } authoredPair)
        {
            if (authoredPair.CollisionAssetKind != MapAssetKind.ColMapMp)
                return null;

            return new CollisionCounterpartIdentity(
                authoredPair.RenderObjectId,
                CollisionCounterpartKind.RenderStaticModel);
        }

        return staticModelCorrespondences.CollisionAssetKind ==
                   MapAssetKind.ColMapMp &&
               staticModelCorrespondences.AuthoritiesValid &&
               staticModelCorrespondences.TryGetByCollisionObjectId(
                   staticModel.Id,
                   out StaticModelCompilationRelationship? relationship) &&
               relationship is not null
            ? new CollisionCounterpartIdentity(
                relationship.RenderObjectId,
                CollisionCounterpartKind.RenderStaticModel)
            : null;
    }

    private static IEnumerable<MapEditorCollisionCompilationGate>
        CreateGates(bool ownershipResolved)
    {
        yield return new MapEditorCollisionCompilationGate(
            MapEditorCollisionCompilationGateKind.Ownership,
            ownershipResolved
                ? MapEditorCollisionCompilationGateStatus.Satisfied
                : MapEditorCollisionCompilationGateStatus.Blocked,
            ownershipResolved
                ? "An exact static-model counterpart identity is retained."
                : "No explicit owner identity is retained.");
        yield return new MapEditorCollisionCompilationGate(
            MapEditorCollisionCompilationGateKind.SerializedDomainWidths,
            MapEditorCollisionCompilationGateStatus.Satisfied,
            "Known ColMap root cardinalities and scalar-array widths are " +
            "versioned, with a fixed-payload block-capacity lower bound. " +
            "Final variable/linker capacity remains an emission gate.");
        yield return new MapEditorCollisionCompilationGate(
            MapEditorCollisionCompilationGateKind
                .ConsumerProvenLocalRecords,
            MapEditorCollisionCompilationGateStatus.Satisfied,
            "Signed BSP children, typed leaf-brush traversal, partition " +
            "vertex segments, leaf/AABB ranges and selectors, and the " +
            "static-model virtual child namespace are locked. Construction " +
            "and whole-graph canonicalization remain open.");
        yield return new MapEditorCollisionCompilationGate(
            MapEditorCollisionCompilationGateKind.IndexContributions,
            MapEditorCollisionCompilationGateStatus.Blocked,
            "This selection does not establish a complete per-domain source " +
            "projection.");
        yield return new MapEditorCollisionCompilationGate(
            MapEditorCollisionCompilationGateKind.ColMapEmission,
            MapEditorCollisionCompilationGateStatus.Blocked,
            "No deterministic ColMap builder consumes this selection yet.");
    }

    private static IEnumerable<MapEditorCollisionCompilationGate>
        CreateAuthoredGates(CollisionOwnershipCategory ownership)
    {
        yield return new MapEditorCollisionCompilationGate(
            MapEditorCollisionCompilationGateKind.Ownership,
            MapEditorCollisionCompilationGateStatus.Satisfied,
            $"Explicit {SplitWords(ownership.ToString())} ownership is " +
            "part of the canonical source.");
        yield return new MapEditorCollisionCompilationGate(
            MapEditorCollisionCompilationGateKind.SerializedDomainWidths,
            MapEditorCollisionCompilationGateStatus.Satisfied,
            "The candidate validates every fixed-width ColMap index and " +
            "serialized capacity before allocation.");
        yield return new MapEditorCollisionCompilationGate(
            MapEditorCollisionCompilationGateKind
                .ConsumerProvenLocalRecords,
            MapEditorCollisionCompilationGateStatus.Satisfied,
            "Canonical planes, windings, materials, indices, and bounds are " +
            "source-owned and compile without mutating imported records.");
        yield return new MapEditorCollisionCompilationGate(
            MapEditorCollisionCompilationGateKind.IndexContributions,
            MapEditorCollisionCompilationGateStatus.Satisfied,
            "The M3 detached candidate assigns deterministic material, " +
            "brush, triangle, partition, leaf, model, and AABB domains.");
        yield return new MapEditorCollisionCompilationGate(
            MapEditorCollisionCompilationGateKind.ColMapEmission,
            MapEditorCollisionCompilationGateStatus.Blocked,
            "The result has offline-validation authority only. M4 consumer " +
            "acceptance and M7 linking/emission are still required.");
    }

    private static string GateName(
        MapEditorCollisionCompilationGateKind kind) =>
        kind switch
        {
            MapEditorCollisionCompilationGateKind.Ownership => "Ownership",
            MapEditorCollisionCompilationGateKind.SerializedDomainWidths =>
                "Root widths",
            MapEditorCollisionCompilationGateKind
                .ConsumerProvenLocalRecords =>
                "Proven locals",
            MapEditorCollisionCompilationGateKind.IndexContributions =>
                "Index plan",
            MapEditorCollisionCompilationGateKind.ColMapEmission =>
                "Emission",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static string SplitWords(string value) =>
        string.Concat(value.Select((character, index) =>
            index > 0 && char.IsUpper(character)
                ? $" {character}"
                : character.ToString()));
}
