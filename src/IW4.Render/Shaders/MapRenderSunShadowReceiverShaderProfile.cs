using System.Collections.Immutable;

namespace IW4.Render.Shaders;

/// <summary>
/// Backend-neutral authored-register profile for the PS3 slot-13 sun-shadow
/// receiver path. Raw engine arguments and RSX destinations are different
/// namespaces and must never be substituted for one another.
/// </summary>
public static class MapRenderSunShadowReceiverShaderProfile
{
    public const uint RawCodeSamplerArgument = 6;

    public const ushort RsxSamplerDestination = 4;

    public const ushort ShadowLookupBaseVertexDestination = 24;

    public const ushort SwitchPartitionSourceRow = 0x1E;

    public const ushort SwitchPartitionFragmentDestination = 0x0D;

    public const ushort ShadowMapScaleSourceRow = 0x1F;

    public const ushort ShadowMapScaleFragmentDestination = 0x0E;

    public const int VertexProgramByteCount = 0xB60;

    public const int VertexUploadOffset = 0x8F0;

    public const int VertexInstructionCount = 39;

    public const string VertexProgramSha256 =
        "9DC067516D52DDC197556BBE119FBE2B0154F3966F22231681DC2AC353CFFDC5";

    public const int FragmentProgramByteCount = 0xF70;

    public const int FragmentUploadOffset = 0x7D0;

    public const int FragmentUploadSize = 0x7A0;

    public const int FragmentInstructionCount = 91;

    public const string FragmentProgramSha256 =
        "4D7705586E3EEFADC552E15AE7E3A9569B7027A5F2A370192A381D52AFCEC37B";

    /// <summary>
    /// Compact Vulkan locations retain only the four RSX inputs read by the
    /// exact receiver microcode. The authored destinations remain the ABI
    /// identity and are not replaced by these compact locations.
    /// </summary>
    public static ImmutableArray<int> VertexInputDestinations { get; } =
        [0, 2, 8, 10];

    /// <summary>
    /// O7 through O12 are the six fragment TEXCOORD inputs, compacted to
    /// Vulkan locations zero through five in this order.
    /// </summary>
    public static ImmutableArray<int> VertexOutputDestinations { get; } =
        [7, 8, 9, 10, 11, 12];

    /// <summary>
    /// Sparse authored c-registers are packed into one exact native constant
    /// payload in this order. No unreferenced c-register is materialized.
    /// </summary>
    public static ImmutableArray<ushort> VertexConstantDestinations
        { get; } =
        [0, 1, 2, 3, 4, 5, 6, 7, 21, 24, 25, 26, 27, 32, 33, 211];

    /// <summary>
    /// Ordinary material samplers remain set zero. Authored destination four
    /// is excluded because it is the set-one comparison atlas.
    /// </summary>
    public static ImmutableArray<ushort> MaterialSamplerDestinations
        { get; } = [0, 1, 2, 3, 5, 6];

    public static ImmutableArray<ushort> FragmentConstantDestinations
        { get; } = [6, 7, 8, 9, 10, 11, 12, 13, 14];

    public static ImmutableArray<ushort> FragmentConstantSourceRows
        { get; } = [0x00, 0x01, 0x02, 0x25, 0x27, 0x2A, 0x28, 0x1E, 0x1F];

    public static ImmutableArray<int> FragmentConstantPatchSiteCounts
        { get; } = [2, 1, 1, 2, 2, 1, 1, 1, 1];

    public static bool IsExactVertexProgram(RsxVertexProgramIr program)
    {
        ArgumentNullException.ThrowIfNull(program);
        return string.Equals(
                   program.DecoderVersion,
                   RsxVertexProgramIr.CurrentDecoderVersion,
                   StringComparison.Ordinal) &&
               program.HasValidUpload &&
               program.InputByteCount == VertexProgramByteCount &&
               program.UploadOffset == VertexUploadOffset &&
               program.Instructions.Length == VertexInstructionCount &&
               string.Equals(
                   program.InputSha256,
                   VertexProgramSha256,
                   StringComparison.Ordinal);
    }

    public static bool IsExactFragmentProgram(RsxFragmentProgramIr program)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (!string.Equals(
                program.DecoderVersion,
                RsxFragmentProgramIr.CurrentDecoderVersion,
                StringComparison.Ordinal) ||
            !string.Equals(
                program.SemanticTranslationVersion,
                RsxFragmentProgramIr.CurrentSemanticTranslationVersion,
                StringComparison.Ordinal) ||
            !program.HasValidUpload ||
            program.OriginalByteCount != FragmentProgramByteCount ||
            program.EffectiveByteCount != FragmentProgramByteCount ||
            program.UploadOffset != FragmentUploadOffset ||
            program.UploadSize != FragmentUploadSize ||
            program.Instructions.Length != FragmentInstructionCount ||
            !string.Equals(
                program.OriginalSha256,
                FragmentProgramSha256,
                StringComparison.Ordinal) ||
            program.ProgramControl != new RsxFragmentProgramControl(
                IsValid: true,
                DescriptorOffset: 0x7B0,
                RegisterCount: 4,
                ExportPrecisionRaw: 1,
                DepthExportRaw: 0,
                ControlFlagsRaw: 0,
                EmittedControl: 0x04008400) ||
            program.SamplerFeatureProfile.Entries.Length != 1 ||
            program.SamplerFeatureProfile.Entries[0] !=
                new RsxFragmentSamplerFeature(
                    RsxSamplerDestination,
                    RsxFragmentSamplerFeatures.Shadow) ||
            program.StaticConstantPatches.Length != 1)
        {
            return false;
        }

        MapRenderStaticFragmentConstantPatch material =
            program.StaticConstantPatches[0];
        if (material.ArgumentOrdinal != 19 ||
            material.Kind != MapRenderSelectedPassConstantKind.MaterialPixel ||
            material.Destination != 15 ||
            unchecked((uint)material.ArgumentRaw) != 0x3D9994DCu ||
            material.PatchSiteCount != 3 ||
            program.DirectCodeConstantBindings.Length !=
                FragmentConstantDestinations.Length)
        {
            return false;
        }

        for (var index = 0;
             index < FragmentConstantDestinations.Length;
             index++)
        {
            RsxFragmentDirectCodeConstantBinding binding =
                program.DirectCodeConstantBindings[index];
            if (!binding.IsDirectSourceResolved ||
                binding.ArgumentOrdinal != index + 10 ||
                binding.Destination !=
                    FragmentConstantDestinations[index] ||
                binding.CodeIndex != FragmentConstantSourceRows[index] ||
                binding.ArgumentRaw !=
                    ((FragmentConstantSourceRows[index] << 16) | 1) ||
                binding.PatchSites.Length !=
                    FragmentConstantPatchSiteCounts[index])
            {
                return false;
            }
        }

        return true;
    }
}
