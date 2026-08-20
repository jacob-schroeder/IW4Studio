using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.Execution;
using IW4.Render.Resources;
using IW4.Render.Shaders;

namespace IW4.Render.Lighting;

/// <summary>
/// Identifies the native static-model lighting contract without relying on
/// technique names. The PS3 path is present only when one selected program
/// consumes the row-0x39 tile center, the row-0x21 sampler transform, and the
/// model-lighting cache volume.
/// </summary>
internal readonly record struct MapRenderStaticModelLightingContract(
    bool AddsDirectionalDiffuse,
    bool AddsDirectionalSpecular)
{
    internal static bool TryCreate(
        ShaderExecutionContract execution,
        out MapRenderStaticModelLightingContract contract)
    {
        ArgumentNullException.ThrowIfNull(execution);
        return TryCreate(
            execution.RuntimeSamplerRequirements,
            execution.ProgramSamplerDestinations,
            execution.CodeSamplerDestinations,
            execution.ProgramVertexConstantDestinations,
            execution.ConstantDestinations,
            execution.CodePixelConstantPatchPlans,
            out contract);
    }

    internal static bool TryCreate(
        RenderWorldShaderProvenanceSnapshot execution,
        out MapRenderStaticModelLightingContract contract)
    {
        ArgumentNullException.ThrowIfNull(execution);
        return TryCreate(
            execution.RuntimeSamplerRequirements,
            execution.ProgramSamplerDestinations,
            execution.CodeSamplerDestinations,
            execution.ProgramVertexConstantDestinations,
            execution.ConstantDestinations,
            execution.CodePixelConstantPatchPlans,
            out contract);
    }

    private static bool TryCreate(
        IEnumerable<ShaderRuntimeSamplerRequirement> runtimeRequirements,
        IEnumerable<int> programSamplerDestinations,
        IEnumerable<ShaderSamplerDestination> codeSamplerDestinations,
        IEnumerable<int> programVertexConstantDestinations,
        IEnumerable<ShaderConstantDestination> constantDestinations,
        IEnumerable<CodePixelConstantPatchPlan> codePixelConstantPatchPlans,
        out MapRenderStaticModelLightingContract contract)
    {
        contract = default;

        if (!HasConsumedAtlasSampler(
                runtimeRequirements,
                programSamplerDestinations,
                codeSamplerDestinations) ||
            !HasVertexSourceRow(
                programVertexConstantDestinations,
                constantDestinations,
                FrameDirectCodeConstants
                    .StaticModelBaseLightingCoordsRowIndex) ||
            !HasPixelSourceRow(
                constantDestinations,
                codePixelConstantPatchPlans,
                FrameDirectCodeConstants.ModelLightingSamplerRowIndex))
        {
            return false;
        }

        bool hasDirectionalDirection =
            HasPixelSourceRow(
                constantDestinations,
                codePixelConstantPatchPlans,
                FrameDirectCodeConstants
                    .DirectionalLightDirectionRowIndex);
        bool addsDirectionalDiffuse =
            hasDirectionalDirection &&
            HasPixelSourceRow(
                constantDestinations,
                codePixelConstantPatchPlans,
                FrameDirectCodeConstants
                    .DirectionalLightDiffuseRowIndex);
        bool addsDirectionalSpecular =
            hasDirectionalDirection &&
            HasPixelSourceRow(
                constantDestinations,
                codePixelConstantPatchPlans,
                FrameDirectCodeConstants
                    .DirectionalLightSpecularRowIndex);
        contract = new(
            addsDirectionalDiffuse,
            addsDirectionalSpecular);
        return true;
    }

    /// <summary>
    /// Rejects a scene before backend resource construction when an actually-read
    /// raw-3 model-lighting sampler has no atlas publication. This
    /// prevents an atlas-authored static program from silently falling back to
    /// the generic ambient path.
    /// </summary>
    internal static void ValidateAtlasAvailability(
        MapRenderStaticModelLightingAtlas? atlas,
        IEnumerable<ShaderExecutionContract?> executions)
    {
        ArgumentNullException.ThrowIfNull(executions);
        if (atlas is not null)
            return;

        if (executions.Any(execution =>
                execution is not null &&
                HasConsumedAtlasSampler(
                    execution.RuntimeSamplerRequirements,
                    execution.ProgramSamplerDestinations,
                    execution.CodeSamplerDestinations)))
        {
            throw new InvalidDataException(
                "A static-model program consumes the raw-3 " +
                "model-lighting atlas, but the scene did not publish it.");
        }
    }

    private static bool HasConsumedAtlasSampler(
        IEnumerable<ShaderRuntimeSamplerRequirement> runtimeRequirements,
        IEnumerable<int> programSamplerDestinations,
        IEnumerable<ShaderSamplerDestination> codeSamplerDestinations)
    {
        int[] sampledDestinations = programSamplerDestinations.ToArray();
        ShaderRuntimeSamplerRequirement[] requirements = runtimeRequirements
            .Where(requirement =>
                requirement.CodeSamplerArgument ==
                    MaterialTextureSource.ModelLighting &&
                requirement.ResourceKind ==
                    ShaderRuntimeSamplerResourceKind
                        .ModelLightingAtlas &&
                requirement.Status ==
                    ShaderRuntimeSamplerRequirementStatus
                        .ImmutableSceneAtlasRequired &&
                sampledDestinations.Contains(
                    requirement.Destination))
            .ToArray();
        if (requirements.Length != 1)
            return false;

        ShaderRuntimeSamplerRequirement requirement =
            requirements[0];
        return codeSamplerDestinations.Count(destination =>
            destination.ArgumentIndex == requirement.ArgumentIndex &&
            string.Equals(
                destination.ArgumentType,
                "CodePixelSampler",
                StringComparison.Ordinal) &&
            destination.Destination == requirement.Destination &&
            destination.Argument ==
                (uint)MaterialTextureSource.ModelLighting &&
            string.Equals(
                destination.TextureTarget,
                "Texture3D",
                StringComparison.Ordinal)) == 1;
    }

    private static bool HasVertexSourceRow(
        IEnumerable<int> programVertexConstantDestinations,
        IEnumerable<ShaderConstantDestination> constantDestinations,
        ushort sourceRow) =>
        constantDestinations.Any(destination =>
            string.Equals(
                destination.ArgumentType,
                "CodeVertexConst",
                StringComparison.Ordinal) &&
            destination.CodeConstantSourceRow == sourceRow &&
            programVertexConstantDestinations.Contains(
                destination.Destination));

    private static bool HasPixelSourceRow(
        IEnumerable<ShaderConstantDestination> constantDestinations,
        IEnumerable<CodePixelConstantPatchPlan> codePixelConstantPatchPlans,
        ushort sourceRow) =>
        constantDestinations.Any(destination =>
            string.Equals(
                destination.ArgumentType,
                "CodePixelConst",
                StringComparison.Ordinal) &&
            destination.CodeConstantSourceRow == sourceRow &&
            codePixelConstantPatchPlans.Any(plan =>
                plan.ArgumentOrdinal == destination.ArgumentIndex &&
                plan.Destination == destination.Destination &&
                plan.CodeIndex == sourceRow &&
                plan.IsDirectSourceResolved));
}
