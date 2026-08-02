using System.Collections.ObjectModel;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Emitters.Linking;
using IW4.FastFiles.Zone;
using IW4.Studio.MapEditor.Compilation.Lighting;
using IW4.Studio.MapEditor.Compilation.RenderWorld;

namespace IW4.Studio.MapEditor.Compilation.TargetAcceptance;

/// <summary>
/// Static authority established from an immutable official dependency
/// observation. It does not represent execution of the generated map on a
/// retail target.
/// </summary>
public enum MinimalMultiplayerMapTargetMaterialResolutionAuthority
{
    OfficialDefaultMpDependencyObservation = 0
}

public enum MinimalMultiplayerMapTargetMaterialResolutionBlockerKind
{
    GeneratedCandidateDefaultMpLoadNotAccepted = 0,
    RetailTargetConsumerAcceptanceNotEstablished = 1,
    PersistenceNotAuthorized = 2
}

public sealed record
    MinimalMultiplayerMapTargetMaterialResolutionBlocker(
        MinimalMultiplayerMapTargetMaterialResolutionBlockerKind Kind,
        string Detail);

/// <summary>
/// Binds every bounded GfxWorld surface to one exact external Material
/// provider and binds that observation into the whole-map content identity.
/// </summary>
public sealed class MinimalMultiplayerMapTargetMaterialResolution
{
    private readonly IReadOnlyList<
        MinimalMultiplayerMapTargetMaterialResolutionBlocker> _blockers;

    internal MinimalMultiplayerMapTargetMaterialResolution(
        MinimalMultiplayerMapTargetProbeCandidate sourceCandidate,
        GfxWorldTargetMaterialDependencyEvidence dependency,
        IEnumerable<
            MinimalMultiplayerMapTargetMaterialResolutionBlocker> blockers)
    {
        SourceCandidate = sourceCandidate ??
            throw new ArgumentNullException(nameof(sourceCandidate));
        Dependency = dependency ??
            throw new ArgumentNullException(nameof(dependency));
        ArgumentNullException.ThrowIfNull(blockers);
        _blockers =
            new ReadOnlyCollection<
                MinimalMultiplayerMapTargetMaterialResolutionBlocker>(
                blockers.ToArray());
    }

    public MinimalMultiplayerMapTargetMaterialResolutionAuthority
        Authority =>
            MinimalMultiplayerMapTargetMaterialResolutionAuthority
                .OfficialDefaultMpDependencyObservation;

    public bool ExternalMaterialIdentityResolved => true;

    public bool OfficialProviderGraphObserved => true;

    public bool OpaqueSurfacePartitionCompatible => true;

    public bool GeneratedCandidateDefaultMpLoadAccepted => false;

    public bool TargetConsumerAccepted => false;

    public bool PersistenceAuthorized => false;

    public MinimalMultiplayerMapTargetProbeCandidate SourceCandidate
    {
        get;
    }

    public GfxWorldTargetMaterialDependencyEvidence Dependency { get; }

    public IReadOnlyList<
        MinimalMultiplayerMapTargetMaterialResolutionBlocker> Blockers =>
            _blockers;
}

/// <summary>
/// Closes only the M7 external-material identity gate. Default MP
/// dependency-plan loading and retail execution remain separate evidence.
/// </summary>
public static class MinimalMultiplayerMapTargetMaterialResolver
{
    public const string CompilerIdentity =
        "iw4-studio.map.m7-target-material-resolution@1";

    public static MinimalMultiplayerMapTargetMaterialResolution Resolve(
        MinimalMultiplayerMapTargetProbeCandidate candidate,
        GfxWorldTargetMaterialDependencyEvidence dependency)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(dependency);
        MinimalMultiplayerMapTargetProbeValidator.RequireValid(candidate);

        var issues = new List<string>();
        ValidateDependencyEvidence(dependency, issues);
        ValidateSurfaceBindings(candidate, dependency, issues);
        ValidateContentIdentity(candidate, dependency, issues);
        ValidateOpaquePartition(candidate, dependency, issues);
        if (issues.Count != 0)
        {
            throw new InvalidDataException(
                "The minimal multiplayer target material could not be " +
                "resolved: " +
                string.Join("; ", issues));
        }

        return new MinimalMultiplayerMapTargetMaterialResolution(
            candidate,
            dependency,
            CreateBlockers(dependency));
    }

    private static void ValidateDependencyEvidence(
        GfxWorldTargetMaterialDependencyEvidence dependency,
        ICollection<string> issues)
    {
        MaterialVertexStreamRouting[] expectedRoutes =
        [
            new(0, 0),
            new(1, 3),
            new(2, 8)
        ];
        if (dependency.AssetKey.Type != XAssetType.Material ||
            !dependency.SerializedExternalName.StartsWith(
                ",",
                StringComparison.Ordinal) ||
            dependency.ProviderZoneName != "common_mp" ||
            dependency.ProviderRootType != XAssetType.Weapon ||
            dependency.WorldVertexFormat !=
                MaterialWorldVertexFormat.MTL_WORLDVERT_TEX_1_NRM_1 ||
            dependency.PrimaryTechnique.Slot != 9 ||
            dependency.PrimaryTechnique.Flags != 0x0008 ||
            dependency.PrimaryTechnique.PassCount != 1 ||
            !dependency.PrimaryTechnique.VertexRoutes.SequenceEqual(
                expectedRoutes) ||
            dependency.DynamicFogTechnique.Slot != 10 ||
            dependency.DynamicFogTechnique.Flags != 0x0008 ||
            dependency.DynamicFogTechnique.PassCount != 1 ||
            !dependency.DynamicFogTechnique.VertexRoutes.SequenceEqual(
                expectedRoutes) ||
            !dependency.IsOpaque ||
            !dependency.ResolvedTransitiveAssetKeys.Contains(
                new ZoneAssetKey(
                    XAssetType.Techset,
                    dependency.TechniqueSetName)) ||
            !dependency.ResolvedTransitiveAssetKeys.Contains(
                new ZoneAssetKey(
                    XAssetType.Image,
                    dependency.MaterialSamplerImageName)))
        {
            issues.Add(
                "the dependency does not satisfy the observed opaque " +
                "TEX_1_NRM_1 common_mp material contract");
        }
    }

    private static void ValidateSurfaceBindings(
        MinimalMultiplayerMapTargetProbeCandidate candidate,
        GfxWorldTargetMaterialDependencyEvidence dependency,
        ICollection<string> issues)
    {
        int surfaceCount =
            candidate.LightingCandidate.GfxWorldDefinition.SurfaceCount;
        IReadOnlyList<SymbolicXAssetReference?>
            references =
                candidate
                    .LightingCandidate
                    .GfxWorldReferences
                    .SurfaceMaterials;
        if (surfaceCount <= 0 ||
            references.Count != surfaceCount ||
            references.Any(value =>
                value is null ||
                value.AssetType != XAssetType.Material ||
                !string.Equals(
                    value.OriginalSerializedName,
                    dependency.SerializedExternalName,
                    StringComparison.Ordinal)))
        {
            issues.Add(
                "not every compiled world surface carries the exact " +
                "comma-prefixed target Material reference");
        }

        string[] authoredMaterials =
            candidate
                .LightingCandidate
                .SpatialAssembly
                .SourceCandidate
                .RenderCandidate
                .Sources
                .Select(value => value.SymbolicMaterialName)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        if (authoredMaterials.Length != 1 ||
            !string.Equals(
                authoredMaterials[0],
                dependency.AssetKey.LogicalName,
                StringComparison.Ordinal))
        {
            issues.Add(
                "authored render sources do not use exactly the resolved " +
                "target Material identity");
        }
    }

    private static void ValidateContentIdentity(
        MinimalMultiplayerMapTargetProbeCandidate candidate,
        GfxWorldTargetMaterialDependencyEvidence dependency,
        ICollection<string> issues)
    {
        if (candidate.ContentIdentityInput.Profile !=
                MapCompilerProfiles.MinimalMultiplayerTargetProbe ||
            !candidate.ContentIdentityInput.DependencyDigest.Equals(
                dependency.DependencyDigest))
        {
            issues.Add(
                "whole-map content identity does not bind the dedicated " +
                "target-probe profile and exact material dependency digest");
        }
    }

    private static void ValidateOpaquePartition(
        MinimalMultiplayerMapTargetProbeCandidate candidate,
        GfxWorldTargetMaterialDependencyEvidence dependency,
        ICollection<string> issues)
    {
        var gfx =
            candidate.LightingCandidate.GfxWorldDefinition;
        uint endpoint = checked((uint)gfx.SurfaceCount);
        if (!dependency.IsOpaque ||
            gfx.Dpvs.LitSurfsBegin != 0 ||
            gfx.Dpvs.LitSurfsEnd != endpoint ||
            gfx.Dpvs.VisibilityCounts.Count != 8 ||
            gfx.Dpvs.VisibilityCounts.Take(6)
                .Any(value => value != endpoint))
        {
            issues.Add(
                "the compiled DPVS surface ranges are not the exact " +
                "all-opaque partition required by the selected material");
        }
    }

    private static
        MinimalMultiplayerMapTargetMaterialResolutionBlocker[]
        CreateBlockers(
            GfxWorldTargetMaterialDependencyEvidence dependency) =>
    [
        new(
            MinimalMultiplayerMapTargetMaterialResolutionBlockerKind
                .GeneratedCandidateDefaultMpLoadNotAccepted,
            $"The generated target candidate has not yet reopened through " +
            $"the Default MP plan with '{dependency.ProviderZoneName}' as " +
            "the active full-definition Material provider."),
        new(
            MinimalMultiplayerMapTargetMaterialResolutionBlockerKind
                .RetailTargetConsumerAcceptanceNotEstablished,
            "The generated world still requires retail initialization, " +
            "render, visibility, collision, spawn, and delayed-stability " +
            "acceptance."),
        new(
            MinimalMultiplayerMapTargetMaterialResolutionBlockerKind
                .PersistenceNotAuthorized,
            "Dependency resolution alone does not authorize Map Editor " +
            "Save As or production deployment.")
    ];
}
