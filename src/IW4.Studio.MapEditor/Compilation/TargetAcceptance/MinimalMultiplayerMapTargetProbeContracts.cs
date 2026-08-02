using System.Collections.ObjectModel;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Zone;
using IW4.Studio.MapEditor.Compilation.Glass;
using IW4.Studio.MapEditor.Compilation.Lighting;

namespace IW4.Studio.MapEditor.Compilation.TargetAcceptance;

/// <summary>
/// Authority carried by the bounded six-semantic-root M7 graph. It permits
/// checked managed emission only. It may enter the dedicated managed package
/// verifier, but not an arbitrary linker, Save As path, deployment path, or
/// retail target claim.
/// </summary>
public enum MinimalMultiplayerMapTargetProbeAuthority
{
    ManagedEmissionProbeOnly = 0
}

public enum MinimalMultiplayerMapTargetProbeBlockerKind
{
    TargetMaterialDependencyNotResolved = 0,
    ManagedLinkPackageAndFreshLoadNotAccepted = 1,
    RetailTargetAcceptanceNotEstablished = 2,
    ProductionGameplayGraphNotCompiled = 3,
    PersistenceNotAuthorized = 4
}

public sealed record MinimalMultiplayerMapTargetProbeBlocker(
    MinimalMultiplayerMapTargetProbeBlockerKind Kind,
    string Detail);

/// <summary>
/// Public, interface-free description of the minimal M7 map graph. Five
/// top-level roots own six semantic roots because ColMapMp materializes
/// MapEnts through its insert-owned nested definition.
/// </summary>
public sealed class MinimalMultiplayerMapTargetProbeCandidate
{
    private static readonly IReadOnlyList<XAssetType>
        OrderedTopLevelRootTypes =
            Array.AsReadOnly(
            [
                XAssetType.GfxMap,
                XAssetType.ColMapMp,
                XAssetType.ComMap,
                XAssetType.FxMap,
                XAssetType.GameMapMp
            ]);

    private static readonly IReadOnlyList<XAssetType>
        OrderedSemanticRootTypes =
            Array.AsReadOnly(
            [
                XAssetType.GfxMap,
                XAssetType.ColMapMp,
                XAssetType.ComMap,
                XAssetType.MapEnts,
                XAssetType.FxMap,
                XAssetType.GameMapMp
            ]);

    private readonly IReadOnlyList<
        MinimalMultiplayerMapTargetProbeBlocker> _blockers;
    private readonly IReadOnlyList<IXAssetBuildData> _topLevelBuildData;

    internal MinimalMultiplayerMapTargetProbeCandidate(
        GfxWorldNoBakeLightingCandidate lightingCandidate,
        MapCompilerContentIdentityInput contentIdentityInput,
        MapCompilerContentIdentity contentIdentity,
        MinimalMultiplayerMapProbeCollisionBuildData collisionBuildData,
        MinimalMultiplayerMapProbeMapEntsBuildData mapEntsBuildData,
        EmptyGlassDomainCompilation glassCompilation,
        IEnumerable<MinimalMultiplayerMapTargetProbeBlocker> blockers)
    {
        LightingCandidate = lightingCandidate ??
            throw new ArgumentNullException(nameof(lightingCandidate));
        ContentIdentityInput = contentIdentityInput ??
            throw new ArgumentNullException(nameof(contentIdentityInput));
        ContentIdentity = contentIdentity ??
            throw new ArgumentNullException(nameof(contentIdentity));
        CollisionBuildData = collisionBuildData ??
            throw new ArgumentNullException(nameof(collisionBuildData));
        MapEntsBuildData = mapEntsBuildData ??
            throw new ArgumentNullException(nameof(mapEntsBuildData));
        GlassCompilation = glassCompilation ??
            throw new ArgumentNullException(nameof(glassCompilation));
        ArgumentNullException.ThrowIfNull(blockers);

        _blockers =
            new ReadOnlyCollection<
                MinimalMultiplayerMapTargetProbeBlocker>(
                blockers.ToArray());
        _topLevelBuildData =
            new ReadOnlyCollection<IXAssetBuildData>(
            [
                GfxWorldBuildData,
                CollisionBuildData,
                ComWorldBuildData,
                FxWorldBuildData,
                GameWorldMpBuildData
            ]);
    }

    public const string CompilerIdentity =
        "iw4-studio.map.m7-minimal-multiplayer-target-probe@2";

    public const string EntityProfileIdentity =
        "iw4-studio.mapents.worldspawn-dm-intermission@2";

    public MinimalMultiplayerMapTargetProbeAuthority Authority =>
        MinimalMultiplayerMapTargetProbeAuthority.ManagedEmissionProbeOnly;

    public bool ManagedEmitterAccepted => true;

    public bool ManagedFreshLoadAccepted => false;

    public bool TargetConsumerAccepted => false;

    public bool PersistenceAuthorized => false;

    public EmptyGlassDomainAuthority GlassDomainAuthority =>
        GlassCompilation.Authority;

    public bool NonEmptyGlassEmissionAuthorized =>
        GlassCompilation.NonEmptyEmissionAuthorized;

    public GlassPieceIdentityAllocationPlan GlassIdentityPlan =>
        GlassCompilation.IdentityPlan;

    public string MapAssetName => ContentIdentityInput.MapAssetName;

    public uint PrimaryChecksum =>
        LightingCandidate
            .SpatialAssembly
            .ChecksumAssignment
            .Checksum
            .Value;

    public int TopLevelRootCount => OrderedTopLevelRootTypes.Count;

    public int SemanticRootCount => OrderedSemanticRootTypes.Count;

    public int MapEntityCount => 3;

    public GfxWorldNoBakeLightingCandidate LightingCandidate { get; }

    public MapCompilerContentIdentityInput ContentIdentityInput { get; }

    public MapCompilerContentIdentity ContentIdentity { get; }

    public IReadOnlyList<XAssetType> TopLevelRootTypes =>
        OrderedTopLevelRootTypes;

    public IReadOnlyList<XAssetType> SemanticRootTypes =>
        OrderedSemanticRootTypes;

    public IReadOnlyList<MinimalMultiplayerMapTargetProbeBlocker> Blockers =>
        _blockers;

    internal IGfxWorldBuildData GfxWorldBuildData =>
        LightingCandidate.GfxWorldBuildData;

    internal IClipMapBuildData CollisionBuildData { get; }

    internal IComWorldBuildData ComWorldBuildData =>
        LightingCandidate.ComWorldBuildData;

    internal IMapEntsBuildData MapEntsBuildData { get; }

    internal EmptyGlassDomainCompilation GlassCompilation { get; }

    internal IFxWorldBuildData FxWorldBuildData =>
        GlassCompilation.FxWorldBuildData;

    internal IGameWorldMpBuildData GameWorldMpBuildData =>
        GlassCompilation.GameWorldMpBuildData;

    internal IReadOnlyList<IXAssetBuildData> TopLevelBuildData =>
        _topLevelBuildData;
}
