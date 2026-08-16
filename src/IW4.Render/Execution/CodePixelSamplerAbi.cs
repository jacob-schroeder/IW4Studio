using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Render.Execution;

/// <summary>
/// One authored code-sampler ABI entry. Runtime ownership is present only
/// after the renderer has a concrete publication contract for that source.
/// </summary>
public sealed record CodePixelSamplerAbiEntry(
    MaterialTextureSource Source,
    string AuthoredName,
    string ResourceIdentity,
    string TextureTarget,
    ShaderRuntimeSamplerResourceKind RuntimeResourceKind =
        ShaderRuntimeSamplerResourceKind.Unknown,
    ShaderRuntimeSamplerRequirementStatus RuntimeRequirementStatus =
        ShaderRuntimeSamplerRequirementStatus.Unknown)
{
    public uint Argument => (uint)Source;

    public bool HasRuntimeRequirement =>
        RuntimeResourceKind !=
            ShaderRuntimeSamplerResourceKind.Unknown &&
        RuntimeRequirementStatus !=
            ShaderRuntimeSamplerRequirementStatus.Unknown;
}

/// <summary>
/// Backend-neutral catalog for the complete PS3 IW4 code-image ABI. The
/// authored names are the exact <c>MaterialTextureSource</c> enumerators, and
/// the 0..26 extent is the descriptor-table boundary recovered from
/// <c>default_mp.elf</c>. The Xbox enum's value 27
/// (<c>TEXTURE_SRC_CODE_COLOR_MANIPULATION</c>) is not a PS3 descriptor; that
/// address overlaps the PS3 sampler-state table.
/// </summary>
public static class CodePixelSamplerAbi
{
    private static readonly IReadOnlyList<CodePixelSamplerAbiEntry>
        Catalog = Array.AsReadOnly(
        [
            Entry(
                MaterialTextureSource.Black,
                "TEXTURE_SRC_CODE_BLACK"),
            Entry(
                MaterialTextureSource.White,
                "TEXTURE_SRC_CODE_WHITE"),
            Entry(
                MaterialTextureSource.IdentityNormalMap,
                "TEXTURE_SRC_CODE_IDENTITY_NORMAL_MAP"),
            Entry(
                MaterialTextureSource.ModelLighting,
                "TEXTURE_SRC_CODE_MODEL_LIGHTING",
                resourceIdentity: "modelLightingSampler",
                textureTarget: "Texture3D",
                runtimeResourceKind: ShaderRuntimeSamplerResourceKind
                    .ModelLightingAtlas,
                runtimeRequirementStatus:
                    ShaderRuntimeSamplerRequirementStatus
                    .ImmutableSceneAtlasRequired),
            Entry(
                MaterialTextureSource.LightmapPrimary,
                "TEXTURE_SRC_CODE_LIGHTMAP_PRIMARY"),
            Entry(
                MaterialTextureSource.LightmapSecondary,
                "TEXTURE_SRC_CODE_LIGHTMAP_SECONDARY"),
            Entry(
                MaterialTextureSource.ShadowMapSun,
                "TEXTURE_SRC_CODE_SHADOWMAP_SUN",
                resourceIdentity: "shadowmapSamplerSun",
                textureTarget: "Texture2DShadow",
                runtimeResourceKind:
                    ShaderRuntimeSamplerResourceKind.SunShadowAtlas,
                runtimeRequirementStatus:
                    ShaderRuntimeSamplerRequirementStatus
                    .SameRevisionAtlasRequired),
            Entry(
                MaterialTextureSource.ShadowMapSpot,
                "TEXTURE_SRC_CODE_SHADOWMAP_SPOT",
                resourceIdentity: "shadowmapSamplerSpot",
                textureTarget: "Texture2DShadow",
                runtimeResourceKind:
                    ShaderRuntimeSamplerResourceKind.SpotShadowAtlas,
                runtimeRequirementStatus:
                    ShaderRuntimeSamplerRequirementStatus
                        .SameRevisionAtlasRequired),
            Entry(
                MaterialTextureSource.Feedback,
                "TEXTURE_SRC_CODE_FEEDBACK"),
            Entry(
                MaterialTextureSource.ResolvedPostSun,
                "TEXTURE_SRC_CODE_RESOLVED_POST_SUN"),
            Entry(
                MaterialTextureSource.ResolvedScene,
                "TEXTURE_SRC_CODE_RESOLVED_SCENE"),
            Entry(
                MaterialTextureSource.PostEffect0,
                "TEXTURE_SRC_CODE_POST_EFFECT_0"),
            Entry(
                MaterialTextureSource.PostEffect1,
                "TEXTURE_SRC_CODE_POST_EFFECT_1"),
            Entry(
                MaterialTextureSource.LightAttenuation,
                "TEXTURE_SRC_CODE_LIGHT_ATTENUATION",
                resourceIdentity: "attenuationSampler",
                textureTarget: "Texture2D",
                runtimeResourceKind:
                    ShaderRuntimeSamplerResourceKind.LightAttenuation,
                runtimeRequirementStatus:
                    ShaderRuntimeSamplerRequirementStatus
                    .ImmutableSceneTextureRequired),
            Entry(
                MaterialTextureSource.Outdoor,
                "TEXTURE_SRC_CODE_OUTDOOR"),
            Entry(
                MaterialTextureSource.FloatZ,
                "TEXTURE_SRC_CODE_FLOATZ"),
            Entry(
                MaterialTextureSource.ProcessedFloatZ,
                "TEXTURE_SRC_CODE_PROCESSED_FLOATZ",
                resourceIdentity: "processedFloatZ",
                textureTarget: "Texture2D",
                runtimeResourceKind:
                    ShaderRuntimeSamplerResourceKind.ProcessedFloatZ,
                runtimeRequirementStatus:
                    ShaderRuntimeSamplerRequirementStatus
                    .SameRevisionTextureRequired),
            Entry(
                MaterialTextureSource.RawFloatZ,
                "TEXTURE_SRC_CODE_RAW_FLOATZ"),
            Entry(
                MaterialTextureSource.HalfParticles,
                "TEXTURE_SRC_CODE_HALF_PARTICLES"),
            Entry(
                MaterialTextureSource.HalfParticlesZ,
                "TEXTURE_SRC_CODE_HALF_PARTICLES_Z"),
            Entry(
                MaterialTextureSource.CaseTexture,
                "TEXTURE_SRC_CODE_CASE_TEXTURE"),
            Entry(
                MaterialTextureSource.CinematicY,
                "TEXTURE_SRC_CODE_CINEMATIC_Y"),
            Entry(
                MaterialTextureSource.CinematicCr,
                "TEXTURE_SRC_CODE_CINEMATIC_CR"),
            Entry(
                MaterialTextureSource.CinematicCb,
                "TEXTURE_SRC_CODE_CINEMATIC_CB"),
            Entry(
                MaterialTextureSource.CinematicA,
                "TEXTURE_SRC_CODE_CINEMATIC_A"),
            Entry(
                MaterialTextureSource.ReflectionProbe,
                "TEXTURE_SRC_CODE_REFLECTION_PROBE",
                textureTarget: "TextureCube"),
            Entry(
                MaterialTextureSource.AlternateScene,
                "TEXTURE_SRC_CODE_ALTERNATE_SCENE")
        ]);

    public static IReadOnlyList<CodePixelSamplerAbiEntry> Entries =>
        Catalog;

    public static bool TryResolve(
        uint argument,
        out CodePixelSamplerAbiEntry entry)
    {
        if (argument < (uint)Catalog.Count)
        {
            CodePixelSamplerAbiEntry candidate =
                Catalog[checked((int)argument)];
            if (candidate.Argument == argument)
            {
                entry = candidate;
                return true;
            }
        }

        entry = null!;
        return false;
    }

    public static bool TryResolve(
        MaterialTextureSource source,
        out CodePixelSamplerAbiEntry entry) =>
        TryResolve((uint)source, out entry);

    internal static bool HasRuntimeRequirement(uint argument) =>
        TryResolve(argument, out CodePixelSamplerAbiEntry entry) &&
        entry.HasRuntimeRequirement;

    internal static bool HasRuntimeRequirement(MaterialTextureSource source) =>
        TryResolve(source, out CodePixelSamplerAbiEntry entry) &&
        entry.HasRuntimeRequirement;

    private static CodePixelSamplerAbiEntry Entry(
        MaterialTextureSource source,
        string authoredName,
        string? resourceIdentity = null,
        string textureTarget = "Texture2D",
        ShaderRuntimeSamplerResourceKind runtimeResourceKind =
            ShaderRuntimeSamplerResourceKind.Unknown,
        ShaderRuntimeSamplerRequirementStatus
            runtimeRequirementStatus =
                ShaderRuntimeSamplerRequirementStatus.Unknown) =>
        new(
            source,
            authoredName,
            resourceIdentity ?? authoredName,
            textureTarget,
            runtimeResourceKind,
            runtimeRequirementStatus);
}
