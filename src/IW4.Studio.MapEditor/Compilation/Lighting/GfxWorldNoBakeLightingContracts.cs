using System.Collections.ObjectModel;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Zone;
using IW4.Studio.MapEditor.Compilation.TargetAcceptance;

namespace IW4.Studio.MapEditor.Compilation.Lighting;

/// <summary>
/// Bounded M5 profile used to make the tiny M4 scene classifiable by the
/// native world-surface ranges without claiming a lightmap bake.
/// </summary>
public static class GfxWorldNoBakeLightingProfile
{
    public const string CompilerIdentity =
        "iw4-studio.gfxworld.lighting.no-bake-opaque-material@2";

    public const string SurfacePartitionPolicyId =
        "iw4-studio.gfxworld.surface-partition.all-opaque@1";

    public const byte NonSunDirectionFogModeMask = 1;
}

public enum GfxWorldNoBakeLightingCandidateAuthority
{
    ManagedSerializationProbeOnly = 0
}

public enum GfxWorldNoBakeLightingDeferredMilestone
{
    M5LightingAndTargetAcceptance = 5,
    M7DependencyGraphAndPersistence = 7
}

public enum GfxWorldNoBakeLightingBlockerKind
{
    TargetConsumerAcceptanceNotEstablished = 0,
    SurfacePartitionTargetAcceptanceNotEstablished = 1,
    PrimaryLightSentinelTargetAcceptanceNotEstablished = 2,
    EmptyLightGridTargetAcceptanceNotEstablished = 3,
    TargetMaterialResolutionNotEstablished = 4,
    SurfaceBoundsTailTargetAcceptanceNotEstablished = 5,
    BakedLightingNotCompiled = 6,
    EnvironmentNotCompiled = 7,
    CompleteGraphAndPersistenceNotAuthorized = 8
}

public sealed record GfxWorldNoBakeLightingBlocker(
    GfxWorldNoBakeLightingDeferredMilestone Milestone,
    GfxWorldNoBakeLightingBlockerKind Kind,
    string Detail);

/// <summary>
/// Synchronized GfxWorld/ComWorld no-bake probe. It intentionally
/// implements no build-data interface and cannot enter Save As.
/// </summary>
public sealed class GfxWorldNoBakeLightingCandidate
{
    private readonly IReadOnlyList<GfxWorldNoBakeLightingBlocker>
        _blockers;
    private readonly GfxWorldTargetAcceptanceBuildData _gfxBuildData;
    private readonly GfxWorldNoBakeComWorldBuildData _comBuildData;

    internal GfxWorldNoBakeLightingCandidate(
        MapSpatialTargetAcceptanceAssembly spatialAssembly,
        GfxWorldAsset gfxWorldDefinition,
        GfxWorldReferenceBuildData gfxWorldReferences,
        IEnumerable<GfxWorldNoBakeLightingBlocker> blockers,
        GfxWorldNoBakeComWorldBuildData comBuildData,
        PrimaryLightOrdinalPlan primaryLightOrdinals,
        GfxComLightingGraphAssessment lightingGraphAssessment)
    {
        SpatialAssembly = spatialAssembly ??
            throw new ArgumentNullException(nameof(spatialAssembly));
        GfxWorldDefinition = gfxWorldDefinition ??
            throw new ArgumentNullException(nameof(gfxWorldDefinition));
        GfxWorldReferences = gfxWorldReferences ??
            throw new ArgumentNullException(nameof(gfxWorldReferences));
        ArgumentNullException.ThrowIfNull(blockers);
        _blockers =
            new ReadOnlyCollection<GfxWorldNoBakeLightingBlocker>(
                blockers.ToArray());
        _comBuildData = comBuildData ??
            throw new ArgumentNullException(nameof(comBuildData));
        PrimaryLightOrdinals = primaryLightOrdinals ??
            throw new ArgumentNullException(nameof(primaryLightOrdinals));
        LightingGraphAssessment = lightingGraphAssessment ??
            throw new ArgumentNullException(
                nameof(lightingGraphAssessment));
        if (!LightingGraphAssessment.IsValid)
        {
            throw new ArgumentException(
                "A no-bake candidate requires a valid local Gfx/Com " +
                "lighting graph.",
                nameof(lightingGraphAssessment));
        }
        _gfxBuildData = new GfxWorldTargetAcceptanceBuildData(
            gfxWorldDefinition,
            gfxWorldReferences);
    }

    public GfxWorldNoBakeLightingCandidateAuthority Authority =>
        GfxWorldNoBakeLightingCandidateAuthority
            .ManagedSerializationProbeOnly;

    public string CompilerIdentity =>
        GfxWorldNoBakeLightingProfile.CompilerIdentity;

    public string SurfacePartitionPolicyId =>
        GfxWorldNoBakeLightingProfile.SurfacePartitionPolicyId;

    public bool ManagedSerializerAccepted => true;

    public bool TargetConsumerAccepted => false;

    public bool PersistenceAuthorized => false;

    public MapSpatialTargetAcceptanceAssembly SpatialAssembly { get; }

    public GfxWorldAsset GfxWorldDefinition { get; }

    public GfxWorldReferenceBuildData GfxWorldReferences { get; }

    public int ComPrimaryLightCount =>
        _comBuildData.PrimaryLights.Count;

    public PrimaryLightOrdinalPlan PrimaryLightOrdinals { get; }

    public GfxComLightingGraphAssessment LightingGraphAssessment { get; }

    public IReadOnlyList<GfxWorldNoBakeLightingBlocker> Blockers =>
        _blockers;

    internal IGfxWorldBuildData GfxWorldBuildData => _gfxBuildData;

    internal IComWorldBuildData ComWorldBuildData => _comBuildData;
}

/// <summary>
/// Internal sentinel-bearing ComMap bridge. Row zero is the non-light
/// sentinel required by the bounded profile; it is not an authored light.
/// </summary>
internal sealed class GfxWorldNoBakeComWorldBuildData :
    IComWorldBuildData
{
    private readonly IReadOnlyList<ComPrimaryLightBuildData>
        _primaryLights;

    internal GfxWorldNoBakeComWorldBuildData(
        string name,
        PrimaryLightOrdinalPlan ordinalPlan)
    {
        ArgumentNullException.ThrowIfNull(ordinalPlan);
        if (ordinalPlan.AuthoredSourceCount != 0)
        {
            throw new ArgumentException(
                "The no-bake profile admits only the compiler-owned " +
                "primary-light sentinel.",
                nameof(ordinalPlan));
        }

        Name = MapCompilerContentIdentityInput
            .NormalizeMultiplayerMapAssetName(name);
        _primaryLights = ordinalPlan.ComPrimaryLights;
    }

    public XAssetType AssetType => XAssetType.ComMap;

    public string Name { get; }

    public int IsInUse => 0;

    public IReadOnlyList<ComPrimaryLightBuildData> PrimaryLights =>
        _primaryLights;
}
