namespace IW4.Render.Execution;

/// <summary>
/// Exact IW4 <c>MaterialTextureSource</c> values used by
/// <c>CodePixelSampler</c> arguments.
/// </summary>
public enum MapRenderCodePixelSamplerSource : uint
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
public sealed record MapRenderCodePixelSamplerAbiEntry(
    MapRenderCodePixelSamplerSource Source,
    string AuthoredName,
    string ResourceIdentity,
    string TextureTarget,
    MapRenderShaderRuntimeSamplerResourceKind RuntimeResourceKind =
        MapRenderShaderRuntimeSamplerResourceKind.Unknown,
    MapRenderShaderRuntimeSamplerRequirementStatus RuntimeRequirementStatus =
        MapRenderShaderRuntimeSamplerRequirementStatus.Unknown)
{
    public uint Argument => (uint)Source;

    public bool HasRuntimeRequirement =>
        RuntimeResourceKind !=
            MapRenderShaderRuntimeSamplerResourceKind.Unknown &&
        RuntimeRequirementStatus !=
            MapRenderShaderRuntimeSamplerRequirementStatus.Unknown;
}

/// <summary>
/// Backend-neutral catalog for the complete PS3 IW4 code-image ABI. The
/// authored names are the exact <c>MaterialTextureSource</c> enumerators, and
/// the 0..26 extent is the descriptor-table boundary recovered from
/// <c>default_mp.elf</c>. The Xbox enum's value 27
/// (<c>TEXTURE_SRC_CODE_COLOR_MANIPULATION</c>) is not a PS3 descriptor; that
/// address overlaps the PS3 sampler-state table.
/// </summary>
public static class MapRenderCodePixelSamplerAbi
{
    private static readonly IReadOnlyList<MapRenderCodePixelSamplerAbiEntry>
        Catalog = Array.AsReadOnly(
        [
            Entry(
                MapRenderCodePixelSamplerSource.Black,
                "TEXTURE_SRC_CODE_BLACK"),
            Entry(
                MapRenderCodePixelSamplerSource.White,
                "TEXTURE_SRC_CODE_WHITE"),
            Entry(
                MapRenderCodePixelSamplerSource.IdentityNormalMap,
                "TEXTURE_SRC_CODE_IDENTITY_NORMAL_MAP"),
            Entry(
                MapRenderCodePixelSamplerSource.ModelLighting,
                "TEXTURE_SRC_CODE_MODEL_LIGHTING",
                resourceIdentity: "modelLightingSampler",
                textureTarget: "Texture3D",
                runtimeResourceKind: MapRenderShaderRuntimeSamplerResourceKind
                    .StaticModelLightingAtlas,
                runtimeRequirementStatus:
                    MapRenderShaderRuntimeSamplerRequirementStatus
                    .ImmutableSceneAtlasRequired),
            Entry(
                MapRenderCodePixelSamplerSource.LightmapPrimary,
                "TEXTURE_SRC_CODE_LIGHTMAP_PRIMARY"),
            Entry(
                MapRenderCodePixelSamplerSource.LightmapSecondary,
                "TEXTURE_SRC_CODE_LIGHTMAP_SECONDARY"),
            Entry(
                MapRenderCodePixelSamplerSource.ShadowmapSun,
                "TEXTURE_SRC_CODE_SHADOWMAP_SUN",
                resourceIdentity: "shadowmapSamplerSun",
                textureTarget: "Texture2DShadow",
                runtimeResourceKind:
                    MapRenderShaderRuntimeSamplerResourceKind.SunShadowAtlas,
                runtimeRequirementStatus:
                    MapRenderShaderRuntimeSamplerRequirementStatus
                    .SameRevisionAtlasRequired),
            Entry(
                MapRenderCodePixelSamplerSource.ShadowmapSpot,
                "TEXTURE_SRC_CODE_SHADOWMAP_SPOT",
                textureTarget: "Texture2DShadow"),
            Entry(
                MapRenderCodePixelSamplerSource.Feedback,
                "TEXTURE_SRC_CODE_FEEDBACK"),
            Entry(
                MapRenderCodePixelSamplerSource.ResolvedPostSun,
                "TEXTURE_SRC_CODE_RESOLVED_POST_SUN"),
            Entry(
                MapRenderCodePixelSamplerSource.ResolvedScene,
                "TEXTURE_SRC_CODE_RESOLVED_SCENE"),
            Entry(
                MapRenderCodePixelSamplerSource.PostEffect0,
                "TEXTURE_SRC_CODE_POST_EFFECT_0"),
            Entry(
                MapRenderCodePixelSamplerSource.PostEffect1,
                "TEXTURE_SRC_CODE_POST_EFFECT_1"),
            Entry(
                MapRenderCodePixelSamplerSource.LightAttenuation,
                "TEXTURE_SRC_CODE_LIGHT_ATTENUATION"),
            Entry(
                MapRenderCodePixelSamplerSource.Outdoor,
                "TEXTURE_SRC_CODE_OUTDOOR"),
            Entry(
                MapRenderCodePixelSamplerSource.FloatZ,
                "TEXTURE_SRC_CODE_FLOATZ"),
            Entry(
                MapRenderCodePixelSamplerSource.ProcessedFloatZ,
                "TEXTURE_SRC_CODE_PROCESSED_FLOATZ",
                resourceIdentity: "processedFloatZ",
                textureTarget: "Texture2D",
                runtimeResourceKind:
                    MapRenderShaderRuntimeSamplerResourceKind.ProcessedFloatZ,
                runtimeRequirementStatus:
                    MapRenderShaderRuntimeSamplerRequirementStatus
                    .SameRevisionTextureRequired),
            Entry(
                MapRenderCodePixelSamplerSource.RawFloatZ,
                "TEXTURE_SRC_CODE_RAW_FLOATZ"),
            Entry(
                MapRenderCodePixelSamplerSource.HalfParticles,
                "TEXTURE_SRC_CODE_HALF_PARTICLES"),
            Entry(
                MapRenderCodePixelSamplerSource.HalfParticlesZ,
                "TEXTURE_SRC_CODE_HALF_PARTICLES_Z"),
            Entry(
                MapRenderCodePixelSamplerSource.CaseTexture,
                "TEXTURE_SRC_CODE_CASE_TEXTURE"),
            Entry(
                MapRenderCodePixelSamplerSource.CinematicY,
                "TEXTURE_SRC_CODE_CINEMATIC_Y"),
            Entry(
                MapRenderCodePixelSamplerSource.CinematicCr,
                "TEXTURE_SRC_CODE_CINEMATIC_CR"),
            Entry(
                MapRenderCodePixelSamplerSource.CinematicCb,
                "TEXTURE_SRC_CODE_CINEMATIC_CB"),
            Entry(
                MapRenderCodePixelSamplerSource.CinematicA,
                "TEXTURE_SRC_CODE_CINEMATIC_A"),
            Entry(
                MapRenderCodePixelSamplerSource.ReflectionProbe,
                "TEXTURE_SRC_CODE_REFLECTION_PROBE",
                textureTarget: "TextureCube"),
            Entry(
                MapRenderCodePixelSamplerSource.AlternateScene,
                "TEXTURE_SRC_CODE_ALTERNATE_SCENE")
        ]);

    public static IReadOnlyList<MapRenderCodePixelSamplerAbiEntry> Entries =>
        Catalog;

    public static bool TryResolve(
        uint argument,
        out MapRenderCodePixelSamplerAbiEntry entry)
    {
        if (argument < (uint)Catalog.Count)
        {
            MapRenderCodePixelSamplerAbiEntry candidate =
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

    private static MapRenderCodePixelSamplerAbiEntry Entry(
        MapRenderCodePixelSamplerSource source,
        string authoredName,
        string? resourceIdentity = null,
        string textureTarget = "Texture2D",
        MapRenderShaderRuntimeSamplerResourceKind runtimeResourceKind =
            MapRenderShaderRuntimeSamplerResourceKind.Unknown,
        MapRenderShaderRuntimeSamplerRequirementStatus
            runtimeRequirementStatus =
                MapRenderShaderRuntimeSamplerRequirementStatus.Unknown) =>
        new(
            source,
            authoredName,
            resourceIdentity ?? authoredName,
            textureTarget,
            runtimeResourceKind,
            runtimeRequirementStatus);
}
