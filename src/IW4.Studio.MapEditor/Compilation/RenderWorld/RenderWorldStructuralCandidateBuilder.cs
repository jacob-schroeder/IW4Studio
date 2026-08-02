using System.Collections.ObjectModel;
using IW4.Studio.MapEditor.Compilation.Collision;
using IW4.Studio.MapEditor.Editing.Identity;

namespace IW4.Studio.MapEditor.Compilation.RenderWorld;

/// <summary>
/// Authority carried by a bounded M3 render result. No value in this enum
/// grants runtime consumption or persistence.
/// </summary>
public enum RenderWorldStructuralCandidateAuthority
{
    OfflineValidationOnly = 0
}

/// <summary>
/// Complete detached render-geometry candidate for offline structural
/// validation. It deliberately does not implement IGfxWorldBuildData and
/// retains no loaded asset, pointer, emitter, or runtime buffer state.
/// </summary>
public sealed class RenderWorldStructuralCandidate
{
    private readonly IReadOnlyList<RenderWorldStructuralBlocker> _blockers;

    internal RenderWorldStructuralCandidate(
        MapDocumentId documentId,
        long documentRevision,
        string mapAssetName,
        RenderWorldCompiledGeometry geometry,
        IEnumerable<RenderWorldStructuralBlocker> blockers,
        RenderWorldStructuralAssessment validationAssessment)
    {
        if (documentId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(documentId));
        if (documentRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(documentRevision));
        ArgumentException.ThrowIfNullOrWhiteSpace(mapAssetName);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(blockers);
        ArgumentNullException.ThrowIfNull(validationAssessment);

        DocumentId = documentId;
        DocumentRevision = documentRevision;
        MapAssetName = mapAssetName;
        Geometry = geometry;
        _blockers =
            new ReadOnlyCollection<RenderWorldStructuralBlocker>(
                blockers.ToArray());
        ValidationAssessment = validationAssessment;
    }

    public MapDocumentId DocumentId { get; }
    public long DocumentRevision { get; }
    public string MapAssetName { get; }
    public RenderWorldStructuralCandidateAuthority Authority =>
        RenderWorldStructuralCandidateAuthority.OfflineValidationOnly;
    public bool PersistenceAuthorized => false;
    public RenderWorldCompiledGeometry Geometry { get; }
    public RenderWorldStructuralAssessment ValidationAssessment { get; }
    public IReadOnlyList<RenderWorldStructuralBlocker> Blockers =>
        _blockers;

    public IReadOnlyList<AuthoredIndexedRenderMeshSource> Sources =>
        Geometry.Sources;
    public IReadOnlyList<byte> PackedPositionData =>
        Geometry.PackedPositionData;
    public IReadOnlyList<byte> PackedVertexLayerData =>
        Geometry.PackedVertexLayerData;
    public IReadOnlyList<ushort> Indices => Geometry.Indices;
    public IReadOnlyList<RenderWorldCompiledSurface> Surfaces =>
        Geometry.Surfaces;
    public IReadOnlyList<RenderWorldSourceSurfaceMapping>
        SourceToSurfaceMappings => Geometry.SourceMappings;
    public RenderWorldRange StandaloneWorldSurfaceRange =>
        Geometry.StandaloneWorldSurfaceRange;
    public IReadOnlyList<ushort> SortedWorldSurfaceOrdinals =>
        Geometry.SortedWorldSurfaceOrdinals;
    public IReadOnlyList<RenderWorldInlineModelSurfaceRange>
        InlineModelRanges => Geometry.InlineModels;
    public RenderWorldWorldModelSurfaceRange WorldModel =>
        Geometry.WorldModel;
    public CollisionInlineModelAllocationPlan InlineModelAllocationPlan =>
        Geometry.InlineModelAllocationPlan;
    public GfxMapVertexChecksumAssignment MapVertexChecksumAssignment =>
        Geometry.MapVertexChecksumAssignment;
    public uint MapVertexChecksum => Geometry.MapVertexChecksum;
}

/// <summary>
/// Composes deterministic vertex/surface and explicit submodel compilers, then
/// fails closed unless the detached graph passes the structural validator.
/// </summary>
public static class RenderWorldStructuralCandidateBuilder
{
    public static RenderWorldStructuralCandidate Compile(
        MapDocumentId documentId,
        long documentRevision,
        string mapAssetName,
        IEnumerable<AuthoredIndexedRenderMeshSource> sources,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        AuthoredIndexedRenderMeshSource[] sourceCopy = sources.ToArray();
        if (sourceCopy.Any(value =>
                value is not null &&
                value.Ownership.Kind ==
                    RenderMeshOwnershipKind.InlineBrushModel))
        {
            throw new InvalidOperationException(
                "Inline render sources require the overload that supplies " +
                "the shared CollisionInlineModelAllocationPlan.");
        }

        CollisionInlineModelAllocationPlan worldOnlyPlan =
            CollisionInlineModelAllocationPlan.Create([], []);
        return Compile(
            documentId,
            documentRevision,
            mapAssetName,
            sourceCopy,
            worldOnlyPlan,
            cancellationToken);
    }

    public static RenderWorldStructuralCandidate Compile(
        MapDocumentId documentId,
        long documentRevision,
        string mapAssetName,
        IEnumerable<AuthoredIndexedRenderMeshSource> sources,
        CollisionInlineModelAllocationPlan inlineModelAllocationPlan,
        CancellationToken cancellationToken = default)
    {
        if (documentId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(documentId));
        if (documentRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(documentRevision));
        string normalizedMapAssetName =
            MapCompilerContentIdentityInput
                .NormalizeMultiplayerMapAssetName(mapAssetName);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(inlineModelAllocationPlan);
        cancellationToken.ThrowIfCancellationRequested();

        RenderWorldSurfaceCompilation surfaceCompilation =
            RenderWorldSurfaceCompiler.Compile(
                sources,
                inlineModelAllocationPlan,
                cancellationToken);
        RenderWorldSubmodelPlan submodelPlan =
            RenderWorldSubmodelCompiler.Compile(
                surfaceCompilation.Surfaces,
                surfaceCompilation.SourceMappings,
                inlineModelAllocationPlan,
                cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var geometry = new RenderWorldCompiledGeometry(
            surfaceCompilation.OrderedSources,
            surfaceCompilation.PackedPositionData,
            surfaceCompilation.PackedVertexLayerData,
            surfaceCompilation.Indices,
            surfaceCompilation.Surfaces,
            surfaceCompilation.SourceMappings,
            submodelPlan.StandaloneWorldSurfaceRange,
            submodelPlan.SortedWorldSurfaceOrdinals,
            submodelPlan.WorldModel,
            submodelPlan.InlineModels,
            inlineModelAllocationPlan,
            GfxMapVertexChecksumPolicy.AssignStudioConstantZero());
        RenderWorldStructuralBlocker[] blockers =
            CreateDeferredBlockers();
        RenderWorldStructuralAssessment assessment =
            RenderWorldStructuralValidator.Assess(
                geometry,
                blockers);
        if (!assessment.IsValid)
        {
            throw new InvalidDataException(
                "The bounded M3 render candidate failed structural " +
                "validation: " +
                string.Join(
                    "; ",
                    assessment.Issues.Select(value =>
                        $"{value.Path}: {value.Detail}")));
        }

        return new RenderWorldStructuralCandidate(
            documentId,
            documentRevision,
            normalizedMapAssetName,
            geometry,
            blockers,
            assessment);
    }

    private static RenderWorldStructuralBlocker[]
        CreateDeferredBlockers() =>
    [
        new(
            RenderWorldDeferredMilestone.M4SpatialTopology,
            RenderWorldStructuralBlockerKind
                .CellsPortalsAabbAndVisibilityNotCompiled,
            "M4 owns cells, portals, AABB memberships, visibility " +
            "scheduling, and consumer acceptance."),
        new(
            RenderWorldDeferredMilestone.M4SpatialTopology,
            RenderWorldStructuralBlockerKind
                .FinalRuntimeBoundsNotCompiled,
            "M3 compiles immutable GfxBrushModel bounds and outward " +
            "local-origin radii; M4 owns surface/world spatial bounds and " +
            "writable runtime brush-model culling bounds."),
        new(
            RenderWorldDeferredMilestone.M5Lighting,
            RenderWorldStructuralBlockerKind
                .LightingAssignmentsNotCompiled,
            "M5 owns lightmap, reflection-probe, primary-light, and shadow " +
            "assignments for each surface."),
        new(
            RenderWorldDeferredMilestone.M5Lighting,
            RenderWorldStructuralBlockerKind.LightingBakesNotCompiled,
            "M5 owns lightmap, probe, and other baked lighting products; " +
            "M3 only preserves authored lightmap UV channels."),
        new(
            RenderWorldDeferredMilestone
                .M7AssetResolutionAndPersistence,
            RenderWorldStructuralBlockerKind
                .MaterialResolutionNotCompiled,
            "M3 orders surfaces by exact symbolic material identity; M7 " +
            "only resolves those symbols to MaterialAsset references and " +
            "records nested dependency provenance."),
        new(
            RenderWorldDeferredMilestone
                .M7AssetResolutionAndPersistence,
            RenderWorldStructuralBlockerKind
                .CompleteGfxWorldAssemblyNotCompiled,
            "The M3 payload/range graph is not a complete GfxWorldAsset or " +
            "IGfxWorldBuildData implementation. StudioConstantZeroV1 is " +
            "assigned to the unconsumed map-vertex word without claiming " +
            "retail producer parity."),
        new(
            RenderWorldDeferredMilestone
                .M7AssetResolutionAndPersistence,
            RenderWorldStructuralBlockerKind
                .LinkingEmissionAndPersistenceNotAuthorized,
            "No linker, emitter, asset-pool, Save As, or persistence path " +
            "is registered or authorized for this candidate.")
    ];
}
