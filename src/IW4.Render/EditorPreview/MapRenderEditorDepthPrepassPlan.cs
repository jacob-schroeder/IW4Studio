using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.Materials;

namespace IW4.Render.EditorPreview;

/// <summary>
/// Asset mapping for the standard normal-camera depth prepass. World geometry
/// executes the resolved authored programs when its RSX input routes match;
/// generic/static geometry uses the bounded host lowering. This is narrower
/// than the complete native prepass category table: alpha-tested and float-Z
/// variants require their own authored fragment coverage implementations.
/// </summary>
public enum MapRenderEditorDepthPrepassProgram
{
    TransformOnlyNull
}

public sealed record MapRenderEditorDepthPrepassPlan(
    string MaterialName,
    string TechniqueSetName,
    int TechniqueSlot,
    string TechniqueName,
    int PassIndex,
    ushort TechniqueFlags,
    string VertexProgramName,
    string PixelProgramName,
    MapRenderEditorDepthPrepassProgram Program,
    MapRenderState State);

/// <summary>
/// Exact asset-to-execution mapping for IW4's standard opaque depth owner.
/// Slot 0 is the normal-camera prepass slot; shadow-map depth uses a different
/// slot and must not be accepted here.
/// </summary>
public static class MapRenderEditorDepthPrepassPlanner
{
    public const int StandardTechniqueSlot = 0;
    public const int StandardPassIndex = 0;
    public const byte MaterialStateFlagStandardDepthPrepass = 0x01;
    public const ushort TransformOnlyTechniqueFlags = 0x0004;
    public const string StandardTechniqueName = "zprepass";
    public const string TransformOnlyVertexProgramName = "transform_only.hlsl";
    public const string NullPixelProgramName = "null.hlsl";
    public const ushort World0VertexConstantDestination = 4;
    public const int World0VertexConstantArgument = 0x005F0004;
    public const ushort ViewProjectionVertexConstantDestination = 0;
    public const int ViewProjectionVertexConstantArgument = 0x00530004;

    public static bool TryCreateStandard(
        string materialName,
        string techniqueSetName,
        byte materialStateFlags,
        int techniqueSlot,
        string techniqueName,
        int passIndex,
        int techniquePassCount,
        ushort techniqueFlags,
        IReadOnlyList<MaterialShaderArgumentAsset> arguments,
        string vertexProgramName,
        string pixelProgramName,
        MapRenderState state,
        out MapRenderEditorDepthPrepassPlan? plan,
        out string blocker)
    {
        plan = null;
        blocker = string.Empty;
        ArgumentNullException.ThrowIfNull(arguments);
        if ((materialStateFlags & MaterialStateFlagStandardDepthPrepass) == 0)
        {
            blocker = "MATERIAL_STANDARD_DEPTH_PREPASS_FLAG_NOT_SET";
            return false;
        }
        if (techniqueSlot != StandardTechniqueSlot ||
            passIndex != StandardPassIndex ||
            techniquePassCount != 1)
        {
            blocker = "STANDARD_DEPTH_PREPASS_SLOT_OR_PASS_SHAPE_MISMATCH";
            return false;
        }
        if (!string.Equals(
                techniqueName,
                StandardTechniqueName,
                StringComparison.Ordinal))
        {
            blocker = "STANDARD_DEPTH_PREPASS_TECHNIQUE_NAME_MISMATCH";
            return false;
        }
        if (techniqueFlags != TransformOnlyTechniqueFlags)
        {
            blocker = "STANDARD_DEPTH_PREPASS_TECHNIQUE_FLAGS_MISMATCH";
            return false;
        }
        if (arguments.Any(argument =>
                argument.Type is
                    MaterialShaderArgumentType.MaterialPixelSampler or
                    MaterialShaderArgumentType.CodePixelSampler))
        {
            blocker = "STANDARD_DEPTH_PREPASS_HAS_SAMPLER_ARGUMENTS";
            return false;
        }
        if (!HasExactTransformOnlyArguments(arguments))
        {
            blocker = "STANDARD_DEPTH_PREPASS_ARGUMENT_MAPPING_MISMATCH";
            return false;
        }
        if (!ProgramNameMatches(
                vertexProgramName,
                TransformOnlyVertexProgramName) ||
            !ProgramNameMatches(
                pixelProgramName,
                NullPixelProgramName))
        {
            blocker = "STANDARD_DEPTH_PREPASS_PROGRAM_MAPPING_UNRESOLVED";
            return false;
        }
        if (!state.HasState ||
            state.ColorMask != 0 ||
            state.AlphaTestEnabled ||
            state.BlendEnabled ||
            !state.DepthTestEnabled ||
            !state.DepthWriteEnabled ||
            state.DepthFunc != 0x0203 ||
            state.Stencil.Enabled)
        {
            blocker = "STANDARD_DEPTH_PREPASS_STATE_CONTRACT_MISMATCH";
            return false;
        }

        plan = new MapRenderEditorDepthPrepassPlan(
            materialName,
            techniqueSetName,
            techniqueSlot,
            techniqueName,
            passIndex,
            techniqueFlags,
            vertexProgramName,
            pixelProgramName,
            MapRenderEditorDepthPrepassProgram.TransformOnlyNull,
            state);
        return true;
    }

    private static bool HasExactTransformOnlyArguments(
        IReadOnlyList<MaterialShaderArgumentAsset> arguments) =>
        arguments.Count == 2 &&
        IsCodeVertexConstant(
            arguments[0],
            World0VertexConstantDestination,
            World0VertexConstantArgument) &&
        IsCodeVertexConstant(
            arguments[1],
            ViewProjectionVertexConstantDestination,
            ViewProjectionVertexConstantArgument);

    private static bool IsCodeVertexConstant(
        MaterialShaderArgumentAsset argument,
        ushort destination,
        int rawArgument) =>
        argument.Type == MaterialShaderArgumentType.CodeVertexConst &&
        argument.Dest == destination &&
        argument.ArgumentRaw == rawArgument;

    private static bool ProgramNameMatches(
        string observed,
        string expected)
    {
        ReadOnlySpan<char> normalized = observed.AsSpan().Trim();
        if (!normalized.IsEmpty && normalized[0] == ',')
            normalized = normalized[1..];
        return normalized.Equals(
            expected.AsSpan(),
            StringComparison.Ordinal);
    }
}
