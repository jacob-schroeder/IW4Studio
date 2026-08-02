using System.Numerics;

namespace IW4.Render.EditorPreview;

public sealed record MapRenderEditorPreviewPrimaryLightVisionState(
    bool UseTweaks,
    float DiffuseStrength,
    float SpecularStrength)
{
    public bool HasFiniteNonnegativeStrengths =>
        float.IsFinite(DiffuseStrength) && DiffuseStrength >= 0f &&
        float.IsFinite(SpecularStrength) && SpecularStrength >= 0f;
}

public sealed record MapRenderEditorPreviewFilmVisionState(
    bool Enabled,
    float Contrast,
    float Brightness,
    float Desaturation,
    float DesaturationDark,
    bool Invert,
    Vector3 LightTint,
    Vector3 MediumTint,
    Vector3 DarkTint);

public sealed record MapRenderEditorPreviewGlowVisionState(
    bool Enabled,
    float Radius,
    float BloomCutoff,
    float BloomDesaturation,
    float BloomIntensity);

/// <summary>
/// Immediate naked-vision state selected by one map createart script. The
/// state is immutable and belongs to the same canonical pool revision as the
/// scene; no RawFile parsing occurs while rendering frames.
/// </summary>
public sealed record MapRenderEditorPreviewVisionState(
    string Name,
    MapRenderEditorPreviewPrimaryLightVisionState PrimaryLight,
    MapRenderEditorPreviewFilmVisionState Film,
    MapRenderEditorPreviewGlowVisionState Glow)
{
    public string CanonicalRawFileName => $"vision/{Name}.vision";
}
