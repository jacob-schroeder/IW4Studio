using IW4.Render.Execution;
using IW4.Render.Materials;

namespace IW4.Render.OpenGl.Programs;

/// <summary>
/// Carries only color-input transfer behavior visible in the immutable RSX
/// fragment IR into the generic EditorPreview fallback. Unknown or ambiguous
/// dataflow deliberately remains unmodified.
/// </summary>
internal static class MapRenderTranslatedColorInputContract
{
    public static int ResolveLinearizationMask(
        ShaderExecutionContract execution,
        IReadOnlyList<MaterialColorLayer> colorLayers,
        int maximumLayerCount)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(colorLayers);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumLayerCount);

        if (!execution.ProgramIrReady || execution.FragmentProgramIr is null)
            return 0;

        int mask = 0;
        int layerCount = Math.Min(
            Math.Min(colorLayers.Count, maximumLayerCount),
            sizeof(int) * 8 - 1);
        for (int layerIndex = 0; layerIndex < layerCount; layerIndex++)
        {
            if (MapRenderColorInputIrCompatibilityClassifier
                    .RequiresLinearization(
                        execution.FragmentProgramIr,
                        colorLayers[layerIndex].Identity.SamplerDest))
            {
                mask |= 1 << layerIndex;
            }
        }

        return mask;
    }
}
