using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.Assets;
using IW4.Render.Materials;
using IW4.Render.Shaders;

namespace IW4.Render.Execution;

/// <summary>
/// Shared authored-pass execution planning for map and standalone geometry.
/// </summary>
internal static class AuthoredMaterialExecutionPlanner
{
    internal static MapRenderShaderExecutionContract CreateContract(
        MaterialAsset? material,
        MaterialTechniqueSetAsset? techniqueSet,
        RenderAssetLookup lookup,
        MapRenderMaterialPass pass,
        MapRenderState state,
        string fallbackTextureName,
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
            techniqueSet,
            lookup,
            new MapRenderShaderExecutionPassSelection(
                pass,
                state,
                fallbackTextureName),
            materialSamplers,
            vertexInputPayloadReady,
            vertexInputPayloadBlocker,
            authoredSourcePassAvailable,
            purpose,
            shaderTranslationCache,
            fixedVertexSourceBackendRow,
            explicitCubeSamplerDestinations);

    internal static MapRenderShaderVertexInputBinding[] ResolveVertexInputs(
        MaterialTechniqueSetAsset? techniqueSet,
        RenderAssetLookup lookup,
        MapRenderMaterialPass pass,
        int? fixedVertexSourceBackendRow = null)
    {
        if (techniqueSet is null ||
            pass.TechniqueSlot < 0 ||
            pass.PassIndex < 0)
        {
            return [];
        }

        MaterialTechniqueSlot? slot = lookup
            .ResolveTechniqueSlots(techniqueSet)
            .FirstOrDefault(candidate =>
                candidate.Index == pass.TechniqueSlot);
        if (slot?.Technique is not { } technique ||
            (uint)pass.PassIndex >= (uint)technique.Passes.Count)
        {
            return [];
        }

        MaterialPassAsset sourcePass = technique.Passes[pass.PassIndex];
        MapRenderSelectedPassProgramSources sources = lookup.ResolveSources(
            techniqueSet,
            technique,
            new MapRenderSelectedTechniquePass(
                pass.PassIndex,
                sourcePass));
        return MapRenderShaderExecutionContractFactory
            .CreateVertexInputBindings(
                techniqueSet,
                technique.Flags,
                sources.VertexDeclaration,
                sources.VertexProgram.HasProgramData
                    ? RsxShaderTranslator.ReadVertexInputDestinations(
                        sources.VertexProgram.Data.ToArray())
                    : null,
                fixedVertexSourceBackendRow);
    }
}
