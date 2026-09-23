namespace IW4.Render.UI;

/// <summary>
/// Declares how closely a UI material preview represents the authored draw.
/// Texture approximation deliberately makes no claim that the material
/// technique, shader constants, or render state have been evaluated.
/// </summary>
public enum UiMaterialPreviewFidelity
{
    Unavailable = 0,
    TextureApproximation = 1
}
