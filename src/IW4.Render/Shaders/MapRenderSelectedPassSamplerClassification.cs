namespace IW4.Render.Shaders;

/// <summary>
/// Immutable shape/resource classification for one authored or implicit
/// selected-pass sampler. This is diagnostic/planning state; it does not imply
/// that a texture has been decoded, uploaded, or populated for the frame.
/// </summary>
public sealed record MapRenderSelectedPassSamplerClassification(
    MapRenderSelectedPassSamplerShape Shape,
    MapRenderSelectedPassSamplerResourceStatus ResourceStatus,
    string ResourceIdentity)
{
    public bool HasKnownShape =>
        Shape != MapRenderSelectedPassSamplerShape.Unknown;
}
