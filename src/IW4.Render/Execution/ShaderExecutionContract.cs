using IW4.Render.Shaders;

namespace IW4.Render.Execution;

public enum ShaderExecutionPurpose
{
    CameraColor,
    DepthOnly
}

public sealed record ShaderExecutionContract(
    ShaderProgramIdentity VertexProgram,
    ShaderProgramIdentity PixelProgram,
    string VertexDeclarationIdentity,
    IReadOnlyList<ShaderVertexInputBinding> VertexInputs,
    IReadOnlyList<ShaderSamplerDestination> MaterialSamplerDestinations,
    IReadOnlyList<ShaderSamplerDestination> CustomSamplerDestinations,
    IReadOnlyList<ShaderSamplerDestination> CodeSamplerDestinations,
    IReadOnlyList<ShaderRuntimeSamplerRequirement>
        RuntimeSamplerRequirements,
    IReadOnlyList<int> ProgramSamplerDestinations,
    IReadOnlyList<int> ProgramVertexConstantDestinations,
    IReadOnlyList<ShaderConstantDestination> ConstantDestinations,
    IReadOnlyList<EmbeddedVertexConstant> EmbeddedVertexConstants,
    IReadOnlyList<CodePixelConstantPatchPlan>
        CodePixelConstantPatchPlans,
    uint FragmentProgramControl,
    string FragmentExportPrecision,
    bool FragmentDepthExportEnabled,
    IReadOnlyList<ShaderFragmentExport> FragmentColorExports,
    string ProgramCacheKey,
    bool ProgramIrReady,
    bool VertexInputPayloadReady,
    bool RendererProgramReady,
    IReadOnlyList<string> RendererBlockers)
{
    public ShaderExecutionPurpose Purpose { get; init; } =
        ShaderExecutionPurpose.CameraColor;

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
        IReadOnlyList<ShaderRuntimeSamplerBinding> runtimeSamplers)
    {
        ArgumentNullException.ThrowIfNull(runtimeSamplers);
        if (revision < 0 || !RendererProgramReady)
            return false;

        return RuntimeSamplerRequirements.All(requirement =>
            runtimeSamplers.Any(binding =>
                binding.Destination == requirement.Destination &&
                binding.ResourceKind == requirement.ResourceKind &&
                (requirement.Status is
                    ShaderRuntimeSamplerRequirementStatus
                        .ImmutableSceneAtlasRequired or
                    ShaderRuntimeSamplerRequirementStatus
                        .ImmutableSceneTextureRequired ||
                 (RequiresSameRevision(requirement.Status) &&
                  binding.Revision == revision)) &&
                binding.Status ==
                    ShaderRuntimeSamplerBindingStatus.Ready));
    }

    private static bool RequiresSameRevision(
        ShaderRuntimeSamplerRequirementStatus status) =>
        status is
            ShaderRuntimeSamplerRequirementStatus
                .SameRevisionAtlasRequired or
            ShaderRuntimeSamplerRequirementStatus
                .SameRevisionTextureRequired;

    public string ProgramExecutionStatus => ProgramExecutionReady
        ? "RENDERER_PROGRAM_EXECUTION_READY"
        : !RendererProgramReady
            ? $"RENDERER_PROGRAM_EXECUTION_BLOCKED:{string.Join('|', RendererBlockers)}"
            : $"RENDERER_RUNTIME_SAMPLERS_DEFERRED:{string.Join('|', RuntimeSamplerRequirements.Select(requirement => $"{requirement.ResourceKind}@{requirement.Destination}:{requirement.Status}"))}";
}
