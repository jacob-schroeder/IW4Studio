using IW4.Render.Execution;
using IW4.Render.Lighting;
using IW4.Render.Scheduling.Fog;
using IW4.Render.Scheduling.Lighting;
using IW4.Render.SceneBuilding;
using IW4.Render.Shaders;

namespace IW4.Render.EditorPreview;

/// <summary>
/// Immutable active direct-table ownership authorized for one translated
/// EditorPreview program. <see cref="Rows"/> contains values that are safe to
/// materialize immediately; projection-owned dynamic rows appear only in
/// <see cref="DynamicSourceRows"/> until their same-revision runtime producer
/// publishes them. Missing rows remain unavailable instead of becoming
/// implicit zero backend bindings.
/// </summary>
public sealed class MapRenderEditorTranslatedProgramDirectCodeConstantPlan
{
    private readonly Dictionary<ushort,
        MapRenderDirectCodeConstantRow> _rows;

    internal MapRenderEditorTranslatedProgramDirectCodeConstantPlan(
        string producerIdentity,
        IReadOnlyList<MapRenderDirectCodeConstantRow> rows,
        IReadOnlySet<ushort>? dynamicSourceRows = null,
        int? sceneLightIndex = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(producerIdentity);
        ArgumentNullException.ThrowIfNull(rows);

        MapRenderDirectCodeConstantRow[] copied = rows
            .Select(row => row is null
                ? throw new ArgumentException(
                    "Editor direct-code plans cannot contain null rows.",
                    nameof(rows))
                : new MapRenderDirectCodeConstantRow(
                    row.SourceRowIndex,
                    row.Value))
            .ToArray();
        if (copied.Any(row => !IsFinite(row.Value)))
        {
            throw new ArgumentException(
                "Editor direct-code plans cannot contain non-finite values.",
                nameof(rows));
        }

        _rows = new(copied.Length);
        foreach (MapRenderDirectCodeConstantRow row in copied)
        {
            ushort sourceRow = checked((ushort)row.SourceRowIndex);
            if (!_rows.TryAdd(sourceRow, row))
            {
                throw new ArgumentException(
                    $"Editor direct-code row 0x{sourceRow:X2} is duplicated.",
                    nameof(rows));
            }
        }

        ushort[] dynamicRows = (dynamicSourceRows ??
                new HashSet<ushort>())
            .Distinct()
            .Order()
            .ToArray();
        if (dynamicRows.Any(row =>
                !_rows.ContainsKey(row) &&
                !MapRenderEditorTranslatedProgramDirectCodeConstantPlanner
                    .IsRuntimeOwnedSourceRow(row)))
        {
            throw new ArgumentException(
                "Dynamic direct-code rows without placeholder values must have a supported runtime owner.",
                nameof(dynamicSourceRows));
        }
        if (sceneLightIndex is < 1 or > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(sceneLightIndex));
        if (dynamicRows.Contains(
                FrameDirectCodeConstants.DirectionalLightDirectionRowIndex) &&
            !sceneLightIndex.HasValue)
        {
            throw new ArgumentException(
                "A dynamic scene-light position row requires exact invocation light identity.",
                nameof(sceneLightIndex));
        }

        ProducerIdentity = producerIdentity;
        Rows = Array.AsReadOnly(copied);
        DynamicSourceRows = Array.AsReadOnly(dynamicRows);
        SceneLightIndex = sceneLightIndex;
    }

    public string ProducerIdentity { get; }

    public IReadOnlyList<MapRenderDirectCodeConstantRow> Rows
        { get; }

    public IReadOnlyList<ushort> DynamicSourceRows { get; }

    /// <summary>
    /// Exact Event20 invocation-owned light index for a dynamic non-directional
    /// row 0x00. Null means this plan has no such dynamic row.
    /// </summary>
    public int? SceneLightIndex { get; }

    public bool IsDynamicSourceRow(ushort sourceRowIndex) =>
        DynamicSourceRows.Contains(sourceRowIndex);

    public bool TryGetRow(
        ushort sourceRowIndex,
        out MapRenderDirectCodeConstantRow? row) =>
        _rows.TryGetValue(sourceRowIndex, out row);

    private static bool IsFinite(MapRenderShaderConstantValue value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        float.IsFinite(value.W);
}

public sealed record
    MapRenderEditorTranslatedProgramDirectCodeConstantPlanBuildResult(
        MapRenderEditorTranslatedProgramDirectCodeConstantPlan? Plan,
        IReadOnlyList<string> Blockers)
{
    public bool IsReady => Plan is not null && Blockers.Count == 0;
}

/// <summary>
/// Resolves the direct CodeVertex and actually-used CodePixel rows that an
/// EditorPreview translated program may consume from the selected fog-enable
/// branch and, when enabled, one explicit active fog state. The source
/// source-template rows are initialized first, then the selected R_SetFrameFog
/// branch writes are overlaid in native order.
/// </summary>
public static class
    MapRenderEditorTranslatedProgramDirectCodeConstantPlanner
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
        IReadOnlyList<MapRenderShaderSamplerDestination>
            constantDestinations,
        IReadOnlyList<MapRenderCodePixelConstantPatchPlan>
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
        MapRenderEditorTranslatedProgramDirectCodeConstantPlanBuildResult
        TryPlan(
            IReadOnlyList<MapRenderShaderSamplerDestination>
                constantDestinations,
            IReadOnlyList<MapRenderCodePixelConstantPatchPlan>
                codePixelConstantPatchPlans,
            MapRenderActiveFogState? activeFog) =>
        TryPlan(
            constantDestinations,
            codePixelConstantPatchPlans,
            fogRenderingEnabled: true,
            activeFog: activeFog,
            directionalSun: null);

    public static
        MapRenderEditorTranslatedProgramDirectCodeConstantPlanBuildResult
        TryPlan(
            IReadOnlyList<MapRenderShaderSamplerDestination>
                constantDestinations,
            IReadOnlyList<MapRenderCodePixelConstantPatchPlan>
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
        MapRenderEditorTranslatedProgramDirectCodeConstantPlanBuildResult
        TryPlan(
            IReadOnlyList<MapRenderShaderSamplerDestination>
                constantDestinations,
            IReadOnlyList<MapRenderCodePixelConstantPatchPlan>
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
                    new MapRenderEditorTranslatedProgramDirectCodeConstantPlan(
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

        Dictionary<ushort, MapRenderDirectCodeConstantRow>
            availableRows =
                FrameDirectCodeConstants
                    .ProduceSourceInitializationRows()
                .Append(
                    FrameDirectCodeConstants.ProduceGameTime(0f))
                .Append(
                    FrameDirectCodeConstants
                        .ProduceModelLightingSamplerRow())
                .Concat(
                    FrameDirectCodeConstants
                        .ProduceFogTemplateRows())
                .ToDictionary(row => checked((ushort)row.SourceRowIndex));
        if (selectedSceneLight is not null)
        {
            int selectedSceneLightIndex = sceneLightIndex!.Value;
            IReadOnlyList<MapRenderDirectCodeConstantRow> sceneRows;
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
            foreach (MapRenderDirectCodeConstantRow sceneRow in sceneRows)
            {
                availableRows[checked((ushort)sceneRow.SourceRowIndex)] =
                    sceneRow;
            }
        }
        else if (requiresDirectionalSun)
        {
            foreach (MapRenderDirectCodeConstantRow sunRow in
                     FrameDirectCodeConstants.ProduceDirectionalSunRows(
                         directionalSun!,
                         primaryLight))
            {
                availableRows[checked((ushort)sunRow.SourceRowIndex)] =
                    sunRow;
            }
        }
        if (requiresFog)
        {
            IReadOnlyList<MapRenderDirectCodeConstantRow>
                fogRows = fogRenderingEnabled
                    ? FrameDirectCodeConstants.ProduceFogRows(
                        fogRenderingEnabled: true,
                        activeFog!)
                    : FrameDirectCodeConstants
                        .ProduceDisabledFogRows();
            foreach (MapRenderDirectCodeConstantRow fogRow in
                     fogRows)
            {
                availableRows[checked((ushort)fogRow.SourceRowIndex)] =
                    fogRow;
            }
        }

        var resolvedRows = new List<
            MapRenderDirectCodeConstantRow>(requiredRows.Count);
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
                    out MapRenderDirectCodeConstantRow? row))
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
            new MapRenderEditorTranslatedProgramDirectCodeConstantPlan(
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
        IReadOnlyList<MapRenderShaderSamplerDestination>
            constantDestinations,
        IReadOnlyList<MapRenderCodePixelConstantPatchPlan>
            codePixelConstantPatchPlans,
        out SortedSet<ushort> requiredRows,
        out SortedSet<string> blockers)
    {
        blockers = new SortedSet<string>(StringComparer.Ordinal);
        requiredRows = new SortedSet<ushort>();

        foreach (MapRenderShaderSamplerDestination constant in
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

        foreach (MapRenderCodePixelConstantPatchPlan patchPlan in
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
