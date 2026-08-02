namespace IW4.Render.Shaders;

/// <summary>
/// Provenance/readiness state of the resource behind a selected-pass sampler.
/// Shape and resource status are deliberately independent: a sampler may have
/// a supported shape while its frame-produced contents are unavailable.
/// </summary>
public enum MapRenderSelectedPassSamplerResourceStatus
{
    Unknown = 0,
    MaterialImageDescriptorResolved = 1,
    CustomReflectionProbeDescriptorRequired = 2,
    CustomPrimaryLightmapDescriptorRequired = 3,
    CustomSecondaryLightmapDescriptorRequired = 4,
    DynamicSunShadowContentUnavailable = 5
}
