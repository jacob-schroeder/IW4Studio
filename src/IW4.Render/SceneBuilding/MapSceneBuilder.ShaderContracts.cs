using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.Assets;
using IW4.Render.EditorPreview;
using IW4.Render.Execution;
using IW4.Render.Materials;
using IW4.Render.Shaders;

namespace IW4.Render.SceneBuilding;

public sealed partial class MapSceneBuilder
{
    internal static MapRenderShaderExecutionContract BuildShaderExecutionContract(
        MaterialAsset? material,
        MaterialTechniqueSetAsset? techset,
        RenderAssetLookup lookup,
        SelectedColorPass selectedPass,
        IReadOnlyList<MapRenderMaterialSamplerBinding> materialSamplers,
        bool vertexInputPayloadReady,
        string vertexInputPayloadBlocker,
        bool authoredSourcePassAvailable,
        MapRenderShaderExecutionPurpose purpose =
            MapRenderShaderExecutionPurpose.CameraColor,
        MapRenderShaderTranslationCache? shaderTranslationCache = null,
        int? fixedVertexSourceBackendRow = null,
        IReadOnlySet<int>? explicitCubeSamplerDestinations = null) =>
        MapRenderShaderExecutionContractFactory.Create(
            material,
            techset,
            lookup,
            new MapRenderShaderExecutionPassSelection(
                selectedPass.Pass,
                selectedPass.State,
                selectedPass.Image.Name ?? string.Empty),
            materialSamplers,
            vertexInputPayloadReady,
            vertexInputPayloadBlocker,
            authoredSourcePassAvailable,
            purpose,
            shaderTranslationCache,
            fixedVertexSourceBackendRow,
            explicitCubeSamplerDestinations);

    private static MapRenderShaderVertexInputBinding[] ResolveSelectedVertexInputs(
        MaterialTechniqueSetAsset? techset,
        RenderAssetLookup lookup,
        SelectedColorPass selectedPass,
        int? fixedVertexSourceBackendRow = null)
    {
        if (techset is null || selectedPass.Pass.TechniqueSlot < 0 || selectedPass.Pass.PassIndex < 0)
            return [];
        MaterialTechniqueSlot? slot = lookup.ResolveTechniqueSlots(techset)
            .FirstOrDefault(candidate => candidate.Index == selectedPass.Pass.TechniqueSlot);
        if (slot?.Technique is not { } technique ||
            (uint)selectedPass.Pass.PassIndex >= (uint)technique.Passes.Count)
        {
            return [];
        }
        MaterialPassAsset sourcePass = technique.Passes[selectedPass.Pass.PassIndex];
        MapRenderSelectedPassProgramSources programSources = lookup.ResolveSources(
            techset,
            technique,
            new MapRenderSelectedTechniquePass(
                selectedPass.Pass.PassIndex,
                sourcePass));
        return MapRenderShaderExecutionContractFactory.CreateVertexInputBindings(
            techset,
            technique.Flags,
            programSources.VertexDeclaration,
            programSources.VertexProgram.HasProgramData
                ? RsxShaderTranslator.ReadVertexInputDestinations(
                    programSources.VertexProgram.Data.ToArray())
                : null,
            fixedVertexSourceBackendRow);
    }

    private static SelectedColorPass CreateStandardDepthPrepassSelection(
        SelectedColorPass colorPass,
        MapRenderEditorDepthPrepassPlan plan) => new(
            colorPass.Texture,
            colorPass.Image,
            new MapRenderMaterialPass(
                plan.MaterialName,
                plan.TechniqueSetName,
                plan.TechniqueSlot,
                plan.TechniqueName,
                MapRenderPassClassifier.NonColorWrite,
                plan.PassIndex,
                SamplerArgIndex: -1,
                SamplerDest: 0,
                SamplerHash: 0,
                TextureSemantic: 0,
                TexCoordSource: 0,
                CustomSamplerFlags: 0),
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
        IReadOnlyList<MapRenderShaderVertexInputBinding> colorBindings,
        IReadOnlyList<MapRenderShaderVertexInputBinding> depthBindings,
        out MapRenderShaderVertexInputBinding[] merged,
        out string blocker)
    {
        ArgumentNullException.ThrowIfNull(colorBindings);
        ArgumentNullException.ThrowIfNull(depthBindings);

        var byDestination = new Dictionary<byte, MapRenderShaderVertexInputBinding>();
        var result = new List<MapRenderShaderVertexInputBinding>(
            colorBindings.Count + depthBindings.Count);
        foreach (MapRenderShaderVertexInputBinding binding in colorBindings)
        {
            if (byDestination.TryGetValue(
                    binding.Destination,
                    out MapRenderShaderVertexInputBinding? existing) &&
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

        foreach (MapRenderShaderVertexInputBinding binding in depthBindings)
        {
            if (byDestination.TryGetValue(
                    binding.Destination,
                    out MapRenderShaderVertexInputBinding? existing))
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
        MapRenderShaderVertexInputBinding first,
        MapRenderShaderVertexInputBinding next) =>
        first.Source == next.Source &&
        first.Destination == next.Destination &&
        first.StreamIndex == next.StreamIndex &&
        first.Stride == next.Stride &&
        first.Offset == next.Offset &&
        first.ComponentCount == next.ComponentCount &&
        first.RsxType == next.RsxType;
}
