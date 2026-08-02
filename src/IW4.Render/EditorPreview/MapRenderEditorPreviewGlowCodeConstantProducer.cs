using System.Numerics;

namespace IW4.Render.EditorPreview;

public readonly record struct MapRenderEditorPreviewGlowCodeConstantRow(
    ushort SourceRowIndex,
    Vector4 Value);

/// <summary>
/// Produces the two direct-code rows used by the native glow setup and
/// additive-apply materials.
/// </summary>
public static class MapRenderEditorPreviewGlowCodeConstantProducer
{
    public const ushort GlowSetupRowIndex = 0x2b;
    public const ushort GlowApplyRowIndex = 0x2c;

    // IEC 61966-2-1 transfer constants used by the renderer.
    private const float LinearCutoffThreshold = 0.0392800010740757f;
    private const float LinearCutoffScale = 0.07739938050508499f;
    private const float NonlinearCutoffBias = 0.054999999701976776f;
    private const float NonlinearCutoffScale = 0.9478673338890076f;
    private const float NonlinearCutoffExponent = 2.4000000953674316f;

    public static IReadOnlyList<MapRenderEditorPreviewGlowCodeConstantRow>
        Produce(MapRenderEditorPreviewGlowVisionState glow)
    {
        ArgumentNullException.ThrowIfNull(glow);
        RequireValid(glow);

        float cutoffLinear = glow.BloomCutoff <= LinearCutoffThreshold
            ? glow.BloomCutoff * LinearCutoffScale
            : MathF.Pow(
                (glow.BloomCutoff + NonlinearCutoffBias) *
                NonlinearCutoffScale,
                NonlinearCutoffExponent);
        float cutoffRescale = 1f / (1f - cutoffLinear);

        return
        [
            new MapRenderEditorPreviewGlowCodeConstantRow(
                GlowSetupRowIndex,
                new Vector4(
                    cutoffLinear,
                    cutoffRescale,
                    0f,
                    glow.BloomDesaturation)),
            new MapRenderEditorPreviewGlowCodeConstantRow(
                GlowApplyRowIndex,
                new Vector4(0f, 0f, 0f, glow.BloomIntensity))
        ];
    }

    private static void RequireValid(
        MapRenderEditorPreviewGlowVisionState glow)
    {
        if (!float.IsFinite(glow.Radius) || glow.Radius < 0f ||
            !float.IsFinite(glow.BloomCutoff) ||
            glow.BloomCutoff < 0f || glow.BloomCutoff >= 1f ||
            !float.IsFinite(glow.BloomDesaturation) ||
            !float.IsFinite(glow.BloomIntensity) ||
            glow.BloomIntensity < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(glow),
                "Glow radius/intensity must be finite and nonnegative; cutoff must be finite in [0, 1).");
        }
    }
}
