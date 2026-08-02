using IW4.Assets.Assets.ColMap;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Zone;
using IW4.Studio.MapEditor.Compilation.Collision;
using IW4.Studio.MapEditor.Compilation.RenderWorld.Visibility;

namespace IW4.Studio.MapEditor.Compilation.TargetAcceptance;

/// <summary>
/// Authority carried by the serialized ColMap projection used during M4.
/// This authority permits managed serializer/loader verification only; it
/// does not authorize a FastFile save or claim retail target acceptance.
/// </summary>
public enum CollisionTargetAcceptanceCandidateAuthority
{
    ManagedSerializationValidationOnly = 0
}

/// <summary>
/// Detached ColMap projection for the bounded M4 target-acceptance profile.
/// The public candidate deliberately does not implement
/// <see cref="IClipMapBuildData"/>. An internal adapter is available to the
/// aggregate managed-load verifier without exposing this candidate as general
/// persistence input.
/// </summary>
public sealed class CollisionTargetAcceptanceCandidate
{
    private readonly CollisionTargetAcceptanceBuildData _buildData;

    internal CollisionTargetAcceptanceCandidate(
        RenderWorldVisibilityCandidate sourceCandidate,
        MapPrimaryChecksumAssignment checksumAssignment,
        ClipMapAsset definition,
        ClipMapReferenceBuildData references,
        CollisionStructuralReachabilityAssessment
            structuralAssessment)
    {
        SourceCandidate = sourceCandidate ??
            throw new ArgumentNullException(nameof(sourceCandidate));
        ChecksumAssignment = checksumAssignment ??
            throw new ArgumentNullException(nameof(checksumAssignment));
        Definition = definition ??
            throw new ArgumentNullException(nameof(definition));
        References = references ??
            throw new ArgumentNullException(nameof(references));
        StructuralAssessment = structuralAssessment ??
            throw new ArgumentNullException(
                nameof(structuralAssessment));
        if (!structuralAssessment.IsValid)
        {
            throw new ArgumentException(
                "A collision target-acceptance candidate requires a valid " +
                "structural assessment.",
                nameof(structuralAssessment));
        }

        _buildData = new CollisionTargetAcceptanceBuildData(
            definition,
            references);
    }

    public const string SerializationProfileIdentity =
        "iw4-studio.colmap.m4-managed-serialization@1";

    public CollisionTargetAcceptanceCandidateAuthority Authority =>
        CollisionTargetAcceptanceCandidateAuthority
            .ManagedSerializationValidationOnly;

    public bool PersistenceAuthorized => false;

    public RenderWorldVisibilityCandidate SourceCandidate { get; }

    public MapPrimaryChecksumAssignment ChecksumAssignment { get; }

    public ClipMapAsset Definition { get; }

    public ClipMapReferenceBuildData References { get; }

    public CollisionStructuralReachabilityAssessment StructuralAssessment
    {
        get;
    }

    internal IClipMapBuildData BuildDataAdapter => _buildData;
}

/// <summary>
/// Narrow bridge to the existing checked ColMap emitter. Keeping this type
/// internal prevents the public M4 candidate from becoming an accidental
/// general-purpose save payload.
/// </summary>
internal sealed class CollisionTargetAcceptanceBuildData :
    IClipMapBuildData
{
    internal CollisionTargetAcceptanceBuildData(
        ClipMapAsset definition,
        ClipMapReferenceBuildData references)
    {
        Definition = definition ??
            throw new ArgumentNullException(nameof(definition));
        References = references ??
            throw new ArgumentNullException(nameof(references));
        if (definition.SerializedType != XAssetType.ColMapMp)
        {
            throw new ArgumentException(
                "The initial M4 target-acceptance profile supports only " +
                "multiplayer ColMap rows.",
                nameof(definition));
        }
    }

    public XAssetType AssetType => XAssetType.ColMapMp;

    public XAssetType SerializedType => XAssetType.ColMapMp;

    public ClipMapAsset Definition { get; }

    public ClipMapReferenceBuildData References { get; }

    public ClipMapLinkerProvenance LinkerProvenance =>
        ClipMapLinkerProvenance.Empty;
}
