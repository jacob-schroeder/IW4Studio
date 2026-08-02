using IW4.Render.Scheduling.Fog;
using IW4.Render.Shaders;
using IW4.Render.Lighting;
using IW4.Render.EditorPreview;

namespace IW4.Render.Execution;

internal readonly record struct MapRenderDirectionalSunLinearColors(
    System.Numerics.Vector3 Diffuse,
    System.Numerics.Vector3 Specular);

internal readonly record struct MapRenderClipSpaceLookupCodeConstants(
    MapRenderShaderConstantValue Scale,
    MapRenderShaderConstantValue Offset);

/// <summary>
/// Shared frame-row calculations used by translated-program
/// EditorPreview rendering.
/// </summary>
internal static class FrameDirectCodeConstants
{
    internal const ushort DirectionalLightDirectionRowIndex = 0x00;
    internal const ushort DirectionalLightDiffuseRowIndex = 0x01;
    internal const ushort DirectionalLightSpecularRowIndex = 0x02;
    // Registered default: r_diffuseColorScale=1.0.
    internal const float DefaultDiffuseColorScale = 1.0f;
    // Registered default: r_specularColorScale=2.5.
    internal const float DefaultSpecularColorScale = 2.5f;
    internal const int GameTimeRowIndex = 0x07;
    internal const float GameTimeWrapSeconds = 43200.0f;

    // PS3 R_SetupSunShadowMaps publishes these two rows with the same
    // projection revision as the shadow lookup matrix and atlas contents.
    // They deliberately have no source-initialization value: treating either
    // row as zero before that projection exists changes cascade selection.
    internal const ushort SunShadowSwitchPartitionRowIndex = 0x1E;
    internal const ushort SunShadowMapScaleRowIndex = 0x1F;
    internal const ushort ZNearRowIndex = 0x20;
    internal const float ZNearScale = 0.984375f;

    // Sampling transform for the 512x256x4 model-lighting program image.
    internal const ushort ModelLightingSamplerRowIndex = 0x21;
    internal const ushort StaticModelBaseLightingCoordsRowIndex = 0x39;
    // The static-model light-probe path uploads this row from the draw-instance
    // GroundLighting value (or its light-grid-derived runtime equivalent)
    // immediately before the Event22 draw.
    internal const ushort StaticModelLightProbeAmbientRowIndex = 0x3A;

    // R_UpdateViewport writes these from the active render-target extent and
    // viewport. PS3's direct-table indices differ from the correlated desktop
    // IW4 names, but the fastfile arguments establish rows 0x3E/0x3F.
    internal const ushort ClipSpaceLookupScaleRowIndex = 0x3E;
    internal const ushort ClipSpaceLookupOffsetRowIndex = 0x3F;

    internal const ushort FogRowIndex = 0x24;
    internal const ushort FogColorLinearRowIndex = 0x25;
    internal const ushort FogColorGammaRowIndex = 0x26;
    internal const ushort SunFogConstantsRowIndex = 0x27;
    internal const ushort SunFogColorLinearRowIndex = 0x28;
    internal const ushort SunFogColorGammaRowIndex = 0x29;
    internal const ushort SunFogDirectionRowIndex = 0x2A;

    private const float TwoPi = 6.283185482025146484375f;

    // Exact PS3 single-precision conversion literals.
    private static readonly float ByteToUnit = Float(0x3B808081);
    private static readonly float DegreesToRadians = Float(0x3C8EFA35);
    private static readonly float InvalidFadeRangeScale = Float(0x42C80000);

    private static readonly IReadOnlyList<
        MapRenderDirectCodeConstantRow> SourceInitializationRows =
    [
        // Row 0x05 uses scalar initialization even when the normal-view caller
        // supplies its input table.
        new(
            0x05,
            new MapRenderShaderConstantValue(
                float.MaxValue,
                float.MaxValue,
                float.MaxValue,
                0.0f)),

        // Normal-view row 0x23 has type-table value 2, so initialization
        // preserves the copied view-input zero instead of taking the scalar
        // sentinel branch.
        new(
            0x23,
            new MapRenderShaderConstantValue(
                0.0f,
                0.0f,
                0.0f,
                0.0f))
    ];

    private static readonly IReadOnlyList<
        MapRenderDirectCodeConstantRow> FogTemplateRows =
        Array.AsReadOnly(
            Enumerable.Range(
                    FogRowIndex,
                    SunFogDirectionRowIndex - FogRowIndex + 1)
                .Select(index => new
                    MapRenderDirectCodeConstantRow(
                        index,
                        new MapRenderShaderConstantValue(0f, 0f, 0f, 0f)))
                .ToArray());

    private static readonly IReadOnlyList<
        MapRenderDirectCodeConstantRow> DisabledFogRows =
        Array.AsReadOnly(
        [
            Row(FogRowIndex, new(0.0f, 1.0f, 0.0f, 0.0f))
        ]);

    internal static IReadOnlyList<MapRenderDirectCodeConstantRow>
        ProduceSourceInitializationRows() => SourceInitializationRows;

    internal static MapRenderDirectCodeConstantRow
        ProduceModelLightingSamplerRow() =>
        Row(
            ModelLightingSamplerRowIndex,
            new MapRenderShaderConstantValue(
                MapRenderStaticModelLightingAtlas.SamplerTransform.X,
                MapRenderStaticModelLightingAtlas.SamplerTransform.Y,
                MapRenderStaticModelLightingAtlas.SamplerTransform.Z,
                MapRenderStaticModelLightingAtlas.SamplerTransform.W));

    internal static MapRenderClipSpaceLookupCodeConstants
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

        return new MapRenderClipSpaceLookupCodeConstants(
            new MapRenderShaderConstantValue(
                scaleX,
                -scaleY,
                0.0f,
                1.0f),
            new MapRenderShaderConstantValue(
                offsetX,
                offsetY,
                0.0f,
                0.0f));
    }

    internal static MapRenderDirectCodeConstantRow ProduceZNear(
        float zNear)
    {
        if (!(zNear > 0.0f) || !float.IsFinite(zNear))
            throw new ArgumentOutOfRangeException(nameof(zNear));

        return Row(
            ZNearRowIndex,
            new MapRenderShaderConstantValue(
                zNear * ZNearScale,
                0.0f,
                0.0f,
                0.0f));
    }

    /// <summary>
    /// The directional-light writer copies the authored direction to row 0,
    /// multiplies the two live
    /// renderer color scales by the view's primary-light tweak strengths when
    /// requested, then applies the exact gamma-to-linear transfer independently
    /// to diffuse/specular rows 1 and 2. The direction in this operational plan
    /// has been adapted once into the viewer basis, matching viewer-adapted
    /// vertex inputs.
    /// </summary>
    internal static IReadOnlyList<MapRenderDirectCodeConstantRow>
        ProduceDirectionalSunRows(
            MapRenderEditorPreviewLightingPlan lighting,
            MapRenderEditorPreviewPrimaryLightVisionState? primaryLight = null,
            float diffuseColorScale =
                DefaultDiffuseColorScale,
            float specularColorScale =
                DefaultSpecularColorScale)
    {
        MapRenderDirectionalSunLinearColors colors =
            ProduceDirectionalSunLinearColors(
                lighting,
                primaryLight,
                diffuseColorScale,
                specularColorScale);
        System.Numerics.Vector3 direction =
            lighting.DirectionalSunCodeDirection;
        return
        [
            Row(
                DirectionalLightDirectionRowIndex,
                new(direction.X, direction.Y, direction.Z, 0.0f)),
            Row(
                DirectionalLightDiffuseRowIndex,
                new(
                    colors.Diffuse.X,
                    colors.Diffuse.Y,
                    colors.Diffuse.Z,
                    1.0f)),
            Row(
                DirectionalLightSpecularRowIndex,
                new(
                    colors.Specular.X,
                    colors.Specular.Y,
                    colors.Specular.Z,
                    1.0f))
        ];
    }

    internal static MapRenderDirectionalSunLinearColors
        ProduceDirectionalSunLinearColors(
            MapRenderEditorPreviewLightingPlan lighting,
            MapRenderEditorPreviewPrimaryLightVisionState? primaryLight = null,
            float diffuseColorScale =
                DefaultDiffuseColorScale,
            float specularColorScale =
                DefaultSpecularColorScale)
    {
        ArgumentNullException.ThrowIfNull(lighting);
        if (!lighting.HasDirectionalSun)
        {
            throw new ArgumentException(
                "Directional sun rows require one active editor sun.",
                nameof(lighting));
        }
        if (!float.IsFinite(diffuseColorScale) ||
            diffuseColorScale < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(diffuseColorScale));
        }
        if (!float.IsFinite(specularColorScale) ||
            specularColorScale < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(specularColorScale));
        }
        if (primaryLight is not null &&
            !primaryLight.HasFiniteNonnegativeStrengths)
        {
            throw new ArgumentException(
                "Primary-light tweak strengths must be finite and nonnegative.",
                nameof(primaryLight));
        }

        if (primaryLight?.UseTweaks == true)
        {
            diffuseColorScale *= primaryLight.DiffuseStrength;
            specularColorScale *= primaryLight.SpecularStrength;
        }

        System.Numerics.Vector3 color = lighting.DirectionalSunColor;
        System.Numerics.Vector3 diffuse =
            color * diffuseColorScale;
        System.Numerics.Vector3 specular =
            color * specularColorScale;
        return new MapRenderDirectionalSunLinearColors(
            new System.Numerics.Vector3(
                GammaColorTransfer.ToLinear(diffuse.X),
                GammaColorTransfer.ToLinear(diffuse.Y),
                GammaColorTransfer.ToLinear(diffuse.Z)),
            new System.Numerics.Vector3(
                GammaColorTransfer.ToLinear(specular.X),
                GammaColorTransfer.ToLinear(specular.Y),
                GammaColorTransfer.ToLinear(specular.Z)));
    }

    internal static MapRenderDirectCodeConstantRow ProduceGameTime(
        float gameTime)
    {
        if (!float.IsFinite(gameTime))
            throw new ArgumentOutOfRangeException(nameof(gameTime));

        float whole = MathF.Floor(gameTime);
        float fraction = gameTime - whole;
        float angle = fraction * TwoPi;
        return new(
            GameTimeRowIndex,
            new MapRenderShaderConstantValue(
                MathF.Sin(angle),
                MathF.Cos(angle),
                fraction,
                gameTime % GameTimeWrapSeconds));
    }

    internal static IReadOnlyList<MapRenderDirectCodeConstantRow>
        ProduceFogTemplateRows() => FogTemplateRows;

    internal static IReadOnlyList<MapRenderDirectCodeConstantRow>
        ProduceDisabledFogRows() => DisabledFogRows;

    internal static IReadOnlyList<MapRenderDirectCodeConstantRow>
        ProduceFogRows(
            bool fogRenderingEnabled,
            MapRenderActiveFogState activeFog)
    {
        ArgumentNullException.ThrowIfNull(activeFog);

        if (!fogRenderingEnabled)
            return ProduceDisabledFogRows();

        MapRenderShaderConstantValue fogGamma = UnpackBgra(activeFog.Color);
        MapRenderShaderConstantValue fogLinear = Linearize(fogGamma);
        var rows = new List<MapRenderDirectCodeConstantRow>
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

        MapRenderShaderConstantValue sunGamma = UnpackBgra(sunFog.Color);
        MapRenderShaderConstantValue sunLinear = Linearize(sunGamma);
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

    private static MapRenderShaderConstantValue UnpackBgra(
        MapRenderBgra8Color color) =>
        new(
            color.Red * ByteToUnit,
            color.Green * ByteToUnit,
            color.Blue * ByteToUnit,
            color.Alpha * ByteToUnit);

    private static MapRenderShaderConstantValue Linearize(
        MapRenderShaderConstantValue gamma) =>
        new(
            GammaColorTransfer.ToLinear(gamma.X),
            GammaColorTransfer.ToLinear(gamma.Y),
            GammaColorTransfer.ToLinear(gamma.Z),
            gamma.W);

    private static MapRenderShaderConstantValue NormalizeDirection(
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

    private static MapRenderDirectCodeConstantRow Row(
        ushort index,
        MapRenderShaderConstantValue value)
    {
        if (!IsFinite(value))
        {
            throw new InvalidOperationException(
                $"Active fog state produced a nonfinite direct code row 0x{index:X2}.");
        }

        return new(index, value);
    }

    private static IReadOnlyList<MapRenderDirectCodeConstantRow>
        ReadOnly(List<MapRenderDirectCodeConstantRow> rows) =>
        Array.AsReadOnly(rows.ToArray());

    private static bool IsFinite(MapRenderShaderConstantValue value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        float.IsFinite(value.W);

    private static float Float(int bits) =>
        BitConverter.Int32BitsToSingle(bits);
}
