namespace IW4.Render.Execution;

/// <summary>
/// Exact IW4 <c>MaterialTextureSource</c> values used by
/// <c>CodePixelSampler</c> arguments.
/// </summary>
public enum CodePixelSamplerSource : uint
{
    Black = 0,
    White = 1,
    IdentityNormalMap = 2,
    ModelLighting = 3,
    LightmapPrimary = 4,
    LightmapSecondary = 5,
    ShadowmapSun = 6,
    ShadowmapSpot = 7,
    Feedback = 8,
    ResolvedPostSun = 9,
    ResolvedScene = 10,
    PostEffect0 = 11,
    PostEffect1 = 12,
    LightAttenuation = 13,
    Outdoor = 14,
    FloatZ = 15,
    ProcessedFloatZ = 16,
    RawFloatZ = 17,
    HalfParticles = 18,
    HalfParticlesZ = 19,
    CaseTexture = 20,
    CinematicY = 21,
    CinematicCr = 22,
    CinematicCb = 23,
    CinematicA = 24,
    ReflectionProbe = 25,
    AlternateScene = 26
}

/// <summary>
/// One authored code-sampler ABI entry. Runtime ownership is present only
/// after the renderer has a concrete publication contract for that source.
/// </summary>
public sealed record CodePixelSamplerAbiEntry(
    CodePixelSamplerSource Source,
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
                CodePixelSamplerSource.Black,
                "TEXTURE_SRC_CODE_BLACK"),
            Entry(
                CodePixelSamplerSource.White,
                "TEXTURE_SRC_CODE_WHITE"),
            Entry(
                CodePixelSamplerSource.IdentityNormalMap,
                "TEXTURE_SRC_CODE_IDENTITY_NORMAL_MAP"),
            Entry(
                CodePixelSamplerSource.ModelLighting,
                "TEXTURE_SRC_CODE_MODEL_LIGHTING",
                resourceIdentity: "modelLightingSampler",
                textureTarget: "Texture3D",
                runtimeResourceKind: ShaderRuntimeSamplerResourceKind
                    .ModelLightingAtlas,
                runtimeRequirementStatus:
                    ShaderRuntimeSamplerRequirementStatus
                    .ImmutableSceneAtlasRequired),
            Entry(
                CodePixelSamplerSource.LightmapPrimary,
                "TEXTURE_SRC_CODE_LIGHTMAP_PRIMARY"),
            Entry(
                CodePixelSamplerSource.LightmapSecondary,
                "TEXTURE_SRC_CODE_LIGHTMAP_SECONDARY"),
            Entry(
                CodePixelSamplerSource.ShadowmapSun,
                "TEXTURE_SRC_CODE_SHADOWMAP_SUN",
                resourceIdentity: "shadowmapSamplerSun",
                textureTarget: "Texture2DShadow",
                runtimeResourceKind:
                    ShaderRuntimeSamplerResourceKind.SunShadowAtlas,
                runtimeRequirementStatus:
                    ShaderRuntimeSamplerRequirementStatus
                    .SameRevisionAtlasRequired),
            Entry(
                CodePixelSamplerSource.ShadowmapSpot,
                "TEXTURE_SRC_CODE_SHADOWMAP_SPOT",
                textureTarget: "Texture2DShadow"),
            Entry(
                CodePixelSamplerSource.Feedback,
                "TEXTURE_SRC_CODE_FEEDBACK"),
            Entry(
                CodePixelSamplerSource.ResolvedPostSun,
                "TEXTURE_SRC_CODE_RESOLVED_POST_SUN"),
            Entry(
                CodePixelSamplerSource.ResolvedScene,
                "TEXTURE_SRC_CODE_RESOLVED_SCENE"),
            Entry(
                CodePixelSamplerSource.PostEffect0,
                "TEXTURE_SRC_CODE_POST_EFFECT_0"),
            Entry(
                CodePixelSamplerSource.PostEffect1,
                "TEXTURE_SRC_CODE_POST_EFFECT_1"),
            Entry(
                CodePixelSamplerSource.LightAttenuation,
                "TEXTURE_SRC_CODE_LIGHT_ATTENUATION"),
            Entry(
                CodePixelSamplerSource.Outdoor,
                "TEXTURE_SRC_CODE_OUTDOOR"),
            Entry(
                CodePixelSamplerSource.FloatZ,
                "TEXTURE_SRC_CODE_FLOATZ"),
            Entry(
                CodePixelSamplerSource.ProcessedFloatZ,
                "TEXTURE_SRC_CODE_PROCESSED_FLOATZ",
                resourceIdentity: "processedFloatZ",
                textureTarget: "Texture2D",
                runtimeResourceKind:
                    ShaderRuntimeSamplerResourceKind.ProcessedFloatZ,
                runtimeRequirementStatus:
                    ShaderRuntimeSamplerRequirementStatus
                    .SameRevisionTextureRequired),
            Entry(
                CodePixelSamplerSource.RawFloatZ,
                "TEXTURE_SRC_CODE_RAW_FLOATZ"),
            Entry(
                CodePixelSamplerSource.HalfParticles,
                "TEXTURE_SRC_CODE_HALF_PARTICLES"),
            Entry(
                CodePixelSamplerSource.HalfParticlesZ,
                "TEXTURE_SRC_CODE_HALF_PARTICLES_Z"),
            Entry(
                CodePixelSamplerSource.CaseTexture,
                "TEXTURE_SRC_CODE_CASE_TEXTURE"),
            Entry(
                CodePixelSamplerSource.CinematicY,
                "TEXTURE_SRC_CODE_CINEMATIC_Y"),
            Entry(
                CodePixelSamplerSource.CinematicCr,
                "TEXTURE_SRC_CODE_CINEMATIC_CR"),
            Entry(
                CodePixelSamplerSource.CinematicCb,
                "TEXTURE_SRC_CODE_CINEMATIC_CB"),
            Entry(
                CodePixelSamplerSource.CinematicA,
                "TEXTURE_SRC_CODE_CINEMATIC_A"),
            Entry(
                CodePixelSamplerSource.ReflectionProbe,
                "TEXTURE_SRC_CODE_REFLECTION_PROBE",
                textureTarget: "TextureCube"),
            Entry(
                CodePixelSamplerSource.AlternateScene,
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

    internal static bool HasRuntimeRequirement(uint argument) =>
        TryResolve(argument, out CodePixelSamplerAbiEntry entry) &&
        entry.HasRuntimeRequirement;

    private static CodePixelSamplerAbiEntry Entry(
        CodePixelSamplerSource source,
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
