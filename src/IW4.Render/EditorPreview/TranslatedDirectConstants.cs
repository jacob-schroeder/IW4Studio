using IW4.Render.Execution;
using IW4.Render.Lighting;
using IW4.Render.Execution.Fog;
using IW4.Render.Scheduling.Lighting;
using IW4.Render.SceneBuilding;
using IW4.Render.Shaders;

namespace IW4.Render.EditorPreview;

/// <summary>
/// Resolves the direct CodeVertex and actually-used CodePixel rows that an
/// EditorPreview translated program may consume from the selected fog-enable
/// branch and, when enabled, one explicit active fog state. The source
/// source-template rows are initialized first, then the selected R_SetFrameFog
/// branch writes are overlaid in native order.
/// </summary>
public static class
    TranslatedProgramDirectCodeConstantPlanner
{
    public const ushort FogRowIndex =
        FrameDirectCodeConstants.FogRowIndex;

    public const ushort SunFogDirectionRowIndex =
        FrameDirectCodeConstants.SunFogDirectionRowIndex;

    public const ushort SunShadowSwitchPartitionRowIndex =
        FrameDirectCodeConstants.SunShadowSwitchPartitionRowIndex;

    public const ushort SunShadowMapScaleRowIndex =
        FrameDirectCodeConstants.SunShadowMapScaleRowIndex;

    public const string ProducerIdentity =
        "EDITOR_PREVIEW_PS3_SOURCE_INITIALIZATION_GAME_TIME_THEN_FOG_TEMPLATE_THEN_ACTIVE_R_SET_FRAME_FOG_AND_DIRECTIONAL_SCENE_LIGHT";

    public const string DisabledProducerIdentity =
        "EDITOR_PREVIEW_PS3_SOURCE_INITIALIZATION_GAME_TIME_THEN_FOG_TEMPLATE_THEN_DISABLED_R_SET_FRAME_FOG_AND_DIRECTIONAL_SCENE_LIGHT";

    public const string ActiveFogMissingBlocker =
        "editorActiveFog=EDITOR_ACTIVE_FOG_STATE_MISSING";

    public const string DirectionalSunMissingBlocker =
        "editorDirectionalSun=EDITOR_DIRECTIONAL_SCENE_LIGHT_STATE_MISSING";

    public const string SceneLightFrameMissingBlocker =
        "editorSceneLights=EDITOR_EVENT20_SCENE_LIGHT_FRAME_MISSING";

    public const string SceneLightSentinelBlocker =
        "editorSceneLightIndex=EDITOR_EVENT20_SENTINEL_DOES_NOT_WRITE_REQUIRED_ROWS";

    public static IReadOnlyList<string> FindBlockers(
        IReadOnlyList<ShaderConstantDestination>
            constantDestinations,
        IReadOnlyList<CodePixelConstantPatchPlan>
            codePixelConstantPatchPlans)
    {
        ArgumentNullException.ThrowIfNull(constantDestinations);
        ArgumentNullException.ThrowIfNull(codePixelConstantPatchPlans);

        CollectRequirements(
            constantDestinations,
            codePixelConstantPatchPlans,
            out _,
            out SortedSet<string> blockers);
        return Array.AsReadOnly(blockers.ToArray());
    }

    public static
        TranslatedProgramDirectCodeConstantPlanBuildResult
        TryPlan(
            IReadOnlyList<ShaderConstantDestination>
                constantDestinations,
            IReadOnlyList<CodePixelConstantPatchPlan>
                codePixelConstantPatchPlans,
            MapRenderActiveFogState? activeFog) =>
        TryPlan(
            constantDestinations,
            codePixelConstantPatchPlans,
            fogRenderingEnabled: true,
            activeFog: activeFog,
            directionalSun: null);

    public static
        TranslatedProgramDirectCodeConstantPlanBuildResult
        TryPlan(
            IReadOnlyList<ShaderConstantDestination>
                constantDestinations,
            IReadOnlyList<CodePixelConstantPatchPlan>
                codePixelConstantPatchPlans,
            bool fogRenderingEnabled,
            MapRenderActiveFogState? activeFog) =>
        TryPlan(
            constantDestinations,
            codePixelConstantPatchPlans,
            fogRenderingEnabled,
            activeFog,
            directionalSun: null);

    public static
        TranslatedProgramDirectCodeConstantPlanBuildResult
        TryPlan(
            IReadOnlyList<ShaderConstantDestination>
                constantDestinations,
            IReadOnlyList<CodePixelConstantPatchPlan>
                codePixelConstantPatchPlans,
            bool fogRenderingEnabled,
            MapRenderActiveFogState? activeFog,
            MapRenderEditorPreviewLightingPlan? directionalSun,
            MapRenderEditorPreviewPrimaryLightVisionState? primaryLight = null,
            int? sceneLightIndex = null,
            MapRenderWorldEvent20SceneLightFrameInput? sceneLightFrame = null)
    {
        ArgumentNullException.ThrowIfNull(constantDestinations);
        ArgumentNullException.ThrowIfNull(codePixelConstantPatchPlans);
        string producerIdentity = fogRenderingEnabled
            ? ProducerIdentity
            : DisabledProducerIdentity;

        CollectRequirements(
            constantDestinations,
            codePixelConstantPatchPlans,
            out SortedSet<ushort> requiredRows,
            out SortedSet<string> blockers);
        if (requiredRows.Count == 0)
        {
            return blockers.Count == 0
                ? new(
                    new TranslatedProgramDirectCodeConstantPlan(
                        producerIdentity,
                        []),
                    [])
                : new(null, Array.AsReadOnly(blockers.ToArray()));
        }

        bool requiresFog = requiredRows.Any(IsFogSourceRow);
        if (requiresFog && fogRenderingEnabled && activeFog is null)
        {
            blockers.Add(ActiveFogMissingBlocker);
            return new(null, Array.AsReadOnly(blockers.ToArray()));
        }

        bool requiresSceneLight = requiredRows.Any(IsSceneLightSourceRow);
        MapRenderWorldEvent20SceneLight? selectedSceneLight = null;
        if (requiresSceneLight && sceneLightIndex.HasValue)
        {
            int index = sceneLightIndex.Value;
            if (index == 0)
            {
                if (requiredRows.Any(row => row != 0x05))
                {
                    blockers.Add(SceneLightSentinelBlocker);
                    return new(
                        null,
                        Array.AsReadOnly(blockers.ToArray()));
                }
            }
            else if (sceneLightFrame is null)
            {
                blockers.Add(SceneLightFrameMissingBlocker);
                return new(null, Array.AsReadOnly(blockers.ToArray()));
            }
            else if ((uint)index >=
                     (uint)sceneLightFrame.SceneLightCount)
            {
                blockers.Add(
                    $"editorSceneLightIndex{index}=" +
                    "EDITOR_EVENT20_SCENE_LIGHT_INDEX_OUT_OF_RANGE");
                return new(null, Array.AsReadOnly(blockers.ToArray()));
            }
            else
            {
                selectedSceneLight = sceneLightFrame.GetSceneLight(index);
            }
        }
        bool useNonDirectionalSceneLight = selectedSceneLight is not null &&
            selectedSceneLight.Type !=
                MapRenderEditorPreviewLightingPlanner.DirectionalLightType;

        bool requiresDirectionalSun = requiresSceneLight &&
            selectedSceneLight is null &&
            sceneLightIndex != 0;
        if (requiresDirectionalSun &&
            directionalSun?.HasDirectionalSun != true)
        {
            blockers.Add(DirectionalSunMissingBlocker);
            return new(null, Array.AsReadOnly(blockers.ToArray()));
        }

        Dictionary<ushort, DirectCodeConstantRow>
            availableRows =
                FrameDirectCodeConstants
                    .ProduceSourceInitializationRows()
                .Append(
                    FrameDirectCodeConstants.ProduceGameTime(0f))
                .Append(
                    ModelLightingAtlasDirectCodeConstantProducer
                        .ProduceSamplerRow())
                .Concat(
                    FrameDirectCodeConstants
                        .ProduceFogTemplateRows())
                .ToDictionary(row => checked((ushort)row.SourceRowIndex));
        if (selectedSceneLight is not null)
        {
            int selectedSceneLightIndex = sceneLightIndex!.Value;
            IReadOnlyList<DirectCodeConstantRow> sceneRows;
            try
            {
                sceneRows =
                    MapRenderWorldEvent20SceneLightDirectCodeConstantProducer
                        .ProduceRows(
                            sceneLightFrame!,
                            selectedSceneLightIndex,
                            sceneLightFrame!.EyeOffset);
            }
            catch (InvalidOperationException exception)
            {
                blockers.Add(
                    $"editorSceneLightIndex{selectedSceneLightIndex}=" +
                    $"EDITOR_EVENT20_SCENE_LIGHT_VALUE_INVALID:{exception.Message}");
                return new(null, Array.AsReadOnly(blockers.ToArray()));
            }
            foreach (DirectCodeConstantRow sceneRow in sceneRows)
            {
                availableRows[checked((ushort)sceneRow.SourceRowIndex)] =
                    sceneRow;
            }
        }
        else if (requiresDirectionalSun)
        {
            foreach (DirectCodeConstantRow sunRow in
                    MapRenderEditorDirectCodeConstantProducers.ProduceDirectionalSunRows(
                         directionalSun!,
                         primaryLight))
            {
                availableRows[checked((ushort)sunRow.SourceRowIndex)] =
                    sunRow;
            }
        }
        if (requiresFog)
        {
            IReadOnlyList<DirectCodeConstantRow>
                fogRows = fogRenderingEnabled
                    ? FrameDirectCodeConstants.ProduceFogRows(
                        fogRenderingEnabled: true,
                        activeFog!)
                    : FrameDirectCodeConstants
                        .ProduceDisabledFogRows();
            foreach (DirectCodeConstantRow fogRow in
                     fogRows)
            {
                availableRows[checked((ushort)fogRow.SourceRowIndex)] =
                    fogRow;
            }
        }

        var resolvedRows = new List<
            DirectCodeConstantRow>(requiredRows.Count);
        foreach (ushort requiredRow in requiredRows)
        {
            // Rows 0x1E/0x1F are owned by the current sun-shadow projection.
            // Do not manufacture a static zero row: the resulting plan stays
            // structurally valid while execution remains dependent on an
            // exact, same-revision runtime projection payload.
            if (IsSunShadowProjectionSourceRow(requiredRow))
            {
                continue;
            }
            if (IsPerInstanceStaticModelSourceRow(requiredRow))
            {
                continue;
            }
            if (IsClipSpaceLookupSourceRow(requiredRow))
            {
                continue;
            }
            if (IsZNearSourceRow(requiredRow))
            {
                continue;
            }
            if (useNonDirectionalSceneLight && requiredRow ==
                FrameDirectCodeConstants.DirectionalLightDirectionRowIndex)
            {
                // Position is eye-relative and follows the current camera.
                continue;
            }

            if (!availableRows.TryGetValue(
                    requiredRow,
                    out DirectCodeConstantRow? row))
            {
                blockers.Add(
                    $"codeConstantRow0x{requiredRow:X2}=" +
                    "EDITOR_DIRECT_CODE_ROW_VALUE_UNAVAILABLE");
                continue;
            }

            resolvedRows.Add(row);
        }

        if (blockers.Count != 0)
        {
            return new(
                null,
                Array.AsReadOnly(blockers.ToArray()));
        }

        var dynamicRows = new HashSet<ushort>();
        if (requiredRows.Contains(
                FrameDirectCodeConstants.GameTimeRowIndex))
        {
            dynamicRows.Add(
                FrameDirectCodeConstants.GameTimeRowIndex);
        }
        foreach (ushort projectionRow in requiredRows.Where(
                     IsSunShadowProjectionSourceRow))
        {
            dynamicRows.Add(projectionRow);
        }
        foreach (ushort viewportRow in requiredRows.Where(
                     IsClipSpaceLookupSourceRow))
        {
            dynamicRows.Add(viewportRow);
        }
        if (requiredRows.Contains(
                FrameDirectCodeConstants.ZNearRowIndex))
        {
            dynamicRows.Add(
                FrameDirectCodeConstants.ZNearRowIndex);
        }
        if (requiredRows.Contains(
                FrameDirectCodeConstants
                    .StaticModelBaseLightingCoordsRowIndex))
        {
            dynamicRows.Add(
                FrameDirectCodeConstants
                    .StaticModelBaseLightingCoordsRowIndex);
        }
        if (requiredRows.Contains(
                FrameDirectCodeConstants
                    .StaticModelLightProbeAmbientRowIndex))
        {
            dynamicRows.Add(
                FrameDirectCodeConstants
                    .StaticModelLightProbeAmbientRowIndex);
        }
        if (useNonDirectionalSceneLight && requiredRows.Contains(
                FrameDirectCodeConstants
                    .DirectionalLightDirectionRowIndex))
        {
            dynamicRows.Add(
                FrameDirectCodeConstants
                    .DirectionalLightDirectionRowIndex);
        }

        return new(
            new TranslatedProgramDirectCodeConstantPlan(
                producerIdentity,
                resolvedRows,
                dynamicRows.Count == 0 ? null : dynamicRows,
                useNonDirectionalSceneLight
                    ? sceneLightIndex
                    : null),
            []);
    }

    public static bool IsSupportedSourceRow(ushort sourceRow) =>
        IsSceneLightSourceRow(sourceRow) ||
        IsSunShadowProjectionSourceRow(sourceRow) ||
        sourceRow ==
            FrameDirectCodeConstants
                .GameTimeRowIndex ||
        sourceRow ==
            FrameDirectCodeConstants.ModelLightingSamplerRowIndex ||
        IsStaticModelBaseLightingCoordsSourceRow(sourceRow) ||
        IsStaticModelLightProbeAmbientSourceRow(sourceRow) ||
        IsClipSpaceLookupSourceRow(sourceRow) ||
        IsZNearSourceRow(sourceRow) ||
        sourceRow == 0x23 ||
        IsFogSourceRow(sourceRow);

    public static bool IsSunShadowProjectionSourceRow(ushort sourceRow) =>
        sourceRow is SunShadowSwitchPartitionRowIndex or
            SunShadowMapScaleRowIndex;

    public static bool IsStaticModelBaseLightingCoordsSourceRow(
        ushort sourceRow) =>
        sourceRow == FrameDirectCodeConstants
            .StaticModelBaseLightingCoordsRowIndex;

    public static bool IsStaticModelLightProbeAmbientSourceRow(
        ushort sourceRow) =>
        sourceRow == FrameDirectCodeConstants
            .StaticModelLightProbeAmbientRowIndex;

    public static bool IsClipSpaceLookupSourceRow(ushort sourceRow) =>
        sourceRow is
            FrameDirectCodeConstants.ClipSpaceLookupScaleRowIndex or
            FrameDirectCodeConstants.ClipSpaceLookupOffsetRowIndex;

    public static bool IsZNearSourceRow(ushort sourceRow) =>
        sourceRow == FrameDirectCodeConstants.ZNearRowIndex;

    public static bool IsPerInstanceStaticModelSourceRow(
        ushort sourceRow) =>
        IsStaticModelBaseLightingCoordsSourceRow(sourceRow) ||
        IsStaticModelLightProbeAmbientSourceRow(sourceRow);

    public static bool IsRuntimeOwnedSourceRow(ushort sourceRow) =>
        sourceRow ==
            FrameDirectCodeConstants.DirectionalLightDirectionRowIndex ||
        IsSunShadowProjectionSourceRow(sourceRow) ||
        IsPerInstanceStaticModelSourceRow(sourceRow) ||
        IsClipSpaceLookupSourceRow(sourceRow) ||
        IsZNearSourceRow(sourceRow);

    public static bool IsSceneLightSourceRow(ushort sourceRow) =>
        sourceRow is >= 0x00 and <= 0x05;

    private static bool IsFogSourceRow(ushort sourceRow) =>
        sourceRow is >=
            FrameDirectCodeConstants.FogRowIndex and <=
            FrameDirectCodeConstants
                .SunFogDirectionRowIndex;

    private static void CollectRequirements(
        IReadOnlyList<ShaderConstantDestination>
            constantDestinations,
        IReadOnlyList<CodePixelConstantPatchPlan>
            codePixelConstantPatchPlans,
        out SortedSet<ushort> requiredRows,
        out SortedSet<string> blockers)
    {
        blockers = new SortedSet<string>(StringComparer.Ordinal);
        requiredRows = new SortedSet<ushort>();

        foreach (ShaderConstantDestination constant in
                 constantDestinations.Where(constant =>
                     constant.ArgumentType.EndsWith(
                         "VertexConst",
                         StringComparison.Ordinal) &&
                     constant.CodeConstantSourceRow.HasValue))
        {
            ushort sourceRow = constant.CodeConstantSourceRow!.Value;
            if (!IsSupportedSourceRow(sourceRow))
            {
                blockers.Add(
                    $"codeConstantRow0x{sourceRow:X2}=" +
                    "EDITOR_DIRECT_CODE_ROW_UNSUPPORTED");
                continue;
            }

            requiredRows.Add(sourceRow);
        }

        foreach (CodePixelConstantPatchPlan patchPlan in
                 codePixelConstantPatchPlans)
        {
            if (!patchPlan.IsDirectSourceResolved)
            {
                blockers.Add(
                    $"codePixelArg{patchPlan.ArgumentOrdinal}=" +
                    $"EDITOR_PATCH_{patchPlan.Status.ToString().ToUpperInvariant()}");
                continue;
            }
            if (!IsSupportedSourceRow(patchPlan.CodeIndex))
            {
                blockers.Add(
                    $"codeConstantRow0x{patchPlan.CodeIndex:X2}=" +
                    "EDITOR_DIRECT_CODE_ROW_UNSUPPORTED");
                continue;
            }
            if (IsPerInstanceStaticModelSourceRow(
                    patchPlan.CodeIndex))
            {
                blockers.Add(
                    $"codeConstantRow0x{patchPlan.CodeIndex:X2}=" +
                    "EDITOR_PER_INSTANCE_VERTEX_ROW_USED_BY_PIXEL_PROGRAM");
                continue;
            }

            requiredRows.Add(patchPlan.CodeIndex);
        }
    }
}
