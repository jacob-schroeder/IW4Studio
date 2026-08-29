using System.Numerics;
using IW4.Assets.Assets.ComWorld;
using IW4.Render.Execution;
using IW4.Render.Lighting;
using IW4.Render.Scheduling.Lighting;
using IW4.Render.Scheduling.Shadows;
using IW4.Render.Shaders;

namespace IW4.Render.SceneBuilding;

/// <summary>
/// PS3 Event20 scene-light rows. Omitted writes preserve persistent-table
/// semantics rather than supplying guessed zero values.
/// </summary>
internal static class
    MapRenderWorldEvent20SceneLightDirectCodeConstantProducer
{
    internal const string ManagedProducerIdentity =
        "PS3_R_SET_DRAW_SURFS_SCENE_LIGHT_MANAGED";

    internal static IReadOnlyList<DirectCodeConstantRow> ProduceRows(
        MapRenderWorldEvent20SceneLightFrameInput frame,
        int sceneLightIndex,
        Vector3 eyeOffset,
        MapRenderSpotShadowAtlasEntry? spotShadowEntry = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!IsFinite(eyeOffset))
            throw new ArgumentOutOfRangeException(nameof(eyeOffset));

        // Both native setters return without touching the table for index 0.
        if (sceneLightIndex == 0)
            return [];
        if ((uint)sceneLightIndex >= (uint)frame.SceneLightCount)
        {
            throw new InvalidOperationException(
                $"Event20 selected scene light {sceneLightIndex}, but the adapted runtime table contains {frame.SceneLightCount} rows.");
        }

        MapRenderWorldEvent20SceneLight light =
            frame.GetSceneLight(sceneLightIndex);
        if (light.Type == GfxLightType.Directional)
        {
            return
            [
                Row(
                    sceneLightIndex,
                    0x00,
                    light.Direction.X,
                    light.Direction.Y,
                    light.Direction.Z,
                    0f),
                ColorRow(
                    sceneLightIndex,
                    0x01,
                    light,
                    frame.DynamicInput.DiffuseColorScale),
                ColorRow(
                    sceneLightIndex,
                    0x02,
                    light,
                    frame.DynamicInput.SpecularColorScale)
            ];
        }
        ValidateNonDirectionalLight(sceneLightIndex, light);
        ValidateSpotShadowEntry(
            sceneLightIndex,
            light,
            spotShadowEntry);

        var rows = new List<DirectCodeConstantRow>(5)
        {
            ProducePositionRow(frame, sceneLightIndex, eyeOffset),
            ColorRow(
                sceneLightIndex,
                0x01,
                light,
                frame.DynamicInput.DiffuseColorScale),
            ColorRow(
                sceneLightIndex,
                0x02,
                light,
                frame.DynamicInput.SpecularColorScale),
            Row(
                sceneLightIndex,
                0x03,
                light.Direction.X,
                light.Direction.Y,
                light.Direction.Z,
                0f)
        };

        // Event20 always writes row 0x04 for spots. Allocation changes only
        // its fade component and whether row 0x05 replaces source
        // initialization; those two rows remain dynamic in translated plans.
        if (light.Type == GfxLightType.Spot)
        {
            rows.Add(ProduceSpotFactorsRow(
                frame,
                sceneLightIndex,
                spotShadowEntry));
            if (spotShadowEntry is not null)
            {
                rows.Add(ProduceLightFalloffPlacementRow(
                    frame,
                    sceneLightIndex,
                    spotShadowEntry));
            }
        }

        return Array.AsReadOnly(rows.ToArray());
    }

    /// <summary>
    /// Produces the only camera-dependent row without allocating a temporary
    /// Event20 row table. The renderer calls this once for each selected
    /// translated draw so camera motion cannot retain a load-time eye offset.
    /// </summary>
    internal static DirectCodeConstantRow ProducePositionRow(
        MapRenderWorldEvent20SceneLightFrameInput frame,
        int sceneLightIndex,
        Vector3 eyeOffset) =>
        new(0x00, ProducePositionValue(
            frame,
            sceneLightIndex,
            eyeOffset));

    internal static ShaderConstantValue ProducePositionValue(
        MapRenderWorldEvent20SceneLightFrameInput frame,
        int sceneLightIndex,
        Vector3 eyeOffset)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!IsFinite(eyeOffset))
            throw new ArgumentOutOfRangeException(nameof(eyeOffset));
        if (sceneLightIndex == 0 ||
            (uint)sceneLightIndex >= (uint)frame.SceneLightCount)
        {
            throw new InvalidOperationException(
                $"Event20 dynamic position selected scene light {sceneLightIndex}, but the adapted runtime table contains {frame.SceneLightCount} rows and row zero is the no-write sentinel.");
        }

        MapRenderWorldEvent20SceneLight light =
            frame.GetSceneLight(sceneLightIndex);
        if (light.Type == GfxLightType.Directional)
        {
            throw new InvalidOperationException(
                $"Event20 scene light {sceneLightIndex} is directional and does not own a dynamic eye-relative position row.");
        }
        ValidateNonDirectionalLight(sceneLightIndex, light);
        return Value(
            sceneLightIndex,
            0x00,
            light.Origin.X - eyeOffset.X,
            light.Origin.Y - eyeOffset.Y,
            light.Origin.Z - eyeOffset.Z,
            1f / light.Radius);
    }

    internal static DirectCodeConstantRow ProduceSpotFactorsRow(
        MapRenderWorldEvent20SceneLightFrameInput frame,
        int sceneLightIndex,
        MapRenderSpotShadowAtlasEntry? spotShadowEntry) =>
        new(
            FrameDirectCodeConstants.LightSpotFactorsRowIndex,
            ProduceSpotFactorsValue(
                frame,
                sceneLightIndex,
                spotShadowEntry));

    internal static ShaderConstantValue ProduceSpotFactorsValue(
        MapRenderWorldEvent20SceneLightFrameInput frame,
        int sceneLightIndex,
        MapRenderSpotShadowAtlasEntry? spotShadowEntry)
    {
        ArgumentNullException.ThrowIfNull(frame);
        MapRenderWorldEvent20SceneLight light =
            GetSpotLight(frame, sceneLightIndex);
        ValidateSpotShadowEntry(
            sceneLightIndex,
            light,
            spotShadowEntry);

        float denominator =
            light.CosHalfFovInner - light.CosHalfFovOuter;
        if (denominator == 0f || !float.IsFinite(denominator))
        {
            throw new InvalidOperationException(
                $"Event20 spot light {sceneLightIndex} has invalid inner and outer cone cosines; row 0x04 cannot contain finite factors.");
        }
        return Value(
            sceneLightIndex,
            FrameDirectCodeConstants.LightSpotFactorsRowIndex,
            1f / denominator,
            -light.CosHalfFovOuter / denominator,
            light.Exponent,
            spotShadowEntry?.Fade ?? 0f);
    }

    internal static DirectCodeConstantRow
        ProduceLightFalloffPlacementRow(
            MapRenderWorldEvent20SceneLightFrameInput frame,
            int sceneLightIndex,
            MapRenderSpotShadowAtlasEntry? spotShadowEntry) =>
        new(
            FrameDirectCodeConstants.LightFalloffPlacementRowIndex,
            ProduceLightFalloffPlacementValue(
                frame,
                sceneLightIndex,
                spotShadowEntry));

    internal static ShaderConstantValue
        ProduceLightFalloffPlacementValue(
            MapRenderWorldEvent20SceneLightFrameInput frame,
            int sceneLightIndex,
            MapRenderSpotShadowAtlasEntry? spotShadowEntry)
    {
        ArgumentNullException.ThrowIfNull(frame);
        MapRenderWorldEvent20SceneLight light =
            GetSpotLight(frame, sceneLightIndex);
        ValidateSpotShadowEntry(
            sceneLightIndex,
            light,
            spotShadowEntry);
        if (spotShadowEntry is null)
        {
            return FrameDirectCodeConstants
                .ProduceLightFalloffPlacementInitializationRow()
                .Value;
        }

        if (light.Definition is not { } definition ||
            light.AttenuationImageWidth is not { } imageWidth)
        {
            throw new InvalidOperationException(
                $"Event20 allocated spot light {sceneLightIndex} has no canonical non-empty LightDef image.");
        }
        return Value(
            sceneLightIndex,
            FrameDirectCodeConstants.LightFalloffPlacementRowIndex,
            imageWidth / (float)LightFalloffLookupLayout.Width,
            0f,
            definition.LmapLookupStart / (float)LightFalloffLookupLayout.Width,
            0f);
    }

    private static MapRenderWorldEvent20SceneLight GetSpotLight(
        MapRenderWorldEvent20SceneLightFrameInput frame,
        int sceneLightIndex)
    {
        if (sceneLightIndex == 0 ||
            (uint)sceneLightIndex >= (uint)frame.SceneLightCount)
        {
            throw new InvalidOperationException(
                $"Event20 spot rows selected scene light {sceneLightIndex}, but the adapted runtime table contains {frame.SceneLightCount} rows and row zero is the no-write sentinel.");
        }

        MapRenderWorldEvent20SceneLight light =
            frame.GetSceneLight(sceneLightIndex);
        if (light.Type != GfxLightType.Spot)
        {
            throw new InvalidOperationException(
                $"Event20 scene light {sceneLightIndex} is not a spot light and cannot own spot-shadow rows.");
        }
        ValidateNonDirectionalLight(sceneLightIndex, light);
        return light;
    }

    private static void ValidateNonDirectionalLight(
        int sceneLightIndex,
        MapRenderWorldEvent20SceneLight light)
    {
        if (light.Definition is null)
        {
            throw new InvalidOperationException(
                $"Event20 scene light {sceneLightIndex} has no canonical LightDef for '{light.DefinitionName ?? "<null>"}'.");
        }
        if (light.Radius == 0f)
        {
            throw new InvalidOperationException(
                $"Event20 scene light {sceneLightIndex} has zero radius; row 0x00 cannot contain a finite reciprocal.");
        }
    }

    private static void ValidateSpotShadowEntry(
        int sceneLightIndex,
        MapRenderWorldEvent20SceneLight light,
        MapRenderSpotShadowAtlasEntry? spotShadowEntry)
    {
        if (spotShadowEntry is null)
            return;
        if (light.Type != GfxLightType.Spot ||
            spotShadowEntry.SceneLightIndex != sceneLightIndex)
        {
            throw new InvalidOperationException(
                $"Event20 scene light {sceneLightIndex} cannot consume spot-shadow entry {spotShadowEntry.SceneLightIndex}.");
        }
    }

    private static DirectCodeConstantRow ColorRow(
        int sceneLightIndex,
        int rowIndex,
        MapRenderWorldEvent20SceneLight light,
        float colorScale) => Row(
            sceneLightIndex,
            rowIndex,
            GammaColorTransfer.ToLinear(light.Color.X * colorScale),
            GammaColorTransfer.ToLinear(light.Color.Y * colorScale),
            GammaColorTransfer.ToLinear(light.Color.Z * colorScale),
            1f);

    private static DirectCodeConstantRow Row(
        int sceneLightIndex,
        int index,
        float x,
        float y,
        float z,
        float w) =>
        new(
            index,
            Value(sceneLightIndex, index, x, y, z, w));

    private static ShaderConstantValue Value(
        int sceneLightIndex,
        int index,
        float x,
        float y,
        float z,
        float w)
    {
        if (!float.IsFinite(x) ||
            !float.IsFinite(y) ||
            !float.IsFinite(z) ||
            !float.IsFinite(w))
        {
            throw new InvalidOperationException(
                $"Event20 scene light {sceneLightIndex} produced a non-finite direct constant for row 0x{index:X2}.");
        }

        return new ShaderConstantValue(x, y, z, w);
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}
