using System.Numerics;
using IW4.Render.Scheduling.Lighting;
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
        "PS3_R_SET_DRAW_SURFS_SCENE_LIGHT_UNSHADOWED_MANAGED";

    internal static IReadOnlyList<DirectCodeConstantRow> ProduceRows(
        MapRenderWorldEvent20SceneLightFrameInput frame,
        int sceneLightIndex,
        Vector3 eyeOffset)
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
        if (light.Type == 1)
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
        ValidateUnshadowedNonDirectionalLight(
            frame,
            sceneLightIndex,
            light);

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

        // The all-clear branch writes row 0x04 only for type 2. Row 0x05 is
        // not written here and retains source initialization (FLT_MAX xyz,0).
        if (light.Type == 2)
        {
            float denominator =
                light.CosHalfFovInner - light.CosHalfFovOuter;
            if (denominator == 0f)
            {
                throw new InvalidOperationException(
                    $"Event20 spot light {sceneLightIndex} has equal inner and outer cone cosines; row 0x04 cannot contain finite factors.");
            }
            rows.Add(Row(
                sceneLightIndex,
                0x04,
                1f / denominator,
                -light.CosHalfFovOuter / denominator,
                light.Exponent,
                0f));
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
        if (light.Type == 1)
        {
            throw new InvalidOperationException(
                $"Event20 scene light {sceneLightIndex} is directional and does not own a dynamic eye-relative position row.");
        }
        ValidateUnshadowedNonDirectionalLight(
            frame,
            sceneLightIndex,
            light);
        return Row(
            sceneLightIndex,
            0x00,
            light.Origin.X - eyeOffset.X,
            light.Origin.Y - eyeOffset.Y,
            light.Origin.Z - eyeOffset.Z,
            1f / light.Radius);
    }

    private static void ValidateUnshadowedNonDirectionalLight(
        MapRenderWorldEvent20SceneLightFrameInput frame,
        int sceneLightIndex,
        MapRenderWorldEvent20SceneLight light)
    {
        if (frame.DynamicInput.ShadowAllocation.IsShadowMapAllocated(
                sceneLightIndex))
        {
            throw new InvalidOperationException(
                $"Event20 scene light {sceneLightIndex} selected the PS3 allocated non-sun 0x003A15F0 branch, whose shadow inputs are not operationally mapped.");
        }
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

        return new(index, new ShaderConstantValue(x, y, z, w));
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}
