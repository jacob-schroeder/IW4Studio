using System.Collections.ObjectModel;
using IW4.FastFiles.Zone;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.Editing.SavePlanning;

namespace IW4.Studio.MapEditor.Compilation.StaticModels;

/// <summary>
/// Result taxonomy for one imported static-model row. Only
/// <see cref="ExactBundleUnique"/> is safe to use as compiled patch authority.
/// </summary>
public enum StaticModelCorrespondenceStatus
{
    ExactBundleUnique,
    Unmatched,
    Inconsistent,
    Ambiguous,
    Invalid
}

/// <summary>
/// Machine-readable reason that a bundle relationship was not proven.
/// </summary>
public enum StaticModelCorrespondenceIssueKind
{
    MissingGfxWorldAuthority,
    MissingCollisionAuthority,
    ConflictingCollisionAuthorities,
    AuthorityCardinalityMismatch,
    DocumentIdentityMismatch,
    SemanticProjectionMismatch,
    InvalidModelReference,
    InvalidTransform,
    NoExactModelOriginCandidate,
    BoundsMismatch,
    AxisScaleMismatch,
    AmbiguousForwardMatch,
    AmbiguousReverseMatch
}

/// <summary>
/// Row-major 3x3 matrix retained as value-only relationship evidence.
/// </summary>
public readonly record struct StaticModelMatrix3x3(
    MapVector3 Row0,
    MapVector3 Row1,
    MapVector3 Row2);

/// <summary>
/// Exact serialized-origin proof. IEEE-754 bits are retained so +0 and -0,
/// and any other bit-distinct values, cannot be treated as interchangeable.
/// </summary>
public readonly record struct StaticModelExactOriginEvidence(
    MapVector3 Value,
    int XBits,
    int YBits,
    int ZBits);

/// <summary>
/// Conservative midpoint/half-size comparison between the Gfx and Clip rows.
/// Clip field names predate their decoded midpoint/half-size semantics.
/// </summary>
public readonly record struct StaticModelBoundsCorrespondenceEvidence(
    MapBounds GfxBounds,
    MapBounds ClipBounds,
    float MaximumAbsoluteDelta,
    float AbsoluteTolerance);

/// <summary>
/// Placement proof formed by multiplying the decoded, scaled Gfx axis and
/// the Clip inverse-scaled axis in both orders. Both products must be within
/// <see cref="IdentityTolerance"/> of the identity matrix.
/// </summary>
public readonly record struct StaticModelAxisScaleCorrespondenceEvidence(
    float GfxScale,
    StaticModelMatrix3x3 GfxScaledAxis,
    StaticModelMatrix3x3 ClipInverseScaledAxis,
    float MaximumIdentityResidual,
    float IdentityTolerance);

/// <summary>
/// One mutual, one-to-one relationship proven only for the exact imported
/// bundle. This type deliberately does not express an ordinal or global IW4
/// format invariant.
/// </summary>
public sealed record StaticModelCompilationRelationship(
    MapObjectId RenderObjectId,
    MapObjectId CollisionObjectId,
    int GfxSourceOrdinal,
    int ClipSourceOrdinal,
    MapAssetKind CollisionAssetKind,
    XAssetType ModelAssetType,
    string ExactSerializedModelName,
    StaticModelExactOriginEvidence Origin,
    StaticModelBoundsCorrespondenceEvidence Bounds,
    StaticModelAxisScaleCorrespondenceEvidence AxisScale,
    string Evidence)
{
    public bool IsExactBundleUnique => true;
}

/// <summary>
/// Resolver-owned identity for the first-stage exact static-model match.
/// Origin components deliberately use IEEE-754 bits so admission and reopen
/// correspondence cannot disagree about bit-distinct coordinates.
/// </summary>
internal readonly record struct StaticModelExactMatchKey(
    XAssetType ModelAssetType,
    string ExactSerializedModelName,
    int OriginXBits,
    int OriginYBits,
    int OriginZBits)
{
    public static StaticModelExactMatchKey Create(
        XAssetType modelAssetType,
        string exactSerializedModelName,
        MapVector3 origin) =>
        new(
            modelAssetType,
            exactSerializedModelName,
            BitConverter.SingleToInt32Bits(origin.X),
            BitConverter.SingleToInt32Bits(origin.Y),
            BitConverter.SingleToInt32Bits(origin.Z));

    public static StaticModelExactMatchKey Create(
        StaticModelCompilationRelationship relationship)
    {
        ArgumentNullException.ThrowIfNull(relationship);
        return new(
            relationship.ModelAssetType,
            relationship.ExactSerializedModelName,
            relationship.Origin.XBits,
            relationship.Origin.YBits,
            relationship.Origin.ZBits);
    }
}

public sealed record StaticModelCorrespondenceIssue(
    StaticModelCorrespondenceIssueKind Kind,
    StaticModelRepresentation? Representation,
    int? SourceOrdinal,
    string Evidence);

/// <summary>
/// Resolution state for one semantic static-model row. Candidate ordinals
/// remain diagnostic only and never authorize a patch.
/// </summary>
public sealed class StaticModelCorrespondenceAssessment
{
    private readonly IReadOnlyList<int> _candidateOrdinals;

    internal StaticModelCorrespondenceAssessment(
        MapObjectId objectId,
        StaticModelRepresentation representation,
        int sourceOrdinal,
        StaticModelCorrespondenceStatus status,
        IEnumerable<int> candidateOrdinals,
        string evidence)
    {
        if (objectId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(objectId));
        ArgumentNullException.ThrowIfNull(candidateOrdinals);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidence);

        ObjectId = objectId;
        Representation = representation;
        SourceOrdinal = sourceOrdinal;
        Status = status;
        _candidateOrdinals = new ReadOnlyCollection<int>(
            candidateOrdinals
                .Distinct()
                .Order()
                .ToArray());
        Evidence = evidence;
    }

    public MapObjectId ObjectId { get; }
    public StaticModelRepresentation Representation { get; }
    public int SourceOrdinal { get; }
    public StaticModelCorrespondenceStatus Status { get; }
    public IReadOnlyList<int> CandidateOrdinals => _candidateOrdinals;
    public string Evidence { get; }
    public bool IsPatchEligible =>
        Status == StaticModelCorrespondenceStatus.ExactBundleUnique;
}

/// <summary>
/// Immutable exact-bundle correspondence catalog. Relationship lookup only
/// returns mutual one-to-one pairs that passed every evidence gate.
/// </summary>
public sealed class StaticModelCorrespondenceCatalog
{
    private readonly IReadOnlyList<StaticModelCompilationRelationship>
        _relationships;
    private readonly IReadOnlyList<StaticModelCorrespondenceAssessment>
        _assessments;
    private readonly IReadOnlyList<StaticModelCorrespondenceIssue> _issues;
    private readonly IReadOnlyDictionary<
        MapObjectId,
        StaticModelCompilationRelationship> _byRenderObjectId;
    private readonly IReadOnlyDictionary<
        MapObjectId,
        StaticModelCompilationRelationship> _byCollisionObjectId;
    private readonly IReadOnlyDictionary<
        MapObjectId,
        StaticModelCorrespondenceAssessment> _assessmentByObjectId;

    internal StaticModelCorrespondenceCatalog(
        MapDocumentId documentId,
        string mapIdentity,
        string bundleBaselineDigest,
        MapAssetKind? collisionAssetKind,
        bool authoritiesValid,
        IEnumerable<StaticModelCompilationRelationship> relationships,
        IEnumerable<StaticModelCorrespondenceAssessment> assessments,
        IEnumerable<StaticModelCorrespondenceIssue> issues)
    {
        if (documentId.Value == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(documentId));
        ArgumentException.ThrowIfNullOrWhiteSpace(mapIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleBaselineDigest);
        ArgumentNullException.ThrowIfNull(relationships);
        ArgumentNullException.ThrowIfNull(assessments);
        ArgumentNullException.ThrowIfNull(issues);

        StaticModelCompilationRelationship[] relationshipCopy =
            relationships.ToArray();
        StaticModelCorrespondenceAssessment[] assessmentCopy =
            assessments.ToArray();
        StaticModelCorrespondenceIssue[] issueCopy = issues.ToArray();

        Dictionary<MapObjectId, StaticModelCompilationRelationship>
            byRenderObjectId = relationshipCopy.ToDictionary(
                value => value.RenderObjectId);
        Dictionary<MapObjectId, StaticModelCompilationRelationship>
            byCollisionObjectId = relationshipCopy.ToDictionary(
                value => value.CollisionObjectId);
        Dictionary<MapObjectId, StaticModelCorrespondenceAssessment>
            assessmentByObjectId = assessmentCopy.ToDictionary(
                value => value.ObjectId);

        DocumentId = documentId;
        MapIdentity = mapIdentity;
        BundleBaselineDigest = bundleBaselineDigest;
        CollisionAssetKind = collisionAssetKind;
        AuthoritiesValid = authoritiesValid;
        _relationships =
            new ReadOnlyCollection<StaticModelCompilationRelationship>(
                relationshipCopy);
        _assessments =
            new ReadOnlyCollection<StaticModelCorrespondenceAssessment>(
                assessmentCopy);
        _issues =
            new ReadOnlyCollection<StaticModelCorrespondenceIssue>(issueCopy);
        _byRenderObjectId = new ReadOnlyDictionary<
            MapObjectId,
            StaticModelCompilationRelationship>(byRenderObjectId);
        _byCollisionObjectId = new ReadOnlyDictionary<
            MapObjectId,
            StaticModelCompilationRelationship>(byCollisionObjectId);
        _assessmentByObjectId = new ReadOnlyDictionary<
            MapObjectId,
            StaticModelCorrespondenceAssessment>(assessmentByObjectId);
    }

    public MapDocumentId DocumentId { get; }
    public string MapIdentity { get; }
    public string BundleBaselineDigest { get; }
    public MapAssetKind? CollisionAssetKind { get; }
    public bool AuthoritiesValid { get; }
    public IReadOnlyList<StaticModelCompilationRelationship> Relationships =>
        _relationships;
    public IReadOnlyList<StaticModelCorrespondenceAssessment> Assessments =>
        _assessments;
    public IReadOnlyList<StaticModelCorrespondenceIssue> Issues => _issues;
    public bool HasExactRelationships => _relationships.Count != 0;
    public bool IsComplete =>
        AuthoritiesValid &&
        _assessments.Count != 0 &&
        _assessments.All(value =>
            value.Status ==
            StaticModelCorrespondenceStatus.ExactBundleUnique);

    public bool TryGetByRenderObjectId(
        MapObjectId objectId,
        out StaticModelCompilationRelationship? relationship) =>
        _byRenderObjectId.TryGetValue(objectId, out relationship);

    public bool TryGetByCollisionObjectId(
        MapObjectId objectId,
        out StaticModelCompilationRelationship? relationship) =>
        _byCollisionObjectId.TryGetValue(objectId, out relationship);

    public bool TryGetAssessment(
        MapObjectId objectId,
        out StaticModelCorrespondenceAssessment? assessment) =>
        _assessmentByObjectId.TryGetValue(objectId, out assessment);
}
