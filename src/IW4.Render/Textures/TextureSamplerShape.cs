namespace IW4.Render.Textures;

/// <summary>
/// Texture dimensionality required by one authored sampler binding.
/// Values remain unknown unless the PS3 descriptor fields form a supported,
/// internally coherent tuple.
/// </summary>
public enum TextureSamplerShape
{
    Unknown = 0,
    TwoDimensional = 1,
    Cube = 2
}
