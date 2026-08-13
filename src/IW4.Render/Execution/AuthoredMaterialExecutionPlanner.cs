using IW4.Render.Techniques;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.Materials;
using IW4.Render.Shaders;

namespace IW4.Render.Execution;

/// <summary>
/// Shared authored-pass execution planning for map and standalone geometry.
/// </summary>
internal static class AuthoredMaterialExecutionPlanner
{
    internal static ShaderExecutionContract CreateContract(
        MaterialAsset? material,
        MaterialTechniqueSetAsset? techniqueSet,
        IMaterialExecutionLookup lookup,
        MaterialPassIdentity pass,
        MaterialSamplerIdentity? primarySampler,
        RenderState state,
        string fallbackTextureName,
        IReadOnlyList<MaterialSamplerBinding> materialSamplers,
        bool vertexInputPayloadReady,
        string vertexInputPayloadBlocker,
        bool authoredSourcePassAvailable,
        ShaderExecutionPurpose purpose =
            ShaderExecutionPurpose.CameraColor,
        ShaderTranslationCache? shaderTranslationCache = null,
        int? fixedVertexSourceBackendRow = null,
        IReadOnlySet<int>? explicitCubeSamplerDestinations = null,
        IReadOnlyList<ShaderVertexInputBinding>? explicitVertexInputs = null,
        IReadOnlyList<string>?
            scopedResourceIdentities = null) =>
        ShaderExecutionContractFactory.Create(
            material,
            techniqueSet,
            lookup,
            new ShaderExecutionPassSelection(
                pass,
                primarySampler,
                state,
                fallbackTextureName),
            materialSamplers,
            vertexInputPayloadReady,
            vertexInputPayloadBlocker,
            authoredSourcePassAvailable,
            purpose,
            shaderTranslationCache,
            fixedVertexSourceBackendRow,
            explicitCubeSamplerDestinations,
            explicitVertexInputs,
            scopedResourceIdentities);
}
