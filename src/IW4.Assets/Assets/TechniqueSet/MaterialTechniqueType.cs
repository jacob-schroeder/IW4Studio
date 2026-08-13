namespace IW4.Assets.Assets.TechniqueSet;

/// <summary>
/// PS3 IW4 material-technique table slots. This 37-slot console layout is not
/// the 48-slot PC layout; the instanced spot/omni shadow families are absent.
/// </summary>
public enum MaterialTechniqueType : byte
{
    DepthPrepass = 0x00,
    BuildFloatZ = 0x01,
    BuildShadowmapDepth = 0x02,
    BuildShadowmapColor = 0x03,
    Unlit = 0x04,
    Emissive = 0x05,
    EmissiveDfog = 0x06,
    EmissiveShadow = 0x07,
    EmissiveShadowDfog = 0x08,
    Lit = 0x09,
    LitDfog = 0x0A,
    LitSun = 0x0B,
    LitSunDfog = 0x0C,
    LitSunShadow = 0x0D,
    LitSunShadowDfog = 0x0E,
    LitSpot = 0x0F,
    LitSpotDfog = 0x10,
    LitSpotShadow = 0x11,
    LitSpotShadowDfog = 0x12,
    LitOmni = 0x13,
    LitOmniDfog = 0x14,
    LitOmniShadow = 0x15,
    LitOmniShadowDfog = 0x16,
    LitInstanced = 0x17,
    LitInstancedDfog = 0x18,
    LitInstancedSun = 0x19,
    LitInstancedSunDfog = 0x1A,
    LightSpot = 0x1B,
    LightOmni = 0x1C,
    LightSpotShadow = 0x1D,
    FakeLightNormal = 0x1E,
    FakeLightView = 0x1F,
    SunlightPreview = 0x20,
    CaseTexture = 0x21,
    WireframeSolid = 0x22,
    WireframeShaded = 0x23,
    DebugNormals = 0x24,

    Count = 0x25,
    TotalCount = 0x26,
    None = 0x27
}
