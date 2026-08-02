using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Emitters.Emission;
using IW4.FastFiles.Zone;
using IW4.Studio.MapEditor.Compilation.TargetAcceptance;

namespace IW4.Studio.MapEditor.Compilation.Lighting;

/// <summary>
/// Applies the bounded all-opaque/no-bake classification to an already
/// synchronized M4 spatial assembly and creates its matching ComMap sentinel.
/// This is a target-test candidate, not the full M5 lighting compiler.
/// </summary>
public static class GfxWorldNoBakeLightingAssembler
{
    public static GfxWorldNoBakeLightingCandidate Compile(
        MapSpatialTargetAcceptanceAssembly spatialAssembly)
    {
        ArgumentNullException.ThrowIfNull(spatialAssembly);
        if (!spatialAssembly.ManagedSerializerAccepted ||
            spatialAssembly.PersistenceAuthorized ||
            spatialAssembly.TargetConsumerAccepted)
        {
            throw new ArgumentException(
                "The no-bake lighting probe requires the valid, " +
                "non-persistable M4 managed-serialization assembly.",
                nameof(spatialAssembly));
        }

        int surfaceCount =
            spatialAssembly.SourceCandidate
                .RenderCandidate.Surfaces.Count;
        GfxWorldTargetAcceptanceProjectionPolicy policy =
            GfxWorldTargetAcceptanceProjectionPolicy
                .NoBakeAllOpaque(surfaceCount);
        GfxWorldAsset definition =
            GfxWorldTargetAcceptanceAssembler.ProjectDefinition(
                spatialAssembly.SourceCandidate,
                spatialAssembly.ChecksumAssignment,
                policy);
        GfxWorldReferenceBuildData references =
            GfxWorldTargetAcceptanceAssembler.ProjectReferences(
                spatialAssembly.SourceCandidate.RenderCandidate,
                policy);
        PrimaryLightOrdinalPlan primaryLightOrdinals =
            PrimaryLightOrdinalPlan.Create([]);
        var comBuildData =
            new GfxWorldNoBakeComWorldBuildData(
                spatialAssembly.SourceCandidate.MapAssetName,
                primaryLightOrdinals);
        var gfxBuildData =
            new GfxWorldTargetAcceptanceBuildData(
                definition,
                references);
        GfxComLightingGraphAssessment lightingGraphAssessment =
            GfxComLightingGraphValidator.AssessTargetRuntime(
                gfxBuildData,
                comBuildData,
                primaryLightOrdinals,
                GfxComLightingDependencyClosure.Empty);
        if (!lightingGraphAssessment.IsValid)
        {
            throw new InvalidDataException(
                "The no-bake Gfx/Com lighting graph is inconsistent: " +
                string.Join(
                    "; ",
                    lightingGraphAssessment.Issues.Select(issue =>
                        $"{issue.Path}: {issue.Detail}")));
        }

        ValidateSemanticProfile(
            spatialAssembly,
            definition,
            references,
            comBuildData,
            surfaceCount);
        ValidateEmitter(
            gfxBuildData,
            new GfxWorldBodyEmitter(),
            "GfxWorld");
        ValidateEmitter(
            comBuildData,
            new ComWorldBodyEmitter(),
            "ComWorld");

        return new GfxWorldNoBakeLightingCandidate(
            spatialAssembly,
            definition,
            references,
            CreateBlockers(),
            comBuildData,
            primaryLightOrdinals,
            lightingGraphAssessment);
    }

    private static void ValidateSemanticProfile(
        MapSpatialTargetAcceptanceAssembly spatialAssembly,
        GfxWorldAsset definition,
        GfxWorldReferenceBuildData references,
        GfxWorldNoBakeComWorldBuildData comBuildData,
        int surfaceCount)
    {
        uint endpoint = checked((uint)surfaceCount);
        bool exactReflectionProbe =
            definition.WorldDraw.ReflectionProbeCount == 1 &&
            definition.WorldDraw.ReflectionProbeImages.Count == 1 &&
            definition.WorldDraw.ReflectionProbeImages[0] is null &&
            definition.WorldDraw.ReflectionProbeOrigins.SequenceEqual(
                [new GfxReflectionProbe(0, 0, 0)]) &&
            definition.WorldDraw.ReflectionProbeTextures.Count == 0 &&
            definition.Cells.All(cell =>
                cell.ReflectionProbeCount == 1 &&
                cell.ReflectionProbes.SequenceEqual(
                    [GfxWorldNoBakeRuntimeDefaults
                        .ReflectionProbeIndex])) &&
            definition.Dpvs.SModelDrawInsts.All(value =>
                value.ReflectionProbeIndex ==
                    GfxWorldNoBakeRuntimeDefaults
                        .ReflectionProbeIndex);
        bool exactReflectionProbeOwnership =
            references.ReflectionProbeImages.Count == 1 &&
            references.ReflectionProbeImages[0] is
                SymbolicXAssetReference probeReference &&
            probeReference.AssetType == XAssetType.Image &&
            string.Equals(
                probeReference.OriginalSerializedName,
                GfxWorldNoBakeRuntimeDefaults
                    .ReflectionProbeSerializedReferenceName,
                StringComparison.Ordinal) &&
            references.ReflectionProbeImageDefinitions.Count == 1 &&
            GfxWorldNoBakeRuntimeDefaults
                .IsCompilerOwnedReflectionProbe(
                    references
                        .ReflectionProbeImageDefinitions[0]) &&
            references.ReflectionProbeImageLinks.Count == 0;
        bool exactNoLightmapState =
            definition.WorldDraw.LightmapCount == 0 &&
            definition.WorldDraw.Lightmaps.Count == 0 &&
            definition.WorldDraw.LightmapPrimaryTextures.Count == 0 &&
            definition.WorldDraw.LightmapSecondaryTextures.Count == 0 &&
            references.Lightmaps.Count == 0 &&
            references.LightmapDefinitions.Count == 0 &&
            references.LightmapLinks.Count == 0;
        bool exact =
            definition.Checksum ==
                spatialAssembly.Collision.Definition.Checksum &&
            definition.PrimaryLightCount == 1 &&
            definition.SunPrimaryLightIndex == 0 &&
            definition.ShadowGeom.Count == 1 &&
            definition.LightRegions.Count == 1 &&
            comBuildData.PrimaryLights.Count == 1 &&
            comBuildData.PrimaryLights[0].Type == 0 &&
            definition.Dpvs.LitSurfsBegin == 0 &&
            definition.Dpvs.LitSurfsEnd == endpoint &&
            definition.Dpvs.VisibilityCounts.Count == 8 &&
            definition.Dpvs.VisibilityCounts
                .Take(6)
                .All(value => value == endpoint) &&
            definition.Dpvs.VisibilityCounts[6] == 0 &&
            definition.Dpvs.VisibilityCounts[7] ==
                spatialAssembly.SourceCandidate
                    .RuntimeAllocationShape
                    .SurfaceVisibilityWordCount &&
            definition.FogTypesAllowed ==
                GfxWorldNoBakeLightingProfile
                    .NonSunDirectionFogModeMask &&
            exactReflectionProbe &&
            exactReflectionProbeOwnership &&
            exactNoLightmapState &&
            definition.SkyCount == 0 &&
            GfxWorldNoBakeRuntimeDefaults
                .IsCanonicalEmptyLightGrid(
                    definition.LightGrid) &&
            definition.Dpvs.Surfaces.All(surface =>
                surface.LightmapIndex ==
                    GfxWorldNoBakeRuntimeDefaults
                        .NoLightmapSurfaceIndex &&
                surface.ReflectionProbeIndex ==
                    GfxWorldNoBakeRuntimeDefaults
                        .ReflectionProbeIndex &&
                surface.PrimaryLightIndex == 0 &&
                surface.CastsSunShadow == 0) &&
            references.SurfaceMaterials.Count == surfaceCount;
        if (!exact)
        {
            throw new InvalidDataException(
                "The no-bake lighting projection diverged from its " +
                "all-opaque surface partition, compiler-owned default probe, " +
                "no-lightmap sentinel, empty-grid fallback, sentinel-light, " +
                "or synchronized-checksum contract.");
        }
    }

    private static void ValidateEmitter(
        IXAssetBuildData buildData,
        IXAssetBodyEmitter emitter,
        string label)
    {
        IReadOnlyList<EmissionError> errors =
            emitter.Validate(buildData);
        if (errors.Count != 0)
        {
            throw new InvalidDataException(
                $"{label} no-bake emission validation failed: " +
                string.Join(
                    "; ",
                    errors.Select(value =>
                        $"{value.Path}: {value.Message}")));
        }

        try
        {
            _ = emitter.Plan(buildData, new EmissionPlan());
        }
        catch (Exception exception)
        {
            throw new InvalidDataException(
                $"{label} no-bake emission planning failed.",
                exception);
        }
    }

    private static GfxWorldNoBakeLightingBlocker[] CreateBlockers() =>
    [
        new(
            GfxWorldNoBakeLightingDeferredMilestone
                .M5LightingAndTargetAcceptance,
            GfxWorldNoBakeLightingBlockerKind
                .TargetConsumerAcceptanceNotEstablished,
            "Managed emission does not establish retail initialization or " +
            "rendering acceptance."),
        new(
            GfxWorldNoBakeLightingDeferredMilestone
                .M5LightingAndTargetAcceptance,
            GfxWorldNoBakeLightingBlockerKind
                .SurfacePartitionTargetAcceptanceNotEstablished,
            "The all-opaque range endpoints are consumer-proven, but the " +
            "bounded candidate and its selected material category have not " +
            "run together on a target."),
        new(
            GfxWorldNoBakeLightingDeferredMilestone
                .M5LightingAndTargetAcceptance,
            GfxWorldNoBakeLightingBlockerKind
                .PrimaryLightSentinelTargetAcceptanceNotEstablished,
            "The synchronized zero-valued primary-light sentinel and its " +
            "empty shadow/light-region rows require target acceptance."),
        new(
            GfxWorldNoBakeLightingDeferredMilestone
                .M5LightingAndTargetAcceptance,
            GfxWorldNoBakeLightingBlockerKind
                .EmptyLightGridTargetAcceptanceNotEstablished,
            "The empty loader-shaped light grid, canonical fallback colors, " +
            "default probe, and no-lightmap surface sentinel require target " +
            "acceptance."),
        new(
            GfxWorldNoBakeLightingDeferredMilestone
                .M7DependencyGraphAndPersistence,
            GfxWorldNoBakeLightingBlockerKind
                .TargetMaterialResolutionNotEstablished,
            "An existing target-proven opaque material dependency has not " +
            "yet been selected and resolved."),
        new(
            GfxWorldNoBakeLightingDeferredMilestone
                .M5LightingAndTargetAcceptance,
            GfxWorldNoBakeLightingBlockerKind
                .SurfaceBoundsTailTargetAcceptanceNotEstablished,
            "The zero-valued opaque GfxSurfaceBounds tail remains a target " +
            "acceptance input."),
        new(
            GfxWorldNoBakeLightingDeferredMilestone
                .M5LightingAndTargetAcceptance,
            GfxWorldNoBakeLightingBlockerKind
                .BakedLightingNotCompiled,
            "Lightmaps, authored reflection probes, populated light-grid " +
            "samples, and static-model lighting remain full M5 compiler " +
            "outputs."),
        new(
            GfxWorldNoBakeLightingDeferredMilestone
                .M5LightingAndTargetAcceptance,
            GfxWorldNoBakeLightingBlockerKind
                .EnvironmentNotCompiled,
            "Sun, authored primary lights, shadows, sky, outdoor lighting, " +
            "and active fog/vision inputs remain full M5 outputs."),
        new(
            GfxWorldNoBakeLightingDeferredMilestone
                .M7DependencyGraphAndPersistence,
            GfxWorldNoBakeLightingBlockerKind
                .CompleteGraphAndPersistenceNotAuthorized,
            "The complete map asset/dependency graph is not assembled and " +
            "Save As remains unauthorized.")
    ];
}
