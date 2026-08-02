namespace IW4.Render.Assets;

/// <summary>Independent immutable render-resource input status.</summary>
public enum MapRenderWorldTextureResourceStatus
{
    Unavailable = 0,
    Ready = 1,
    SourceProviderUnavailable = 2,
    SamplerShapeUnavailable = 3,
    ImageDecodeFailed = 4
}
