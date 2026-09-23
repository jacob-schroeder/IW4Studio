using System.Numerics;

namespace IW4.Render.EditorPreview;

/// <summary>
/// Creates the explicit editor haze preset or validates caller-owned editor
/// settings. GfxWorld fog capability bits are deliberately not an input.
/// </summary>
public static class MapRenderEditorPreviewAtmospherePlanner
{
    public static readonly Vector3 NeutralHazeColor =
        new(0.68f, 0.66f, 0.62f);

    public const float DefaultMaxOpacity = 0.72f;

    public static MapRenderEditorPreviewAtmospherePlan Create(
        RenderBounds bounds,
        MapRenderEditorPreviewAtmosphereSettings? settings = null)
    {
        MapRenderEditorPreviewAtmosphereSettings effective =
            settings ?? CreatePresetSettings(bounds);
        if (!effective.Enabled)
        {
            return Disabled(
                MapRenderEditorPreviewAtmosphereStatus
                    .DisabledByEditorSettings,
                "Editor atmosphere was explicitly disabled.");
        }

        return new MapRenderEditorPreviewAtmospherePlan(
            settings is null
                ? MapRenderEditorPreviewAtmosphereStatus.EditorPreset
                : MapRenderEditorPreviewAtmosphereStatus
                    .ExplicitEditorSettings,
            isEnabled: true,
            effective.FogColor,
            effective.StartDistance,
            effective.EndDistance,
            effective.MaxOpacity,
            settings is null
                ? "Live Preview uses a bounded neutral haze preset because active fog is runtime-only state."
                : "Live Preview uses explicit caller-owned atmosphere settings.");
    }

    public static float FogFactor(
        MapRenderEditorPreviewAtmospherePlan plan,
        float cameraDistance)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!float.IsFinite(cameraDistance) || cameraDistance < 0f)
            throw new ArgumentOutOfRangeException(nameof(cameraDistance));
        if (!plan.IsEnabled)
            return 0f;

        float normalized = Math.Clamp(
            (cameraDistance - plan.StartDistance) /
            (plan.EndDistance - plan.StartDistance),
            0f,
            1f);
        return normalized * plan.MaxOpacity;
    }

    private static MapRenderEditorPreviewAtmosphereSettings
        CreatePresetSettings(RenderBounds bounds)
    {
        float extent = bounds.IsValid
            ? MathF.Max(
                bounds.Max.X - bounds.Min.X,
                MathF.Max(
                    bounds.Max.Y - bounds.Min.Y,
                    bounds.Max.Z - bounds.Min.Z))
            : 4096f;
        if (!float.IsFinite(extent) || extent <= 0f)
            extent = 4096f;

        float start = Math.Clamp(extent * 0.08f, 256f, 1024f);
        float end = Math.Clamp(extent * 0.65f, start + 1024f, 8192f);
        return new MapRenderEditorPreviewAtmosphereSettings(
            enabled: true,
            NeutralHazeColor,
            start,
            end,
            DefaultMaxOpacity);
    }

    private static MapRenderEditorPreviewAtmospherePlan Disabled(
        MapRenderEditorPreviewAtmosphereStatus status,
        string reason) =>
        new(
            status,
            isEnabled: false,
            Vector3.Zero,
            startDistance: 0f,
            endDistance: 1f,
            maxOpacity: 0f,
            reason);
}
