using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.Shaders;

namespace IW4.Render.Execution;

internal static class TranslatedProgramDirectCodeConstantRows
{
    internal static IReadOnlyList<string> FindBlockers(
        IReadOnlyList<ShaderConstantDestination> constantDestinations,
        IReadOnlyList<CodePixelConstantPatchPlan> codePixelConstantPatchPlans)
    {
        ArgumentNullException.ThrowIfNull(constantDestinations);
        ArgumentNullException.ThrowIfNull(codePixelConstantPatchPlans);
        var blockers = new SortedSet<string>(StringComparer.Ordinal);
        foreach (ShaderConstantDestination constant in constantDestinations.Where(constant =>
            constant.ArgumentType.EndsWith("VertexConst", StringComparison.Ordinal) &&
            constant.CodeConstantSourceRow.HasValue))
        {
            ushort row = constant.CodeConstantSourceRow!.Value;
            if (!IsSupportedSourceRow(row))
                blockers.Add($"codeConstantRow0x{row:X2}=EDITOR_DIRECT_CODE_ROW_UNSUPPORTED");
        }
        foreach (CodePixelConstantPatchPlan patchPlan in codePixelConstantPatchPlans)
        {
            if (!patchPlan.IsDirectSourceResolved)
            {
                blockers.Add($"codePixelArg{patchPlan.ArgumentOrdinal}=EDITOR_PATCH_{patchPlan.Status.ToString().ToUpperInvariant()}");
            }
            else if (!IsSupportedSourceRow(patchPlan.CodeIndex))
            {
                blockers.Add($"codeConstantRow0x{patchPlan.CodeIndex:X2}=EDITOR_DIRECT_CODE_ROW_UNSUPPORTED");
            }
            else if (IsPerInstanceStaticModelSourceRow(patchPlan.CodeIndex))
            {
                blockers.Add($"codeConstantRow0x{patchPlan.CodeIndex:X2}=EDITOR_PER_INSTANCE_VERTEX_ROW_USED_BY_PIXEL_PROGRAM");
            }
        }
        return Array.AsReadOnly(blockers.ToArray());
    }
    internal static bool IsSupportedSourceRow(ushort sourceRow) =>
        IsSceneLightSourceRow(sourceRow) || IsSunShadowProjectionSourceRow(sourceRow) ||
        sourceRow == FrameDirectCodeConstants.GameTimeRowIndex ||
        sourceRow == FrameDirectCodeConstants.ModelLightingSamplerRowIndex ||
        IsStaticModelBaseLightingCoordsSourceRow(sourceRow) || IsStaticModelLightProbeAmbientSourceRow(sourceRow) ||
        IsClipSpaceLookupSourceRow(sourceRow) || IsZNearSourceRow(sourceRow) ||
        sourceRow == FrameDirectCodeConstants.MaterialColorRowIndex || IsFogSourceRow(sourceRow);
    internal static bool IsSunShadowProjectionSourceRow(ushort sourceRow) => sourceRow is FrameDirectCodeConstants.SunShadowSwitchPartitionRowIndex or FrameDirectCodeConstants.SunShadowMapScaleRowIndex;
    internal static bool IsStaticModelBaseLightingCoordsSourceRow(ushort sourceRow) => sourceRow == FrameDirectCodeConstants.StaticModelBaseLightingCoordsRowIndex;
    internal static bool IsStaticModelLightProbeAmbientSourceRow(ushort sourceRow) => sourceRow == FrameDirectCodeConstants.StaticModelLightProbeAmbientRowIndex;
    internal static bool IsClipSpaceLookupSourceRow(ushort sourceRow) => sourceRow is FrameDirectCodeConstants.ClipSpaceLookupScaleRowIndex or FrameDirectCodeConstants.ClipSpaceLookupOffsetRowIndex;
    internal static bool IsZNearSourceRow(ushort sourceRow) => sourceRow == FrameDirectCodeConstants.ZNearRowIndex;
    internal static bool IsDynamicSceneLightSourceRow(ushort sourceRow) =>
        sourceRow == FrameDirectCodeConstants.DirectionalLightDirectionRowIndex ||
        sourceRow is FrameDirectCodeConstants.LightSpotFactorsRowIndex or
            FrameDirectCodeConstants.LightFalloffPlacementRowIndex;
    internal static bool IsPerInstanceStaticModelSourceRow(ushort sourceRow) => IsStaticModelBaseLightingCoordsSourceRow(sourceRow) || IsStaticModelLightProbeAmbientSourceRow(sourceRow);
    internal static bool IsRuntimeOwnedSourceRow(ushort sourceRow) => IsDynamicSceneLightSourceRow(sourceRow) || IsSunShadowProjectionSourceRow(sourceRow) || IsPerInstanceStaticModelSourceRow(sourceRow) || IsClipSpaceLookupSourceRow(sourceRow) || IsZNearSourceRow(sourceRow);
    internal static bool IsSceneLightSourceRow(ushort sourceRow) =>
        sourceRow is >= (ushort)MaterialConstantSource.LightPosition and
            <= (ushort)MaterialConstantSource.LightFalloffPlacement;
    private static bool IsFogSourceRow(ushort sourceRow) => sourceRow is >= FrameDirectCodeConstants.FogRowIndex and <= FrameDirectCodeConstants.SunFogDirectionRowIndex;
}
