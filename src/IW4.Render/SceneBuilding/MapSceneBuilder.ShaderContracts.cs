using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.Assets;
using IW4.Render.EditorPreview;
using IW4.Render.Execution;
using IW4.Render.Geometry;
using IW4.Render.Materials;
using IW4.Render.Shaders;
using IW4.Render.Techniques;

namespace IW4.Render.SceneBuilding;

public sealed partial class MapSceneBuilder
{
    internal static ShaderExecutionContract BuildShaderExecutionContract(
        MaterialAsset? material,
        MaterialTechniqueSetAsset? techset,
        RenderAssetLookup lookup,
        SelectedColorPass selectedPass,
        IReadOnlyList<MapRenderWorldMaterialSamplerBinding> materialSamplers,
        bool vertexInputPayloadReady,
        string vertexInputPayloadBlocker,
        bool authoredSourcePassAvailable,
        ShaderExecutionPurpose purpose =
            ShaderExecutionPurpose.CameraColor,
        ShaderTranslationCache? shaderTranslationCache = null,
        int? fixedVertexSourceBackendRow = null,
        IReadOnlySet<int>? explicitCubeSamplerDestinations = null) =>
        AuthoredMaterialExecutionPlanner.CreateContract(
            material,
            techset,
            lookup,
            selectedPass.Pass,
            selectedPass.PrimarySampler,
            selectedPass.State,
            selectedPass.Image.Name ?? string.Empty,
            materialSamplers.Select(binding => binding.Binding).ToArray(),
            vertexInputPayloadReady,
            vertexInputPayloadBlocker,
            authoredSourcePassAvailable,
            purpose,
            shaderTranslationCache,
            fixedVertexSourceBackendRow,
            explicitCubeSamplerDestinations,
            MaterialVertexInputBindingPlanner.Resolve(
                techset,
                lookup,
                selectedPass.Pass,
                fixedVertexSourceBackendRow),
            materialSamplers
                .Select(binding => string.Concat(
                    binding.RuntimeTextureIdentity?.ToString() ??
                        "NO_WORLD_SLOT",
                    ":",
                    binding.Binding.ResourceBindingIdentity))
                .ToArray());

    internal static ShaderExecutionContract BuildShaderExecutionContract(
        MaterialAsset? material,
        MaterialTechniqueSetAsset? techset,
        RenderAssetLookup lookup,
        SelectedColorPass selectedPass,
        IReadOnlyList<MaterialSamplerBinding> materialSamplers,
        bool vertexInputPayloadReady,
        string vertexInputPayloadBlocker,
        bool authoredSourcePassAvailable,
        ShaderExecutionPurpose purpose =
            ShaderExecutionPurpose.CameraColor,
        ShaderTranslationCache? shaderTranslationCache = null,
        int? fixedVertexSourceBackendRow = null,
        IReadOnlySet<int>? explicitCubeSamplerDestinations = null) =>
        AuthoredMaterialExecutionPlanner.CreateContract(
            material,
            techset,
            lookup,
            selectedPass.Pass,
            selectedPass.PrimarySampler,
            selectedPass.State,
            selectedPass.Image.Name ?? string.Empty,
            materialSamplers,
            vertexInputPayloadReady,
            vertexInputPayloadBlocker,
            authoredSourcePassAvailable,
            purpose,
            shaderTranslationCache,
            fixedVertexSourceBackendRow,
            explicitCubeSamplerDestinations,
            MaterialVertexInputBindingPlanner.Resolve(
                techset,
                lookup,
                selectedPass.Pass,
                fixedVertexSourceBackendRow));

    internal static ShaderVertexInputBinding[] ResolveSelectedVertexInputs(
        MaterialTechniqueSetAsset? techset,
        RenderAssetLookup lookup,
        SelectedColorPass selectedPass,
        int? fixedVertexSourceBackendRow = null)
    {
        return MaterialVertexInputBindingPlanner.Resolve(
            techset,
            lookup,
            selectedPass.Pass,
            fixedVertexSourceBackendRow);
    }

    private static SelectedColorPass CreateStandardDepthPrepassSelection(
        SelectedColorPass colorPass,
        MapRenderEditorDepthPrepassPlan plan) => new(
            colorPass.Texture,
            colorPass.Image,
            new MaterialPassIdentity(
                plan.MaterialName,
                new TechniquePassIdentity(
                    plan.TechniqueSetName,
                    plan.TechniqueSlot,
                    plan.TechniqueName,
                    MaterialPassClassifier.NonColorWrite,
                    plan.PassIndex,
                    CustomSamplerFlags: 0)),
            new MaterialSamplerIdentity(
                SamplerArgIndex: -1,
                SamplerDest: 0,
                SamplerHash: 0,
                TextureSemantic: 0),
            plan.State,
            UnresolvedCodeSamplerCount: 0,
            TexCoordSource: 0,
            TexCoordSourceIsEngineRouted: false,
            AuthoredProgramExecutable: true);

    /// <summary>
    /// The translated world arena stores one vec4 per RSX destination. A
    /// camera-color program and its depth owner may share that slab only when
    /// a destination means the same source route to both programs. Extra
    /// depth-only destinations can be decoded into otherwise-unused rows.
    /// </summary>
    internal static bool TryMergeVertexInputBindings(
        IReadOnlyList<ShaderVertexInputBinding> colorBindings,
        IReadOnlyList<ShaderVertexInputBinding> depthBindings,
        out ShaderVertexInputBinding[] merged,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(colorBindings);
        ArgumentNullException.ThrowIfNull(depthBindings);

        var byDestination = new Dictionary<byte, ShaderVertexInputBinding>();
        var result = new List<ShaderVertexInputBinding>(
            colorBindings.Count + depthBindings.Count);
        foreach (ShaderVertexInputBinding binding in colorBindings)
        {
            if (byDestination.TryGetValue(
                    binding.Destination,
                    out ShaderVertexInputBinding? existing) &&
                !VertexInputRoutesMatch(existing, binding))
            {
                merged = [];
                blocker =
                    $"COLOR_VERTEX_INPUT_DEST{binding.Destination}_ROUTE_CONFLICT";
                return false;
            }
            if (existing is not null)
                continue;
            byDestination.Add(binding.Destination, binding);
            result.Add(binding);
        }

        foreach (ShaderVertexInputBinding binding in depthBindings)
        {
            if (byDestination.TryGetValue(
                    binding.Destination,
                    out ShaderVertexInputBinding? existing))
            {
                if (!VertexInputRoutesMatch(existing, binding))
                {
                    merged = colorBindings.ToArray();
                    blocker =
                        $"DEPTH_VERTEX_INPUT_DEST{binding.Destination}_ROUTE_CONFLICT";
                    return false;
                }
                continue;
            }

            byDestination.Add(binding.Destination, binding);
            result.Add(binding);
        }

        merged = result.ToArray();
        blocker = string.Empty;
        return true;
    }

    private static bool VertexInputRoutesMatch(
        ShaderVertexInputBinding first,
        ShaderVertexInputBinding next) =>
        first.Source == next.Source &&
        first.Destination == next.Destination &&
        first.StreamIndex == next.StreamIndex &&
        first.Stride == next.Stride &&
        first.Offset == next.Offset &&
        first.ComponentCount == next.ComponentCount &&
        first.RsxType == next.RsxType;
}
