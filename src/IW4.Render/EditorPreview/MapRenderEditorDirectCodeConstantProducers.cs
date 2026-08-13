using System.Numerics;

using IW4.Render.Execution;
using IW4.Render.Execution.Fog;
using IW4.Render.Lighting;
using IW4.Render.Shaders;

namespace IW4.Render.EditorPreview;

internal static class MapRenderEditorDirectCodeConstantProducers
{
    internal static IReadOnlyList<DirectCodeConstantRow> ProduceDirectionalSunRows(
        MapRenderEditorPreviewLightingPlan lighting,
        MapRenderEditorPreviewPrimaryLightVisionState? primaryLight = null,
        float diffuseColorScale = FrameDirectCodeConstants.DefaultDiffuseColorScale,
        float specularColorScale = FrameDirectCodeConstants.DefaultSpecularColorScale)
    {
        Vector3 colors = ProduceDirectionalSunLinearColors(lighting, primaryLight, diffuseColorScale, specularColorScale, out Vector3 specular);
        Vector3 direction = lighting.DirectionalSunCodeDirection;
        return [new(FrameDirectCodeConstants.DirectionalLightDirectionRowIndex, new(direction.X, direction.Y, direction.Z, 0f)), new(FrameDirectCodeConstants.DirectionalLightDiffuseRowIndex, new(colors.X, colors.Y, colors.Z, 1f)), new(FrameDirectCodeConstants.DirectionalLightSpecularRowIndex, new(specular.X, specular.Y, specular.Z, 1f))];
    }

    internal static DirectionalSunLinearColors ProduceDirectionalSunLinearColors(
        MapRenderEditorPreviewLightingPlan lighting,
        MapRenderEditorPreviewPrimaryLightVisionState? primaryLight = null,
        float diffuseColorScale = FrameDirectCodeConstants.DefaultDiffuseColorScale,
        float specularColorScale = FrameDirectCodeConstants.DefaultSpecularColorScale)
    {
        Vector3 diffuse = ProduceDirectionalSunLinearColors(lighting, primaryLight, diffuseColorScale, specularColorScale, out Vector3 specular);
        return new(diffuse, specular);
    }

    private static Vector3 ProduceDirectionalSunLinearColors(MapRenderEditorPreviewLightingPlan lighting, MapRenderEditorPreviewPrimaryLightVisionState? primaryLight, float diffuseColorScale, float specularColorScale, out Vector3 specular)
    {
        ArgumentNullException.ThrowIfNull(lighting);
        if (!lighting.HasDirectionalSun)
            throw new ArgumentException("Directional sun rows require one active editor sun.", nameof(lighting));
        if (!float.IsFinite(diffuseColorScale) || diffuseColorScale < 0f)
            throw new ArgumentOutOfRangeException(nameof(diffuseColorScale));
        if (!float.IsFinite(specularColorScale) || specularColorScale < 0f)
            throw new ArgumentOutOfRangeException(nameof(specularColorScale));
        if (primaryLight is not null && !primaryLight.HasFiniteNonnegativeStrengths)
            throw new ArgumentException("Primary-light tweak strengths must be finite and nonnegative.", nameof(primaryLight));
        if (primaryLight?.UseTweaks == true)
        { diffuseColorScale *= primaryLight.DiffuseStrength; specularColorScale *= primaryLight.SpecularStrength; }
        Vector3 color = lighting.DirectionalSunColor;
        Vector3 diffuse = color * diffuseColorScale;
        Vector3 specularGamma = color * specularColorScale;
        specular = new(GammaColorTransfer.ToLinear(specularGamma.X), GammaColorTransfer.ToLinear(specularGamma.Y), GammaColorTransfer.ToLinear(specularGamma.Z));
        return new(GammaColorTransfer.ToLinear(diffuse.X), GammaColorTransfer.ToLinear(diffuse.Y), GammaColorTransfer.ToLinear(diffuse.Z));
    }
}
