using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.Execution.Fog;
using IW4.Render.Shaders;

namespace IW4.Render.Execution;

internal readonly record struct DirectionalSunLinearColors(
    System.Numerics.Vector3 Diffuse,
    System.Numerics.Vector3 Specular);

internal readonly record struct ClipSpaceLookupCodeConstants(
    ShaderConstantValue Scale,
    ShaderConstantValue Offset);

/// <summary>
/// Shared frame-row calculations used by translated-program
/// EditorPreview rendering.
/// </summary>
internal static class FrameDirectCodeConstants
{
    internal const ushort DirectionalLightDirectionRowIndex =
        (ushort)MaterialConstantSource.LightPosition;
    internal const ushort DirectionalLightDiffuseRowIndex =
        (ushort)MaterialConstantSource.LightDiffuse;
    internal const ushort DirectionalLightSpecularRowIndex =
        (ushort)MaterialConstantSource.LightSpecular;
    internal const ushort LightSpotFactorsRowIndex =
        (ushort)MaterialConstantSource.LightSpotFactors;
    internal const ushort LightFalloffPlacementRowIndex =
        (ushort)MaterialConstantSource.LightFalloffPlacement;
    // Registered default: r_diffuseColorScale=1.0.
    internal const float DefaultDiffuseColorScale = 1.0f;
    // Registered default: r_specularColorScale=2.5.
    internal const float DefaultSpecularColorScale = 2.5f;
    internal const int GameTimeRowIndex =
        (int)MaterialConstantSource.GameTime;
    internal const float GameTimeWrapSeconds = 43200.0f;

    // PS3 R_SetupSunShadowMaps publishes these two rows with the same
    // projection revision as the shadow lookup matrix and atlas contents.
    // They deliberately have no source-initialization value: treating either
    // row as zero before that projection exists changes cascade selection.
    internal const ushort SunShadowSwitchPartitionRowIndex =
        (ushort)MaterialConstantSource.ShadowMapSwitchPartition;
    internal const ushort SunShadowMapScaleRowIndex =
        (ushort)MaterialConstantSource.ShadowMapScale;
    internal const ushort ZNearRowIndex =
        (ushort)MaterialConstantSource.ZNear;
    internal const float ZNearScale = 0.984375f;

    // Sampling transform for the 512x256x4 model-lighting program image.
    internal const ushort ModelLightingSamplerRowIndex =
        (ushort)MaterialConstantSource.LightingLookupScale;
    internal const ushort MaterialColorRowIndex =
        (ushort)MaterialConstantSource.MaterialColor;
    internal const ushort StaticModelBaseLightingCoordsRowIndex =
        (ushort)MaterialConstantSource.BaseLightingCoords;
    // The static-model light-probe path uploads this row from the draw-instance
    // GroundLighting value (or its light-grid-derived runtime equivalent)
    // immediately before the Event22 draw.
    internal const ushort StaticModelLightProbeAmbientRowIndex =
        (ushort)MaterialConstantSource.LightProbeAmbient;

    // R_UpdateViewport writes these from the active render-target extent and
    // viewport. PS3's direct-table indices differ from the correlated desktop
    // IW4 names, but the fastfile arguments establish rows 0x3E/0x3F.
    internal const ushort ClipSpaceLookupScaleRowIndex =
        (ushort)MaterialConstantSource.ClipSpaceLookupScale;
    internal const ushort ClipSpaceLookupOffsetRowIndex =
        (ushort)MaterialConstantSource.ClipSpaceLookupOffset;

    internal const ushort FogRowIndex =
        (ushort)MaterialConstantSource.Fog;
    internal const ushort FogColorLinearRowIndex =
        (ushort)MaterialConstantSource.FogColorLinear;
    internal const ushort FogColorGammaRowIndex =
        (ushort)MaterialConstantSource.FogColorGamma;
    internal const ushort SunFogConstantsRowIndex =
        (ushort)MaterialConstantSource.FogSunConstants;
    internal const ushort SunFogColorLinearRowIndex =
        (ushort)MaterialConstantSource.FogSunColorLinear;
    internal const ushort SunFogColorGammaRowIndex =
        (ushort)MaterialConstantSource.FogSunColorGamma;
    internal const ushort SunFogDirectionRowIndex =
        (ushort)MaterialConstantSource.FogSunDirection;

    private const float TwoPi = 6.283185482025146484375f;

    // Exact PS3 single-precision conversion literals.
    private static readonly float ByteToUnit = Float(0x3B808081);
    private static readonly float DegreesToRadians = Float(0x3C8EFA35);
    private static readonly float InvalidFadeRangeScale = Float(0x42C80000);

    private static readonly IReadOnlyList<
        DirectCodeConstantRow> SourceInitializationRows =
    [
        // Row 0x05 uses scalar initialization even when the normal-view caller
        // supplies its input table.
        new(
            (int)MaterialConstantSource.LightFalloffPlacement,
            new ShaderConstantValue(
                float.MaxValue,
                float.MaxValue,
                float.MaxValue,
                0.0f)),

        // Normal-view row 0x23 has type-table value 2, so initialization
        // preserves the copied view-input zero instead of taking the scalar
        // sentinel branch.
        new(
            MaterialColorRowIndex,
            new ShaderConstantValue(
                0.0f,
                0.0f,
                0.0f,
                0.0f))
    ];

    private static readonly IReadOnlyList<
        DirectCodeConstantRow> FogTemplateRows =
        Array.AsReadOnly(
            Enumerable.Range(
                    FogRowIndex,
                    SunFogDirectionRowIndex - FogRowIndex + 1)
                .Select(index => new
                    DirectCodeConstantRow(
                        index,
                        new ShaderConstantValue(0f, 0f, 0f, 0f)))
                .ToArray());

    private static readonly IReadOnlyList<
        DirectCodeConstantRow> DisabledFogRows =
        Array.AsReadOnly(
        [
            Row(FogRowIndex, new(0.0f, 1.0f, 0.0f, 0.0f))
        ]);

    internal static IReadOnlyList<DirectCodeConstantRow>
        ProduceSourceInitializationRows() => SourceInitializationRows;

    internal static DirectCodeConstantRow
        ProduceLightFalloffPlacementInitializationRow() =>
        SourceInitializationRows[0];

    internal static ClipSpaceLookupCodeConstants
        ProduceClipSpaceLookup(
            int renderTargetWidth,
            int renderTargetHeight,
            int viewportX,
            int viewportY,
            int viewportWidth,
            int viewportHeight)
    {
        if (renderTargetWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(renderTargetWidth));
        if (renderTargetHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(renderTargetHeight));
        if (viewportX < 0 || viewportWidth <= 0 ||
            viewportX > renderTargetWidth - viewportWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(viewportWidth));
        }
        if (viewportY < 0 || viewportHeight <= 0 ||
            viewportY > renderTargetHeight - viewportHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(viewportHeight));
        }

        float inverseWidth = 1.0f / renderTargetWidth;
        float inverseHeight = 1.0f / renderTargetHeight;
        float scaleX =
            0.5f * inverseWidth * viewportWidth;
        float scaleY =
            0.5f * inverseHeight * viewportHeight;
        float offsetX =
            scaleX +
            inverseWidth * viewportX;
        float offsetY =
            scaleY +
            inverseHeight * viewportY;

        return new ClipSpaceLookupCodeConstants(
            new ShaderConstantValue(
                scaleX,
                -scaleY,
                0.0f,
                1.0f),
            new ShaderConstantValue(
                offsetX,
                offsetY,
                0.0f,
                0.0f));
    }

    internal static DirectCodeConstantRow ProduceZNear(
        float zNear)
    {
        if (!(zNear > 0.0f) || !float.IsFinite(zNear))
            throw new ArgumentOutOfRangeException(nameof(zNear));

        return Row(
            ZNearRowIndex,
            new ShaderConstantValue(
                zNear * ZNearScale,
                0.0f,
                0.0f,
                0.0f));
    }

    internal static DirectCodeConstantRow ProduceGameTime(
        float gameTime)
    {
        if (!float.IsFinite(gameTime))
            throw new ArgumentOutOfRangeException(nameof(gameTime));

        float whole = MathF.Floor(gameTime);
        float fraction = gameTime - whole;
        float angle = fraction * TwoPi;
        return new(
            GameTimeRowIndex,
            new ShaderConstantValue(
                MathF.Sin(angle),
                MathF.Cos(angle),
                fraction,
                gameTime % GameTimeWrapSeconds));
    }

    internal static IReadOnlyList<DirectCodeConstantRow>
        ProduceFogTemplateRows() => FogTemplateRows;

    internal static IReadOnlyList<DirectCodeConstantRow>
        ProduceDisabledFogRows() => DisabledFogRows;

    internal static IReadOnlyList<DirectCodeConstantRow>
        ProduceFogRows(
            bool fogRenderingEnabled,
            MapRenderActiveFogState activeFog)
    {
        ArgumentNullException.ThrowIfNull(activeFog);

        if (!fogRenderingEnabled)
            return ProduceDisabledFogRows();

        ShaderConstantValue fogGamma = UnpackBgra(activeFog.Color);
        ShaderConstantValue fogLinear = Linearize(fogGamma);
        var rows = new List<DirectCodeConstantRow>
        {
            // Native write order is retained: color rows precede FOG.
            Row(FogColorLinearRowIndex, fogLinear),
            Row(FogColorGammaRowIndex, fogGamma),
            Row(
                FogRowIndex,
                new(
                    0.0f,
                    1.0f - activeFog.FogMaxOpacity,
                    -activeFog.Density,
                    activeFog.Density * activeFog.FogStart))
        };

        if (!activeFog.SunFog.Enabled)
            return ReadOnly(rows);

        MapRenderActiveSunFogState sunFog = activeFog.SunFog;
        float beginRadians =
            sunFog.BeginFadeAngleDegrees * DegreesToRadians;
        float endRadians = sunFog.EndFadeAngleDegrees * DegreesToRadians;
        float beginCosine = MathF.Cos(beginRadians);
        float endCosine = MathF.Cos(endRadians);
        float angularScale = beginCosine > endCosine
            ? 1.0f / (beginCosine - endCosine)
            : InvalidFadeRangeScale;
        float negativeDensity = -activeFog.Density;

        ShaderConstantValue sunGamma = UnpackBgra(sunFog.Color);
        ShaderConstantValue sunLinear = Linearize(sunGamma);
        rows.Add(Row(
            SunFogConstantsRowIndex,
            new(
                negativeDensity * sunFog.Scale,
                endCosine,
                angularScale,
                negativeDensity)));
        rows.Add(Row(SunFogColorLinearRowIndex, sunLinear));
        rows.Add(Row(SunFogColorGammaRowIndex, sunGamma));
        rows.Add(Row(
            SunFogDirectionRowIndex,
            NormalizeDirection(sunFog.Direction)));
        return ReadOnly(rows);
    }

    private static ShaderConstantValue UnpackBgra(
        MapRenderBgra8Color color) =>
        new(
            color.Red * ByteToUnit,
            color.Green * ByteToUnit,
            color.Blue * ByteToUnit,
            color.Alpha * ByteToUnit);

    private static ShaderConstantValue Linearize(
        ShaderConstantValue gamma) =>
        new(
            GammaColorTransfer.ToLinear(gamma.X),
            GammaColorTransfer.ToLinear(gamma.Y),
            GammaColorTransfer.ToLinear(gamma.Z),
            gamma.W);

    private static ShaderConstantValue NormalizeDirection(
        System.Numerics.Vector3 direction)
    {
        // Preserve the PS3 operation order: y*y, then fused x*x and z*z.
        float lengthSquared = direction.Y * direction.Y;
        lengthSquared = MathF.FusedMultiplyAdd(
            direction.X,
            direction.X,
            lengthSquared);
        lengthSquared = MathF.FusedMultiplyAdd(
            direction.Z,
            direction.Z,
            lengthSquared);
        float length = MathF.Sqrt(lengthSquared);
        float divisor = length == 0.0f ? 1.0f : length;
        float inverse = 1.0f / divisor;
        return new(
            direction.X * inverse,
            direction.Y * inverse,
            direction.Z * inverse,
            0.0f);
    }

    private static DirectCodeConstantRow Row(
        ushort index,
        ShaderConstantValue value)
    {
        if (!IsFinite(value))
        {
            throw new InvalidOperationException(
                $"Active fog state produced a nonfinite direct code row 0x{index:X2}.");
        }

        return new(index, value);
    }

    private static IReadOnlyList<DirectCodeConstantRow>
        ReadOnly(List<DirectCodeConstantRow> rows) =>
        Array.AsReadOnly(rows.ToArray());

    private static bool IsFinite(ShaderConstantValue value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        float.IsFinite(value.W);

    private static float Float(int bits) =>
        BitConverter.Int32BitsToSingle(bits);
}
