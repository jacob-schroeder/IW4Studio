using IW4.Render.Shaders;

namespace IW4.Render.Execution;

public enum MapRenderShaderExecutionPurpose
{
    CameraColor,
    DepthOnly
}

public sealed record MapRenderShaderExecutionContract(
    MapRenderShaderProgramIdentity VertexProgram,
    MapRenderShaderProgramIdentity PixelProgram,
    string VertexDeclarationIdentity,
    IReadOnlyList<MapRenderShaderVertexInputBinding> VertexInputs,
    IReadOnlyList<MapRenderShaderSamplerDestination> MaterialSamplerDestinations,
    IReadOnlyList<MapRenderShaderSamplerDestination> CustomSamplerDestinations,
    IReadOnlyList<MapRenderShaderSamplerDestination> CodeSamplerDestinations,
    IReadOnlyList<MapRenderShaderRuntimeSamplerRequirement>
        RuntimeSamplerRequirements,
    IReadOnlyList<int> ProgramSamplerDestinations,
    IReadOnlyList<int> ProgramVertexConstantDestinations,
    IReadOnlyList<MapRenderShaderSamplerDestination> ConstantDestinations,
    IReadOnlyList<MapRenderEmbeddedVertexConstant> EmbeddedVertexConstants,
    IReadOnlyList<MapRenderCodePixelConstantPatchPlan>
        CodePixelConstantPatchPlans,
    uint FragmentProgramControl,
    string FragmentExportPrecision,
    bool FragmentDepthExportEnabled,
    IReadOnlyList<MapRenderShaderFragmentExport> FragmentColorExports,
    string ProgramCacheKey,
    bool ProgramIrReady,
    bool VertexInputPayloadReady,
    bool RendererProgramReady,
    IReadOnlyList<string> RendererBlockers)
{
    public MapRenderShaderExecutionPurpose Purpose { get; init; } =
        MapRenderShaderExecutionPurpose.CameraColor;

    /// <summary>
    /// Exact backend-neutral vertex IR used to produce this contract.
    /// </summary>
    public RsxVertexProgramIr? VertexProgramIr { get; init; }

    /// <summary>
    /// Exact backend-neutral fragment IR used to produce this contract.
    /// </summary>
    public RsxFragmentProgramIr? FragmentProgramIr { get; init; }

    /// <summary>
    /// Static scene readiness. Runtime-sampler programs remain fail-closed
    /// until a renderer validates publications with ProgramExecutionReadyFor.
    /// </summary>
    public bool ProgramExecutionReady =>
        RendererProgramReady && RuntimeSamplerRequirements.Count == 0;

    public bool ProgramExecutionReadyFor(
        long revision,
        IReadOnlyList<MapRenderShaderRuntimeSamplerBinding> runtimeSamplers)
    {
        ArgumentNullException.ThrowIfNull(runtimeSamplers);
        if (revision < 0 || !RendererProgramReady)
            return false;

        return RuntimeSamplerRequirements.All(requirement =>
            runtimeSamplers.Any(binding =>
                binding.Destination == requirement.Destination &&
                binding.ResourceKind == requirement.ResourceKind &&
                (requirement.Status ==
                    MapRenderShaderRuntimeSamplerRequirementStatus
                        .ImmutableSceneAtlasRequired ||
                 (RequiresSameRevision(requirement.Status) &&
                  binding.Revision == revision)) &&
                binding.Status ==
                    MapRenderShaderRuntimeSamplerBindingStatus.Ready));
    }

    private static bool RequiresSameRevision(
        MapRenderShaderRuntimeSamplerRequirementStatus status) =>
        status is
            MapRenderShaderRuntimeSamplerRequirementStatus
                .SameRevisionAtlasRequired or
            MapRenderShaderRuntimeSamplerRequirementStatus
                .SameRevisionTextureRequired;

    public string ProgramExecutionStatus => ProgramExecutionReady
        ? "RENDERER_PROGRAM_EXECUTION_READY"
        : !RendererProgramReady
            ? $"RENDERER_PROGRAM_EXECUTION_BLOCKED:{string.Join('|', RendererBlockers)}"
            : $"RENDERER_RUNTIME_SAMPLERS_DEFERRED:{string.Join('|', RuntimeSamplerRequirements.Select(requirement => $"{requirement.ResourceKind}@{requirement.Destination}:{requirement.Status}"))}";
}
