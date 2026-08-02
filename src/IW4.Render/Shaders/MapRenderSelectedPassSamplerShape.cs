namespace IW4.Render.Shaders;

/// <summary>
/// Texture dimensionality required by one selected-pass sampler destination.
/// Values remain unknown unless the PS3 descriptor fields form a supported,
/// internally coherent tuple.
/// </summary>
public enum MapRenderSelectedPassSamplerShape
{
    Unknown = 0,
    TwoDimensional = 1,
    Cube = 2
}
