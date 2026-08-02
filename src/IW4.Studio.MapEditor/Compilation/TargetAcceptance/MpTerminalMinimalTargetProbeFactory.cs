using IW4.Studio.MapEditor.Compilation.Collision;
using IW4.Studio.MapEditor.Compilation.Lighting;
using IW4.Studio.MapEditor.Compilation.RenderWorld;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.SourceDocuments;

namespace IW4.Studio.MapEditor.Compilation.TargetAcceptance;

public enum MpTerminalMinimalTargetProbeCompilationAuthority
{
    ManagedPreAcceptanceCompilationOnly = 0
}

/// <summary>
/// Deterministic output of the deliberately tiny real-name mp_terminal target
/// fixture. It closes offline compilation, external dependency identity, and
/// isolated managed round-trip gates only.
/// </summary>
public sealed class MpTerminalMinimalTargetProbeCompilation
{
    internal MpTerminalMinimalTargetProbeCompilation(
        MapCompilerContentIdentityInput contentIdentityInput,
        MapPrimaryChecksumAssignment checksumAssignment,
        MinimalMultiplayerMapTargetProbeCandidate candidate,
        MinimalMultiplayerMapTargetMaterialResolution materialResolution,
        MinimalMultiplayerMapManagedRoundTripEvidence managedRoundTrip,
        MinimalMultiplayerMapRuntimeSupportCompilation runtimeSupport)
    {
        ContentIdentityInput = contentIdentityInput ??
            throw new ArgumentNullException(nameof(contentIdentityInput));
        ChecksumAssignment = checksumAssignment ??
            throw new ArgumentNullException(nameof(checksumAssignment));
        Candidate = candidate ??
            throw new ArgumentNullException(nameof(candidate));
        MaterialResolution = materialResolution ??
            throw new ArgumentNullException(nameof(materialResolution));
        ManagedRoundTrip = managedRoundTrip ??
            throw new ArgumentNullException(nameof(managedRoundTrip));
        RuntimeSupport = runtimeSupport ??
            throw new ArgumentNullException(nameof(runtimeSupport));

        if (!ReferenceEquals(
                candidate,
                materialResolution.SourceCandidate) ||
            !ReferenceEquals(
                candidate,
                managedRoundTrip.SourceCandidate) ||
            !string.Equals(
                candidate.MapAssetName,
                MpTerminalMinimalTargetProbeFactory.MapAssetName,
                StringComparison.Ordinal) ||
            !string.Equals(
                runtimeSupport.TargetZoneName,
                MpTerminalMinimalTargetProbeFactory.TargetZoneName,
                StringComparison.Ordinal) ||
            checksumAssignment.Checksum.Value !=
                candidate.PrimaryChecksum)
        {
            throw new ArgumentException(
                "An mp_terminal target-probe compilation requires one " +
                "coherent candidate, material resolution, managed " +
                "round-trip, and checksum.");
        }
    }

    public MpTerminalMinimalTargetProbeCompilationAuthority Authority =>
        MpTerminalMinimalTargetProbeCompilationAuthority
            .ManagedPreAcceptanceCompilationOnly;

    public bool ManagedCompilationAccepted => true;

    public bool ExternalMaterialIdentityResolved => true;

    public bool ManagedIsolatedRoundTripAccepted => true;

    public bool DefaultMpDependencyPlanAccepted => false;

    public bool TargetConsumerAccepted => false;

    public bool PersistenceAuthorized => false;

    public string TargetZoneName =>
        MpTerminalMinimalTargetProbeFactory.TargetZoneName;

    public MapCompilerContentIdentityInput ContentIdentityInput { get; }

    public MapPrimaryChecksumAssignment ChecksumAssignment { get; }

    public MinimalMultiplayerMapTargetProbeCandidate Candidate { get; }

    public MinimalMultiplayerMapTargetMaterialResolution
        MaterialResolution { get; }

    public MinimalMultiplayerMapManagedRoundTripEvidence ManagedRoundTrip
    {
        get;
    }

    public MinimalMultiplayerMapRuntimeSupportCompilation RuntimeSupport
    {
        get;
    }

    /// <summary>
    /// Returns a target-test compilation enriched with the explicitly imported
    /// retail startup closure. The semantic map candidate and its content
    /// identity remain unchanged.
    /// </summary>
    public MpTerminalMinimalTargetProbeCompilation
        WithGameplayModelSupport(
            MapGameplayModelSupportCompilation gameplayModelSupport)
    {
        ArgumentNullException.ThrowIfNull(gameplayModelSupport);
        MinimalMultiplayerMapRuntimeSupportCompilation runtimeSupport =
            MinimalMultiplayerMapRuntimeSupportCompiler
                .AttachGameplayModelSupport(
                    RuntimeSupport,
                    gameplayModelSupport);
        return new MpTerminalMinimalTargetProbeCompilation(
            ContentIdentityInput,
            ChecksumAssignment,
            Candidate,
            MaterialResolution,
            ManagedRoundTrip,
            runtimeSupport);
    }
}

/// <summary>
/// Creates the first bounded PS3 target-test map from semantic source. The
/// fixed source IDs and dimensions are fixture identity, not general editor
/// defaults.
/// </summary>
public static class MpTerminalMinimalTargetProbeFactory
{
    public const string MapAssetName =
        "maps/mp/mp_terminal.d3dbsp";

    public const string TargetZoneName = "mp_terminal";

    public const float FloorHalfExtent = 256f;

    public const float CollisionSlabHalfDepth = 8f;

    // Retail MP maps use this exact clip_player material value for actual
    // player-only brushes. IW4's player trace mask (0x02810011) intersects
    // it at 0x00010000, while bulletTracePassed's 0x02806831 world mask does
    // not intersect it.
    public const int FloorCollisionSurfaceFlags = 0x000440A0;

    public const int FloorCollisionContents = 0x08010000;

    private static readonly MapDocumentId FixtureDocumentId =
        new(
            Guid.Parse(
                "6d705f74-6572-4d69-a16e-616c70726f62"));

    private static readonly MapObjectId FloorRenderObjectId =
        new(
            Guid.Parse(
                "6d705f74-6572-4d69-a16e-616c666c6f72"));

    private static readonly MapObjectId FloorCollisionObjectId =
        new(
            Guid.Parse(
                "6d705f74-6572-4d69-a16e-616c636f6c6c"));

    /// <summary>
    /// Creates the canonical semantic source for the bounded target fixture.
    /// Compiled topology and FastFile state are deliberately absent.
    /// </summary>
    public static Iw4SceneDocument CreateCanonicalSource()
    {
        GfxWorldTargetMaterialDependencyEvidence material =
            GfxWorldTargetMaterialDependencyCatalog
                .CommonMpChemLightGlow;
        return new Iw4SceneDocument(
            Iw4SceneFormat.CurrentVersion,
            FixtureDocumentId,
            documentRevision: 0,
            MapAssetName,
            TargetZoneName,
            MapCompilerProfiles.MinimalMultiplayerTargetProbe,
            MinimalMultiplayerMapTargetProbeCandidate
                .EntityProfileIdentity,
            MinimalMultiplayerMapTargetStartupProfile
                .OfflineSplitScreenFreeForAll,
            [CreateRenderFloor(material.AssetKey.LogicalName)],
            [CreateCollisionFloor()]);
    }

    public static MpTerminalMinimalTargetProbeCompilation Compile() =>
        MpTerminalMinimalTargetProbeSceneCompiler.Compile(
            CreateCanonicalSource());

    private static AuthoredIndexedRenderMeshSource CreateRenderFloor(
        string materialName) =>
        new(
            FloorRenderObjectId,
            new StandaloneWorldRenderMeshOwnership(),
            materialName,
            RenderTriangleWinding.CounterClockwiseFrontFace,
            [
                Vertex(
                    -FloorHalfExtent,
                    -FloorHalfExtent,
                    textureU: 0,
                    textureV: 0),
                Vertex(
                    FloorHalfExtent,
                    -FloorHalfExtent,
                    textureU: 1,
                    textureV: 0),
                Vertex(
                    FloorHalfExtent,
                    FloorHalfExtent,
                    textureU: 1,
                    textureV: 1),
                Vertex(
                    -FloorHalfExtent,
                    FloorHalfExtent,
                    textureU: 0,
                    textureV: 1)
            ],
            [
                new AuthoredIndexedRenderTriangle(0, 1, 2),
                new AuthoredIndexedRenderTriangle(0, 2, 3)
            ]);

    private static AuthoredRenderVertex Vertex(
        float x,
        float y,
        float textureU,
        float textureV) =>
        new(
            new MapVector3(x, y, 0),
            new AuthoredRenderColor(255, 255, 255, 255),
            new AuthoredRenderUv(textureU, textureV),
            new AuthoredRenderUv(0, 0),
            new MapVector3(0, 0, 1),
            new MapVector3(1, 0, 0));

    private static AuthoredConvexBrushCollisionSource
        CreateCollisionFloor() =>
            AuthoredCollisionPrimitiveFactory
                .CreateStandaloneAxisAlignedBox(
                    FloorCollisionObjectId,
                    new MapBounds(
                        new MapVector3(
                            0,
                            0,
                            -CollisionSlabHalfDepth),
                        new MapVector3(
                            FloorHalfExtent,
                            FloorHalfExtent,
                            CollisionSlabHalfDepth)),
                    new AuthoredCollisionMaterialInput(
                        "clip_player",
                        surfaceFlags: FloorCollisionSurfaceFlags,
                        contents: FloorCollisionContents));

}
