using System.Collections.ObjectModel;
using IW4.Studio.MapEditor.Compilation.Collision;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;

namespace IW4.Studio.MapEditor.Compilation.RenderWorld.Visibility;

/// <summary>
/// The deliberately narrow M4 topology admitted by the first visibility
/// compiler. Changing this topology requires a new profile identity.
/// </summary>
public enum RenderWorldVisibilityTopologyProfile
{
    SingleCellAllStaticV1 = 0
}

public static class RenderWorldVisibilityProfile
{
    public const string CompilerIdentity =
        "iw4-studio.gfxworld.visibility.single-cell-all-static@1";
    public const int CellCount = 1;
    public const int PortalCount = 0;
    public const ushort SingleCellLeafNode = 1;
    public const int StaticVisibilityViewCount = 3;
    public const int SceneEntityCellBitWordCount = 512;

    public static int AlignedVisibilityWordCount(int elementCount)
    {
        if (elementCount < 0)
            throw new ArgumentOutOfRangeException(nameof(elementCount));

        // Native static visibility rows are allocated in 128-bit groups.
        return checked(((elementCount + 127) / 128) * 4);
    }
}

public enum RenderWorldVisibilityCandidateAuthority
{
    OfflineValidationOnly = 0
}

/// <summary>
/// One source-derived surface bound and its exact slot in the cell's sorted
/// surface membership. The bound is rounded outward in float space.
/// </summary>
public sealed class RenderWorldVisibilitySurfaceMembership
{
    internal RenderWorldVisibilitySurfaceMembership(
        int membershipOrdinal,
        ushort surfaceOrdinal,
        MapObjectId sourceObjectId,
        int sourceSurfaceOrdinal,
        MapBounds bounds)
    {
        if (membershipOrdinal < 0)
            throw new ArgumentOutOfRangeException(nameof(membershipOrdinal));
        if (sourceObjectId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(sourceObjectId));
        if (sourceSurfaceOrdinal < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceSurfaceOrdinal));
        }
        RenderWorldVisibilityOutwardBounds.RequireValid(
            bounds,
            nameof(bounds));

        MembershipOrdinal = membershipOrdinal;
        SurfaceOrdinal = surfaceOrdinal;
        SourceObjectId = sourceObjectId;
        SourceSurfaceOrdinal = sourceSurfaceOrdinal;
        Bounds = bounds;
    }

    public int MembershipOrdinal { get; }
    public ushort SurfaceOrdinal { get; }
    public MapObjectId SourceObjectId { get; }
    public int SourceSurfaceOrdinal { get; }
    public MapBounds Bounds { get; }
}

/// <summary>
/// Native GfxAabbTree-shaped root leaf. It owns one contiguous range in the
/// sorted world-surface array and no static-model membership.
/// </summary>
public sealed class RenderWorldVisibilityAabbLeaf
{
    private readonly IReadOnlyList<ushort> _staticModelOrdinals;

    internal RenderWorldVisibilityAabbLeaf(
        MapBounds bounds,
        RenderWorldRange sortedSurfaceRange)
    {
        RenderWorldVisibilityOutwardBounds.RequireValid(
            bounds,
            nameof(bounds));
        if (sortedSurfaceRange.IsEmpty ||
            sortedSurfaceRange.Start > ushort.MaxValue ||
            sortedSurfaceRange.Count > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sortedSurfaceRange));
        }

        Bounds = bounds;
        SortedSurfaceRange = sortedSurfaceRange;
        _staticModelOrdinals =
            Array.AsReadOnly(Array.Empty<ushort>());
    }

    public MapBounds Bounds { get; }
    public ushort ChildCount => 0;
    public ushort SurfaceCount =>
        checked((ushort)SortedSurfaceRange.Count);
    public ushort StartSurfaceIndex =>
        checked((ushort)SortedSurfaceRange.Start);
    public RenderWorldRange SortedSurfaceRange { get; }
    public ushort StaticModelIndexCount => 0;
    public IReadOnlyList<ushort> StaticModelOrdinals =>
        _staticModelOrdinals;
    public int ChildrenOffset => 0;
}

public sealed class RenderWorldVisibilityCellTree
{
    private readonly IReadOnlyList<RenderWorldVisibilityAabbLeaf>
        _aabbTrees;

    internal RenderWorldVisibilityCellTree(
        RenderWorldVisibilityAabbLeaf rootLeaf)
    {
        ArgumentNullException.ThrowIfNull(rootLeaf);
        _aabbTrees =
            new ReadOnlyCollection<RenderWorldVisibilityAabbLeaf>(
                [rootLeaf]);
    }

    public uint DeclaredAabbTreeCount => 1;
    public IReadOnlyList<RenderWorldVisibilityAabbLeaf> AabbTrees =>
        _aabbTrees;
    public RenderWorldVisibilityAabbLeaf RootLeaf => _aabbTrees[0];
}

public sealed class RenderWorldVisibilityCell
{
    internal RenderWorldVisibilityCell(
        int ordinal,
        MapBounds bounds,
        RenderWorldVisibilityCellTree tree)
    {
        if (ordinal < 0)
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        RenderWorldVisibilityOutwardBounds.RequireValid(
            bounds,
            nameof(bounds));
        ArgumentNullException.ThrowIfNull(tree);

        Ordinal = ordinal;
        Bounds = bounds;
        Tree = tree;
    }

    public int Ordinal { get; }
    public MapBounds Bounds { get; }
    public int PortalCount => RenderWorldVisibilityProfile.PortalCount;
    public RenderWorldVisibilityCellTree Tree { get; }
}

/// <summary>
/// Serialized/static DPVS cardinalities proven at M4. Lighting-dependent
/// partitions and the first six native visibility-count rows remain
/// deliberately unresolved rather than receiving invented values.
/// </summary>
public sealed class RenderWorldVisibilityStaticDpvsShape
{
    private readonly IReadOnlyList<uint?> _visibilityWordCounts;

    internal RenderWorldVisibilityStaticDpvsShape(int surfaceCount)
    {
        if (surfaceCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(surfaceCount));

        StaticSurfaceCount = checked((uint)surfaceCount);
        uint surfaceVisibilityWords = checked(
            (uint)RenderWorldVisibilityProfile
                .AlignedVisibilityWordCount(surfaceCount));
        _visibilityWordCounts =
            Array.AsReadOnly<uint?>(
            [
                null,
                null,
                null,
                null,
                null,
                null,
                0,
                surfaceVisibilityWords
            ]);
    }

    public uint StaticModelCount => 0;
    public uint StaticSurfaceCount { get; }
    public uint? LitSurfacesBegin => null;
    public uint? LitSurfacesEnd => null;
    public bool LightingPartitionResolved => false;
    public IReadOnlyList<uint?> VisibilityWordCounts =>
        _visibilityWordCounts;
}

/// <summary>
/// Runtime-owned allocation cardinalities only. No bit, material-key, portal,
/// or dynamic visibility state is authored by this object.
/// </summary>
public sealed class RenderWorldVisibilityRuntimeAllocationShape
{
    internal RenderWorldVisibilityRuntimeAllocationShape(
        int surfaceCount)
    {
        if (surfaceCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(surfaceCount));

        SurfaceVisibilityWordCount =
            RenderWorldVisibilityProfile.AlignedVisibilityWordCount(
                surfaceCount);
        SurfaceMaterialRowCount = surfaceCount;
    }

    public bool CarriesAuthoredRuntimeState => false;
    public int SceneEntityCellBitWordCount =>
        RenderWorldVisibilityProfile.SceneEntityCellBitWordCount;
    public int StaticVisibilityViewCount =>
        RenderWorldVisibilityProfile.StaticVisibilityViewCount;
    public int StaticModelVisibilityWordCount => 0;
    public int SurfaceVisibilityWordCount { get; }
    public int SurfaceMaterialRowCount { get; }
    public int SurfaceSunShadowWordCount =>
        SurfaceVisibilityWordCount;
    public int CellCasterMatrixWordCount => 1;
    public int CellCasterAggregateWordCount => 1;
    public int DynamicModelClientCount => 0;
    public int DynamicBrushClientCount => 0;
    public int DynamicModelWordCount => 0;
    public int DynamicBrushWordCount => 0;
    public int DynamicCellBitWordCount => 0;
    public int DynamicVisibilityByteCount => 0;
}

public enum RenderWorldVisibilityCoveragePolicy
{
    ExactWorldSurfaceCoverWithOutwardBoundsV1 = 0
}

/// <summary>
/// Evidence summary re-proved by the offline validator. A true result means
/// every admitted world surface occurs exactly once and every packed source
/// position is contained by both its surface bound and the aggregate
/// collision/render cell-tree bound.
/// </summary>
public sealed class RenderWorldVisibilityCoverageProof
{
    internal RenderWorldVisibilityCoverageProof(
        int coveredSurfaceCount,
        bool exactOrdinalCover,
        bool surfaceBoundsContainPackedVertices,
        bool aggregateBoundsContainCollisionAndSurfaces)
    {
        if (coveredSurfaceCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(coveredSurfaceCount));
        }

        CoveredSurfaceCount = coveredSurfaceCount;
        ExactOrdinalCover = exactOrdinalCover;
        SurfaceBoundsContainPackedVertices =
            surfaceBoundsContainPackedVertices;
        AggregateBoundsContainCollisionAndSurfaces =
            aggregateBoundsContainCollisionAndSurfaces;
    }

    public RenderWorldVisibilityCoveragePolicy Policy =>
        RenderWorldVisibilityCoveragePolicy
            .ExactWorldSurfaceCoverWithOutwardBoundsV1;
    public int CoveredSurfaceCount { get; }
    public bool ExactOrdinalCover { get; }
    public bool SurfaceBoundsContainPackedVertices { get; }
    public bool AggregateBoundsContainCollisionAndSurfaces { get; }
    public bool NoFalseNegativeWithinAdmittedGeometry =>
        ExactOrdinalCover &&
        SurfaceBoundsContainPackedVertices &&
        AggregateBoundsContainCollisionAndSurfaces;
}

public enum RenderWorldVisibilityDeferredMilestone
{
    M4TargetConsumerAcceptance = 4,
    M5Lighting = 5,
    M7AssetResolutionAndPersistence = 7
}

public enum RenderWorldVisibilityBlockerKind
{
    TargetConsumerAcceptanceNotEstablished = 0,
    LightingMembershipAndBakeOutputsNotCompiled = 1,
    CompleteGfxWorldAssemblyNotCompiled = 2,
    LinkingEmissionAndPersistenceNotAuthorized = 3
}

public sealed record RenderWorldVisibilityBlocker
{
    public RenderWorldVisibilityBlocker(
        RenderWorldVisibilityDeferredMilestone milestone,
        RenderWorldVisibilityBlockerKind kind,
        string detail)
    {
        if (!Enum.IsDefined(milestone))
            throw new ArgumentOutOfRangeException(nameof(milestone));
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);

        Milestone = milestone;
        Kind = kind;
        Detail = detail;
    }

    public RenderWorldVisibilityDeferredMilestone Milestone { get; }
    public RenderWorldVisibilityBlockerKind Kind { get; }
    public string Detail { get; }
}

/// <summary>
/// Complete detached M4 visibility candidate. It intentionally implements no
/// GfxWorld build-data or persistence interface.
/// </summary>
public sealed class RenderWorldVisibilityCandidate
{
    private readonly IReadOnlyList<RenderWorldVisibilityCellPartitionPlane>
        _cellPartitionPlanes;
    private readonly IReadOnlyList<ushort> _packedCellPartitionNodes;
    private readonly IReadOnlyList<ushort> _sortedWorldSurfaceOrdinals;
    private readonly IReadOnlyList<RenderWorldVisibilitySurfaceMembership>
        _surfaceMemberships;
    private readonly IReadOnlyList<RenderWorldVisibilityCell> _cells;
    private readonly IReadOnlyList<RenderWorldVisibilityBlocker> _blockers;

    internal RenderWorldVisibilityCandidate(
        CollisionStructuralCandidate collisionCandidate,
        RenderWorldStructuralCandidate renderCandidate,
        MapBounds collisionWorldBounds,
        IEnumerable<ushort> sortedWorldSurfaceOrdinals,
        IEnumerable<RenderWorldVisibilitySurfaceMembership>
            surfaceMemberships,
        RenderWorldVisibilityCell cell,
        RenderWorldVisibilityStaticDpvsShape staticDpvsShape,
        RenderWorldVisibilityRuntimeAllocationShape runtimeAllocationShape,
        RenderWorldVisibilityCoverageProof coverageProof,
        IEnumerable<RenderWorldVisibilityBlocker> blockers,
        RenderWorldVisibilityAssessment validationAssessment)
    {
        CollisionCandidate = collisionCandidate ??
            throw new ArgumentNullException(nameof(collisionCandidate));
        RenderCandidate = renderCandidate ??
            throw new ArgumentNullException(nameof(renderCandidate));
        RenderWorldVisibilityOutwardBounds.RequireValid(
            collisionWorldBounds,
            nameof(collisionWorldBounds));
        ArgumentNullException.ThrowIfNull(sortedWorldSurfaceOrdinals);
        ArgumentNullException.ThrowIfNull(surfaceMemberships);
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentNullException.ThrowIfNull(staticDpvsShape);
        ArgumentNullException.ThrowIfNull(runtimeAllocationShape);
        ArgumentNullException.ThrowIfNull(coverageProof);
        ArgumentNullException.ThrowIfNull(blockers);
        ArgumentNullException.ThrowIfNull(validationAssessment);

        CollisionWorldBounds = collisionWorldBounds;
        _cellPartitionPlanes =
            Array.AsReadOnly(
                Array.Empty<RenderWorldVisibilityCellPartitionPlane>());
        _packedCellPartitionNodes =
            Array.AsReadOnly(
                new[] { RenderWorldVisibilityProfile.SingleCellLeafNode });
        _sortedWorldSurfaceOrdinals =
            new ReadOnlyCollection<ushort>(
                sortedWorldSurfaceOrdinals.ToArray());
        _surfaceMemberships =
            new ReadOnlyCollection<RenderWorldVisibilitySurfaceMembership>(
                surfaceMemberships.ToArray());
        _cells =
            new ReadOnlyCollection<RenderWorldVisibilityCell>([cell]);
        StaticDpvsShape = staticDpvsShape;
        RuntimeAllocationShape = runtimeAllocationShape;
        CoverageProof = coverageProof;
        _blockers =
            new ReadOnlyCollection<RenderWorldVisibilityBlocker>(
                blockers.ToArray());
        ValidationAssessment = validationAssessment;
    }

    public MapDocumentId DocumentId => RenderCandidate.DocumentId;
    public long DocumentRevision => RenderCandidate.DocumentRevision;
    public string MapAssetName => RenderCandidate.MapAssetName;
    public string CompilerIdentity =>
        RenderWorldVisibilityProfile.CompilerIdentity;
    public RenderWorldVisibilityTopologyProfile TopologyProfile =>
        RenderWorldVisibilityTopologyProfile.SingleCellAllStaticV1;
    public RenderWorldVisibilityCandidateAuthority Authority =>
        RenderWorldVisibilityCandidateAuthority.OfflineValidationOnly;
    public bool PersistenceAuthorized => false;
    public CollisionStructuralCandidate CollisionCandidate { get; }
    public RenderWorldStructuralCandidate RenderCandidate { get; }
    public MapBounds CollisionWorldBounds { get; }
    public MapBounds WorldBounds => _cells[0].Bounds;
    public IReadOnlyList<RenderWorldVisibilityCellPartitionPlane>
        CellPartitionPlanes => _cellPartitionPlanes;
    public IReadOnlyList<ushort> PackedCellPartitionNodes =>
        _packedCellPartitionNodes;
    public IReadOnlyList<ushort> SortedWorldSurfaceOrdinals =>
        _sortedWorldSurfaceOrdinals;
    public IReadOnlyList<RenderWorldVisibilitySurfaceMembership>
        SurfaceMemberships => _surfaceMemberships;
    public IReadOnlyList<RenderWorldVisibilityCell> Cells => _cells;
    public RenderWorldVisibilityStaticDpvsShape StaticDpvsShape { get; }
    public RenderWorldVisibilityRuntimeAllocationShape
        RuntimeAllocationShape { get; }
    public RenderWorldVisibilityCoverageProof CoverageProof { get; }
    public IReadOnlyList<RenderWorldVisibilityBlocker> Blockers =>
        _blockers;
    public RenderWorldVisibilityAssessment ValidationAssessment { get; }
}

/// <summary>
/// Future cell-partition plane shape. The single-cell profile deliberately
/// materializes no rows.
/// </summary>
public readonly record struct RenderWorldVisibilityCellPartitionPlane(
    float NormalX,
    float NormalY,
    float NormalZ,
    float Distance);
