namespace MapConverter.Game.IW3.PC.Techniques;

/// <summary>
/// IW3's 34 material-technique table slots, in serialized order.
/// </summary>
internal enum Iw3TechniqueSlot : byte
{
    DepthPrepass = 0,
    BuildFloatZ = 1,
    BuildShadowmapDepth = 2,
    BuildShadowmapColor = 3,
    Unlit = 4,
    Emissive = 5,
    EmissiveShadow = 6,
    Lit = 7,
    LitSun = 8,
    LitSunShadow = 9,
    LitSpot = 10,
    LitSpotShadow = 11,
    LitOmni = 12,
    LitOmniShadow = 13,
    LitInstanced = 14,
    LitInstancedSun = 15,
    LitInstancedSunShadow = 16,
    LitInstancedSpot = 17,
    LitInstancedSpotShadow = 18,
    LitInstancedOmni = 19,
    LitInstancedOmniShadow = 20,
    LightSpot = 21,
    LightOmni = 22,
    LightSpotShadow = 23,
    FakeLightNormal = 24,
    FakeLightView = 25,
    SunlightPreview = 26,
    CaseTexture = 27,
    WireframeSolid = 28,
    WireframeShaded = 29,
    ShadowCookieCaster = 30,
    ShadowCookieReceiver = 31,
    DebugBumpmap = 32,
    DebugBumpmapInstanced = 33,
}

internal enum Iw3ShaderStage
{
    Vertex,
    Pixel,
}

internal enum Iw3CodeShaderValueKind
{
    Constant,
    Sampler,
}

internal sealed record Iw3TechniqueSetSource(
    string Name,
    IReadOnlyList<Iw3TechniqueSlotSource> Slots);

internal sealed record Iw3TechniqueSlotSource(
    Iw3TechniqueSlot Slot,
    string DisplayName,
    string? TechniqueName,
    Iw3TechniqueSource? Technique);

internal sealed record Iw3TechniqueSource(
    string Name,
    IReadOnlyList<Iw3TechniquePassSource> Passes);

internal sealed record Iw3TechniquePassSource(
    string StateMapName,
    Iw3ShaderSource VertexShader,
    Iw3ShaderSource PixelShader,
    IReadOnlyList<Iw3VertexStreamRoutingSource> VertexRouting);

internal sealed record Iw3ShaderSource(
    Iw3ShaderStage Stage,
    Iw3ShaderModel ShaderModel,
    string ProgramName,
    IReadOnlyList<Iw3ShaderArgumentSource> Arguments);

internal readonly record struct Iw3ShaderModel(int Major, int Minor);

internal sealed record Iw3ShaderArgumentSource(
    Iw3IndexedName Destination,
    Iw3ShaderValueSource Value);

internal abstract record Iw3ShaderValueSource;

internal sealed record Iw3CodeShaderValueSource(
    Iw3CodeShaderValueKind Kind,
    string Accessor,
    int? ElementIndex) : Iw3ShaderValueSource;

internal sealed record Iw3LiteralShaderValueSource(
    float X,
    float Y,
    float Z,
    float W) : Iw3ShaderValueSource;

internal sealed record Iw3MaterialShaderValueSource(
    string? PropertyName,
    uint? PropertyHash) : Iw3ShaderValueSource;

internal sealed record Iw3VertexStreamRoutingSource(
    Iw3IndexedName Destination,
    Iw3IndexedName Source);

internal readonly record struct Iw3IndexedName(string Name, int? Index);
