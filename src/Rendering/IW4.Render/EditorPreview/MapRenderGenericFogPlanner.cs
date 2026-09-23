using System.Numerics;

using IW4.Render.Execution;
using IW4.Render.Execution.Fog;
using IW4.Render.Shaders;
using IW4.Render.Transforms;

namespace IW4.Render.EditorPreview;

public enum MapRenderGenericFogSource
{
    Disabled = 0,
    ActiveFog,
    EditorAtmosphere
}

public readonly record struct MapRenderGenericFogPlan(
    MapRenderGenericFogSource Source,
    Vector3 FogColor,
    Vector3 SunFogColor,
    float AtmosphereStartDistance,
    float AtmosphereEndDistance,
    float AtmosphereMaxOpacity,
    float FogDistanceScale,
    float FogDistanceBias,
    float FogMinimumVisibility,
    bool SunFogEnabled,
    Vector3 SunFogDirection,
    float SunFogDistanceScale,
    float SunFogEndCosine,
    float SunFogAngularScale)
{
    public bool IsEnabled =>
        Source != MapRenderGenericFogSource.Disabled;

    public bool UsesActiveFog =>
        Source == MapRenderGenericFogSource.ActiveFog;
}

public readonly record struct MapRenderGenericActiveFogEvaluation(
    float Visibility,
    float SunFogFactor,
    Vector3 FogColor);

/// <summary>
/// Adapts the PS3 frame-fog rows consumed by translated programs
/// to the generic EditorPreview shader. Caller-owned atmosphere remains a
/// separate linear-distance fallback only when no active fog state exists.
/// </summary>
public static class MapRenderGenericFogPlanner
{
    internal const float NaturalExponentToBase2 =
        1.4426950408889634f;
    internal const float MinimumSquaredCameraDistance = 0.0000001f;

    public static MapRenderGenericFogPlan Resolve(
        bool fogRenderingEnabled,
        MapRenderActiveFogState? activeFog,
        MapRenderEditorPreviewAtmospherePlan? atmosphere,
        bool shaderConsumesLinearFogColor)
    {
        if (!fogRenderingEnabled)
            return default;
        if (activeFog is not null)
            return FromActiveFog(
                activeFog,
                shaderConsumesLinearFogColor);
        if (atmosphere?.IsEnabled == true)
            return FromAtmosphere(atmosphere);
        return default;
    }

    public static MapRenderGenericActiveFogEvaluation EvaluateActiveFog(
        MapRenderGenericFogPlan plan,
        Vector3 renderCameraOffset)
    {
        if (!plan.UsesActiveFog)
        {
            throw new ArgumentException(
                "An active-fog evaluation requires an active-fog plan.",
                nameof(plan));
        }
        if (!IsFinite(renderCameraOffset))
            throw new ArgumentOutOfRangeException(nameof(renderCameraOffset));

        float distance = MathF.Sqrt(MathF.Max(
            renderCameraOffset.LengthSquared(),
            MinimumSquaredCameraDistance));
        float fogVisibility = MathF.Max(
            ExpNatural(
                plan.FogDistanceScale * distance +
                plan.FogDistanceBias),
            plan.FogMinimumVisibility);
        if (!plan.SunFogEnabled)
        {
            return new(
                Math.Clamp(fogVisibility, 0f, 1f),
                0f,
                plan.FogColor);
        }

        float directionalCosine = Vector3.Dot(
            renderCameraOffset / distance,
            plan.SunFogDirection);
        float sunFogFactor = Math.Clamp(
            (directionalCosine - plan.SunFogEndCosine) *
            plan.SunFogAngularScale,
            0f,
            1f);
        float sunVisibility = MathF.Max(
            ExpNatural(
                plan.SunFogDistanceScale * distance +
                plan.FogDistanceBias),
            plan.FogMinimumVisibility);
        float visibility = Math.Clamp(
            sunFogFactor * (sunVisibility - fogVisibility) +
            fogVisibility,
            0f,
            1f);
        return new(
            visibility,
            sunFogFactor,
            Vector3.Lerp(
                plan.FogColor,
                plan.SunFogColor,
                sunFogFactor));
    }

    private static MapRenderGenericFogPlan FromActiveFog(
        MapRenderActiveFogState activeFog,
        bool shaderConsumesLinearFogColor)
    {
        IReadOnlyDictionary<int, ShaderConstantValue> rows =
            FrameDirectCodeConstants
                .ProduceFogRows(
                    fogRenderingEnabled: true,
                    activeFog)
                .ToDictionary(
                    row => row.SourceRowIndex,
                    row => row.Value);
        ShaderConstantValue fogRow =
            rows[FrameDirectCodeConstants.FogRowIndex];
        ShaderConstantValue fogColor = rows[
            shaderConsumesLinearFogColor
                ? FrameDirectCodeConstants.FogColorLinearRowIndex
                : FrameDirectCodeConstants.FogColorGammaRowIndex];

        bool sunFogEnabled = activeFog.SunFog.Enabled;
        ShaderConstantValue sunFogColor = fogColor;
        ShaderConstantValue sunFogConstants = default;
        Vector3 renderSunFogDirection = Vector3.Zero;
        if (sunFogEnabled)
        {
            sunFogColor = rows[
                shaderConsumesLinearFogColor
                    ? FrameDirectCodeConstants.SunFogColorLinearRowIndex
                    : FrameDirectCodeConstants.SunFogColorGammaRowIndex];
            sunFogConstants =
                rows[FrameDirectCodeConstants.SunFogConstantsRowIndex];
            ShaderConstantValue gameDirection =
                rows[FrameDirectCodeConstants.SunFogDirectionRowIndex];
            renderSunFogDirection = RenderCoordinateConverter
                .GameToRenderPosition(new Vector3(
                    gameDirection.X,
                    gameDirection.Y,
                    gameDirection.Z));
        }

        return new(
            MapRenderGenericFogSource.ActiveFog,
            ToVector3(fogColor),
            ToVector3(sunFogColor),
            AtmosphereStartDistance: 0f,
            AtmosphereEndDistance: 1f,
            AtmosphereMaxOpacity: 0f,
            FogDistanceScale: fogRow.Z,
            FogDistanceBias: fogRow.W,
            FogMinimumVisibility: fogRow.Y,
            sunFogEnabled,
            renderSunFogDirection,
            SunFogDistanceScale: sunFogConstants.X,
            SunFogEndCosine: sunFogConstants.Y,
            SunFogAngularScale: sunFogConstants.Z);
    }

    private static MapRenderGenericFogPlan FromAtmosphere(
        MapRenderEditorPreviewAtmospherePlan atmosphere) =>
        new(
            MapRenderGenericFogSource.EditorAtmosphere,
            atmosphere.FogColor,
            atmosphere.FogColor,
            atmosphere.StartDistance,
            atmosphere.EndDistance,
            atmosphere.MaxOpacity,
            FogDistanceScale: 0f,
            FogDistanceBias: 0f,
            FogMinimumVisibility: 1f,
            SunFogEnabled: false,
            SunFogDirection: Vector3.Zero,
            SunFogDistanceScale: 0f,
            SunFogEndCosine: 0f,
            SunFogAngularScale: 0f);

    private static float ExpNatural(float exponent) =>
        MathF.Pow(2f, exponent * NaturalExponentToBase2);

    private static Vector3 ToVector3(
        ShaderConstantValue value) =>
        new(value.X, value.Y, value.Z);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}
