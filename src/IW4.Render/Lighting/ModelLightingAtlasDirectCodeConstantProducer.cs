using IW4.Render.Execution;
using IW4.Render.Shaders;

namespace IW4.Render.Lighting;

internal static class ModelLightingAtlasDirectCodeConstantProducer
{
    internal static DirectCodeConstantRow ProduceSamplerRow() =>
        new(
            FrameDirectCodeConstants.ModelLightingSamplerRowIndex,
            new ShaderConstantValue(
                ModelLightingAtlasLayout.SamplerTransform.X,
                ModelLightingAtlasLayout.SamplerTransform.Y,
                ModelLightingAtlasLayout.SamplerTransform.Z,
                ModelLightingAtlasLayout.SamplerTransform.W));
}
