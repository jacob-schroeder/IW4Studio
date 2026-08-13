using IW4.Render.Execution;

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
        contract = default;

        if (!HasConsumedAtlasSampler(execution) ||
            !HasVertexSourceRow(
                execution,
                FrameDirectCodeConstants
                    .StaticModelBaseLightingCoordsRowIndex) ||
            !HasPixelSourceRow(
                execution,
                FrameDirectCodeConstants.ModelLightingSamplerRowIndex))
        {
            return false;
        }

        bool hasDirectionalDirection =
            HasPixelSourceRow(
                execution,
                FrameDirectCodeConstants
                    .DirectionalLightDirectionRowIndex);
        bool addsDirectionalDiffuse =
            hasDirectionalDirection &&
            HasPixelSourceRow(
                execution,
                FrameDirectCodeConstants
                    .DirectionalLightDiffuseRowIndex);
        bool addsDirectionalSpecular =
            hasDirectionalDirection &&
            HasPixelSourceRow(
                execution,
                FrameDirectCodeConstants
                    .DirectionalLightSpecularRowIndex);
        contract = new(
            addsDirectionalDiffuse,
            addsDirectionalSpecular);
        return true;
    }

    /// <summary>
    /// Rejects a scene before GL resource construction when an actually-read
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
                HasConsumedAtlasSampler(execution)))
        {
            throw new InvalidDataException(
                "A static-model program consumes the raw-3 " +
                "model-lighting atlas, but the scene did not publish it.");
        }
    }

    private static bool HasConsumedAtlasSampler(
        ShaderExecutionContract execution)
    {
        ShaderRuntimeSamplerRequirement[] requirements = execution
            .RuntimeSamplerRequirements
            .Where(requirement =>
                requirement.CodeSamplerArgument == 3 &&
                requirement.ResourceKind ==
                    ShaderRuntimeSamplerResourceKind
                        .ModelLightingAtlas &&
                requirement.Status ==
                    ShaderRuntimeSamplerRequirementStatus
                        .ImmutableSceneAtlasRequired &&
                execution.ProgramSamplerDestinations.Contains(
                    requirement.Destination))
            .ToArray();
        if (requirements.Length != 1)
            return false;

        ShaderRuntimeSamplerRequirement requirement =
            requirements[0];
        return execution.CodeSamplerDestinations.Count(destination =>
            destination.ArgumentIndex == requirement.ArgumentIndex &&
            string.Equals(
                destination.ArgumentType,
                "CodePixelSampler",
                StringComparison.Ordinal) &&
            destination.Destination == requirement.Destination &&
            destination.Argument == 3 &&
            string.Equals(
                destination.TextureTarget,
                "Texture3D",
                StringComparison.Ordinal)) == 1;
    }

    private static bool HasVertexSourceRow(
        ShaderExecutionContract execution,
        ushort sourceRow) =>
        execution.ConstantDestinations.Any(destination =>
            string.Equals(
                destination.ArgumentType,
                "CodeVertexConst",
                StringComparison.Ordinal) &&
            destination.CodeConstantSourceRow == sourceRow &&
            execution.ProgramVertexConstantDestinations.Contains(
                destination.Destination));

    private static bool HasPixelSourceRow(
        ShaderExecutionContract execution,
        ushort sourceRow) =>
        execution.ConstantDestinations.Any(destination =>
            string.Equals(
                destination.ArgumentType,
                "CodePixelConst",
                StringComparison.Ordinal) &&
            destination.CodeConstantSourceRow == sourceRow &&
            execution.CodePixelConstantPatchPlans.Any(plan =>
                plan.ArgumentOrdinal == destination.ArgumentIndex &&
                plan.Destination == destination.Destination &&
                plan.CodeIndex == sourceRow &&
                plan.IsDirectSourceResolved));
}
