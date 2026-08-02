using System.Numerics;

namespace IW4.Render.EditorPreview;

public readonly record struct MapRenderEditorPreviewFilmCodeConstantRow(
    ushort SourceRowIndex,
    Vector4 Value);

/// <summary>
/// Renderer-global operands applied after a vision's authored film values.
/// They are deliberately separate from the .vision payload because the native
/// producer reads them from renderer dvars.
/// </summary>
public sealed record MapRenderEditorPreviewFilmDvarState(
    float Desaturation,
    float Contrast,
    float Brightness,
    bool AltShader,
    float BlackLevel)
{
    /// <summary>
    /// Registered PS3 defaults: r_desaturation=0.8,
    /// r_contrast=0.83, r_brightness=-0.07, r_filmAltShader=true, and
    /// r_blacklevel=0. The late material selector uses AltShader directly;
    /// the other values feed the color-row producer when color2 is selected.
    /// </summary>
    public static MapRenderEditorPreviewFilmDvarState RegisteredDefault
        { get; } = new(
            Desaturation: 0.8f,
            Contrast: 0.83f,
            Brightness: -0.07f,
            AltShader: true,
            BlackLevel: 0f);

    public bool SelectsPostFxColor2 => !AltShader;

    public bool HasFiniteValues =>
        float.IsFinite(Desaturation) &&
        float.IsFinite(Contrast) &&
        float.IsFinite(Brightness) &&
        float.IsFinite(BlackLevel);
}

/// <summary>
/// Produces the four direct-code rows consumed by the postfx_color2 fragment
/// program.
/// </summary>
public static class MapRenderEditorPreviewFilmCodeConstantProducer
{
    public const ushort ColorBiasRowIndex = 0x2d;
    public const ushort ColorTintBaseRowIndex = 0x2e;
    public const ushort ColorTintDeltaRowIndex = 0x2f;
    public const ushort ColorTintQuadraticRowIndex = 0x30;

    private const float MinimumDesaturation = 1f / 4096f;

    public static IReadOnlyList<MapRenderEditorPreviewFilmCodeConstantRow>
        Produce(MapRenderEditorPreviewFilmVisionState film) =>
        Produce(film, MapRenderEditorPreviewFilmDvarState.RegisteredDefault);

    public static IReadOnlyList<MapRenderEditorPreviewFilmCodeConstantRow>
        Produce(
            MapRenderEditorPreviewFilmVisionState film,
            MapRenderEditorPreviewFilmDvarState dvars)
    {
        ArgumentNullException.ThrowIfNull(film);
        ArgumentNullException.ThrowIfNull(dvars);
        RequireFinite(dvars);

        if (!film.Enabled)
        {
            return
            [
                Row(ColorBiasRowIndex, 0f, 0f, 0f, 4095f),
                Row(
                    ColorTintBaseRowIndex,
                    MinimumDesaturation,
                    MinimumDesaturation,
                    MinimumDesaturation,
                    0f),
                Row(ColorTintDeltaRowIndex, 0f, 0f, 0f, 0f),
                Row(ColorTintQuadraticRowIndex, 0f, 0f, 0f, 0f)
            ];
        }

        float authoredDesaturation = film.Desaturation;
        float desaturationMix =
            (1f - authoredDesaturation) * dvars.Desaturation +
            authoredDesaturation;
        float processedDesaturation =
            authoredDesaturation * desaturationMix;
        float d = MathF.Max(MinimumDesaturation, processedDesaturation);
        float contrast = film.Contrast * dvars.Contrast;
        float brightness = film.Brightness + dvars.Brightness;
        float preScale = dvars.AltShader ? contrast : contrast * d;
        float denominator = MathF.Max(
            1f - dvars.BlackLevel,
            MinimumDesaturation);
        float scale = preScale / denominator;
        float bias = brightness + 0.5f - 0.5f * contrast -
                     dvars.BlackLevel / denominator;
        if (film.Invert)
        {
            scale = -scale;
            bias += 1f;
        }

        Vector3 baseTint = film.DarkTint * scale;
        Vector3 linearTint =
            2f * (film.MediumTint - film.DarkTint) * scale;
        Vector3 quadraticTint =
            (film.LightTint + film.DarkTint - 2f * film.MediumTint) *
            scale;
        return
        [
            Row(
                ColorBiasRowIndex,
                bias,
                bias,
                bias,
                1f / d - 1f),
            Row(
                ColorTintBaseRowIndex,
                baseTint.X,
                baseTint.Y,
                baseTint.Z,
                film.DesaturationDark),
            Row(
                ColorTintDeltaRowIndex,
                linearTint.X,
                linearTint.Y,
                linearTint.Z,
                processedDesaturation - film.DesaturationDark),
            Row(
                ColorTintQuadraticRowIndex,
                quadraticTint.X,
                quadraticTint.Y,
                quadraticTint.Z,
                0f)
        ];
    }

    private static void RequireFinite(
        MapRenderEditorPreviewFilmDvarState dvars)
    {
        if (!dvars.HasFiniteValues)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dvars),
                "Renderer-global film dvars must be finite.");
        }
    }

    private static MapRenderEditorPreviewFilmCodeConstantRow Row(
        ushort index,
        float x,
        float y,
        float z,
        float w) => new(index, new Vector4(x, y, z, w));
}
