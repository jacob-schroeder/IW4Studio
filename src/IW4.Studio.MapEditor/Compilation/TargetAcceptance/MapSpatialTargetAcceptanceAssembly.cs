using IW4.FastFiles.Emitters.Assets;
using IW4.Studio.MapEditor.Compilation.RenderWorld.Visibility;

namespace IW4.Studio.MapEditor.Compilation.TargetAcceptance;

/// <summary>
/// Authority carried by the synchronized GfxWorld/ColMap M4 assembly.
/// The graph may enter checked managed emitters, but it cannot enter Save As
/// and does not represent retail or emulator acceptance.
/// </summary>
public enum MapSpatialTargetAcceptanceAssemblyAuthority
{
    ManagedSerializationProbeOnly = 0
}

/// <summary>
/// Synchronized, address-free map-spatial roots for the bounded M4 profile.
/// Build-data adapters remain internal so this proof seam cannot silently
/// become persistence authority.
/// </summary>
public sealed class MapSpatialTargetAcceptanceAssembly
{
    internal MapSpatialTargetAcceptanceAssembly(
        RenderWorldVisibilityCandidate sourceCandidate,
        MapPrimaryChecksumAssignment checksumAssignment,
        GfxWorldTargetAcceptanceCandidate gfxWorld,
        CollisionTargetAcceptanceCandidate collision)
    {
        SourceCandidate = sourceCandidate ??
            throw new ArgumentNullException(nameof(sourceCandidate));
        ChecksumAssignment = checksumAssignment ??
            throw new ArgumentNullException(nameof(checksumAssignment));
        GfxWorld = gfxWorld ??
            throw new ArgumentNullException(nameof(gfxWorld));
        Collision = collision ??
            throw new ArgumentNullException(nameof(collision));

        if (!ReferenceEquals(
                sourceCandidate,
                gfxWorld.VisibilityCandidate) ||
            !ReferenceEquals(
                sourceCandidate,
                collision.SourceCandidate))
        {
            throw new ArgumentException(
                "Both serialized roots must project the same immutable M4 " +
                "visibility candidate.");
        }
        if (!ReferenceEquals(
                checksumAssignment,
                gfxWorld.PrimaryChecksumAssignment) ||
            !ReferenceEquals(
                checksumAssignment,
                collision.ChecksumAssignment) ||
            gfxWorld.Definition.Checksum !=
                collision.Definition.Checksum ||
            gfxWorld.Definition.Checksum !=
                checksumAssignment.Checksum.Value)
        {
            throw new ArgumentException(
                "Both serialized roots must project one shared primary " +
                "checksum assignment.");
        }
        if (!string.Equals(
                gfxWorld.Definition.Name,
                collision.Definition.Name,
                StringComparison.Ordinal) ||
            !string.Equals(
                gfxWorld.Definition.Name,
                sourceCandidate.MapAssetName,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Both serialized roots must retain the source map identity.");
        }
    }

    public const string AssemblyProfileIdentity =
        "iw4-studio.map-spatial.m4-managed-serialization@1";

    public MapSpatialTargetAcceptanceAssemblyAuthority Authority =>
        MapSpatialTargetAcceptanceAssemblyAuthority
            .ManagedSerializationProbeOnly;

    public bool ManagedSerializerAccepted =>
        GfxWorld.ManagedSerializerAccepted &&
        Collision.StructuralAssessment.IsValid;

    public bool ManagedFreshLoadAccepted => false;

    public bool TargetConsumerAccepted => false;

    public bool PersistenceAuthorized => false;

    public RenderWorldVisibilityCandidate SourceCandidate { get; }

    public MapPrimaryChecksumAssignment ChecksumAssignment { get; }

    public GfxWorldTargetAcceptanceCandidate GfxWorld { get; }

    public CollisionTargetAcceptanceCandidate Collision { get; }

    internal IGfxWorldBuildData GfxWorldBuildData =>
        new GfxWorldTargetAcceptanceBuildData(
            GfxWorld.Definition,
            GfxWorld.References);

    internal IClipMapBuildData CollisionBuildData =>
        Collision.BuildDataAdapter;
}

/// <summary>
/// Builds the two primary spatial roots atomically from one validated source
/// and checksum assignment. This method performs no linking, packaging, asset
/// registration, or persistence.
/// </summary>
public static class MapSpatialTargetAcceptanceAssembler
{
    public static MapSpatialTargetAcceptanceAssembly Assemble(
        RenderWorldVisibilityCandidate sourceCandidate,
        MapPrimaryChecksumAssignment checksumAssignment)
    {
        ArgumentNullException.ThrowIfNull(sourceCandidate);
        ArgumentNullException.ThrowIfNull(checksumAssignment);

        GfxWorldTargetAcceptanceCandidate gfxWorld =
            GfxWorldTargetAcceptanceAssembler.Compile(
                sourceCandidate,
                checksumAssignment);
        CollisionTargetAcceptanceCandidate collision =
            CollisionTargetAcceptanceAssembler.Assemble(
                sourceCandidate,
                checksumAssignment);

        return new MapSpatialTargetAcceptanceAssembly(
            sourceCandidate,
            checksumAssignment,
            gfxWorld,
            collision);
    }
}
