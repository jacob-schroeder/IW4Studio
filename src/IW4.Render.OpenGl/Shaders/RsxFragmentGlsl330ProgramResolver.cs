using System.Collections.Immutable;

using IW4.Render.Execution;
using IW4.Render.Shaders;

namespace IW4.Render.OpenGl.Shaders;

/// <summary>
/// Render-thread-owned, exact-IR cache for OpenGL fragment lowering.
/// Executable GLSL is produced solely from the immutable RSX IR retained by
/// the shared contract.
/// </summary>
internal sealed class RsxFragmentGlsl330ProgramResolver
{
    private static readonly RsxFragmentGlsl330ProgramResolution
        CoreProgramIrNotReady =
            RsxFragmentGlsl330ProgramResolution.Failure(
                RsxFragmentGlsl330ProgramFailureKind.CoreProgramIrNotReady,
                "OPENGL_FRAGMENT_CORE_PROGRAM_IR_NOT_READY",
                ImmutableArray<string>.Empty);

    private static readonly RsxFragmentGlsl330ProgramResolution IrMissing =
        RsxFragmentGlsl330ProgramResolution.Failure(
            RsxFragmentGlsl330ProgramFailureKind.ProgramIrMissing,
            "OPENGL_FRAGMENT_PROGRAM_IR_MISSING",
            ImmutableArray<string>.Empty);

    // Identity is a cheap bucket prefilter only. It intentionally does not
    // decide cache equality: internally constructible IR can retain the same
    // semantic identity while changing control or decoded instruction state.
    private readonly Dictionary<string, CacheEntry> _buckets =
        new(StringComparer.Ordinal);

    private int _count;

    /// <summary>
    /// Number of cold exact-IR lowerings. Cache hits do not advance this value.
    /// </summary>
    internal int LoweringCount { get; private set; }

    internal int Count => _count;

    /// <summary>
    /// Logical bytes copied into exact-key payloads, excluding CLR array and
    /// object headers. Each entry retains 14 fixed bytes, 41 bytes per decoded
    /// instruction, and 8 bytes per sampler-profile entry.
    /// </summary>
    internal long RetainedKeyPayloadByteCount { get; private set; }

    internal RsxFragmentGlsl330ProgramResolution Resolve(
        MapRenderShaderExecutionContract execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        RsxFragmentGlsl330ProgramResolution fragment = Resolve(
            execution.FragmentProgramIr);
        if (!fragment.IsReady)
            return fragment;

        return execution.ProgramIrReady
            ? fragment
            : CoreProgramIrNotReady;
    }

    internal RsxFragmentGlsl330ProgramResolution Resolve(
        RsxFragmentProgramIr? program)
    {
        if (program is null)
            return IrMissing;

        CacheEntry? entry = null;
        if (_buckets.TryGetValue(program.Identity, out CacheEntry? candidate))
        {
            for (; candidate is not null; candidate = candidate.Next)
            {
                if (candidate.Key.Matches(program))
                {
                    entry = candidate;
                    break;
                }
            }
        }
        if (entry is null)
        {
            _buckets.TryGetValue(program.Identity, out CacheEntry? head);
            entry = CreateEntry(program, head);
            _buckets[program.Identity] = entry;
            _count = checked(_count + 1);
            RetainedKeyPayloadByteCount = checked(
                RetainedKeyPayloadByteCount + entry.Key.LogicalByteCount);
        }

        return entry.Lowered;
    }

    private CacheEntry CreateEntry(
        RsxFragmentProgramIr program,
        CacheEntry? next)
    {
        LoweringCount = checked(LoweringCount + 1);
        var key = new ExactLoweringKeySnapshot(program);
        RsxFragmentGlsl330LoweringResult lowering =
            RsxFragmentGlsl330Lowerer.Lower(program);
        if (!lowering.TranslationReady || lowering.Glsl is null)
        {
            string reason =
                $"OPENGL_FRAGMENT_GLSL330_LOWERING_BLOCKED:{program.Identity}:" +
                string.Join('|', lowering.Blockers);
            RsxFragmentGlsl330ProgramResolution blocked =
                RsxFragmentGlsl330ProgramResolution.Failure(
                    RsxFragmentGlsl330ProgramFailureKind.LoweringBlocked,
                    reason,
                    lowering.Blockers);
            return new CacheEntry(
                key,
                blocked,
                next);
        }

        RsxFragmentGlsl330ProgramResolution lowered =
            RsxFragmentGlsl330ProgramResolution.Success(lowering.Glsl);
        return new CacheEntry(
            key,
            lowered,
            next);
    }

    private sealed record CacheEntry(
        ExactLoweringKeySnapshot Key,
        RsxFragmentGlsl330ProgramResolution Lowered,
        CacheEntry? Next);

    /// <summary>
    /// Owned exact snapshot of every IR value read by the fragment lowerer.
    /// No digest participates in equality. Raw decoded words cover all
    /// destination/source-derived semantics; opcode, direct-constant binding,
    /// and inline-constant bits are retained separately because internal IR
    /// construction can make them diverge from those words. Authored/effective
    /// byte provenance, patch records, and precomputed color-export summaries
    /// are deliberately absent because this lowerer never reads them.
    /// </summary>
    private sealed class ExactLoweringKeySnapshot
    {
        private const int FixedLogicalByteCount =
            sizeof(byte) +
            sizeof(byte) +
            sizeof(uint) +
            (4 * sizeof(byte)) +
            sizeof(uint);
        private const int InstructionLogicalByteCount =
            sizeof(int) +
            (4 * sizeof(uint)) +
            sizeof(byte) +
            sizeof(byte) +
            sizeof(ushort) +
            sizeof(byte) +
            (4 * sizeof(uint));
        private const int SamplerFeatureLogicalByteCount =
            sizeof(int) + sizeof(int);

        private readonly bool _hasValidUpload;
        private readonly RsxFragmentProgramControl _programControl;
        private readonly InstructionSnapshot[] _instructions;
        private readonly SamplerFeatureSnapshot[] _samplerFeatures;

        internal ExactLoweringKeySnapshot(RsxFragmentProgramIr program)
        {
            _hasValidUpload = program.HasValidUpload;
            _programControl = program.ProgramControl;
            _instructions = new InstructionSnapshot[
                program.Instructions.Length];
            for (int index = 0; index < _instructions.Length; index++)
            {
                _instructions[index] = new InstructionSnapshot(
                    program.Instructions[index]);
            }

            ImmutableArray<RsxFragmentSamplerFeature> entries =
                program.SamplerFeatureProfile.Entries;
            _samplerFeatures = new SamplerFeatureSnapshot[entries.Length];
            for (int index = 0; index < _samplerFeatures.Length; index++)
            {
                _samplerFeatures[index] = new SamplerFeatureSnapshot(
                    entries[index].Destination,
                    entries[index].Features);
            }

            LogicalByteCount = checked(
                FixedLogicalByteCount +
                ((long)_instructions.Length *
                 InstructionLogicalByteCount) +
                ((long)_samplerFeatures.Length *
                 SamplerFeatureLogicalByteCount));
        }

        internal long LogicalByteCount { get; }

        internal bool Matches(RsxFragmentProgramIr program)
        {
            if (_hasValidUpload != program.HasValidUpload ||
                _programControl != program.ProgramControl ||
                _instructions.Length != program.Instructions.Length)
            {
                return false;
            }

            for (int index = 0; index < _instructions.Length; index++)
            {
                if (!_instructions[index].Matches(
                        program.Instructions[index]))
                {
                    return false;
                }
            }

            ImmutableArray<RsxFragmentSamplerFeature> entries =
                program.SamplerFeatureProfile.Entries;
            if (_samplerFeatures.Length != entries.Length)
                return false;
            for (int index = 0; index < _samplerFeatures.Length; index++)
            {
                if (!_samplerFeatures[index].Matches(entries[index]))
                    return false;
            }

            return true;
        }
    }

    private readonly record struct InstructionSnapshot(
        int Index,
        uint Dst,
        uint Src0,
        uint Src1,
        uint Src2,
        byte Opcode,
        bool HasDirectCodeConstant,
        ushort DirectCodeConstantIndex,
        bool HasInlineConstant,
        uint ConstantX,
        uint ConstantY,
        uint ConstantZ,
        uint ConstantW)
    {
        internal InstructionSnapshot(RsxFragmentInstruction instruction)
            : this(
                instruction.Index,
                instruction.Dst,
                instruction.Src0,
                instruction.Src1,
                instruction.Src2,
                instruction.Opcode,
                instruction.DirectCodeConstantIndex.HasValue,
                instruction.DirectCodeConstantIndex.GetValueOrDefault(),
                instruction.Constant.HasValue,
                instruction.Constant.GetValueOrDefault().XBits,
                instruction.Constant.GetValueOrDefault().YBits,
                instruction.Constant.GetValueOrDefault().ZBits,
                instruction.Constant.GetValueOrDefault().WBits)
        {
        }

        internal bool Matches(RsxFragmentInstruction instruction)
        {
            if (Index != instruction.Index ||
                Dst != instruction.Dst ||
                Src0 != instruction.Src0 ||
                Src1 != instruction.Src1 ||
                Src2 != instruction.Src2 ||
                Opcode != instruction.Opcode ||
                HasDirectCodeConstant !=
                instruction.DirectCodeConstantIndex.HasValue ||
                DirectCodeConstantIndex !=
                instruction.DirectCodeConstantIndex.GetValueOrDefault() ||
                HasInlineConstant != instruction.Constant.HasValue)
            {
                return false;
            }

            RsxFragmentInlineConstant constant =
                instruction.Constant.GetValueOrDefault();
            return ConstantX == constant.XBits &&
                   ConstantY == constant.YBits &&
                   ConstantZ == constant.ZBits &&
                   ConstantW == constant.WBits;
        }
    }

    private readonly record struct SamplerFeatureSnapshot(
        int Destination,
        RsxFragmentSamplerFeatures Features)
    {
        internal bool Matches(RsxFragmentSamplerFeature feature) =>
            Destination == feature.Destination &&
            Features == feature.Features;
    }
}

internal enum RsxFragmentGlsl330ProgramFailureKind
{
    None,
    CoreProgramIrNotReady,
    ProgramIrMissing,
    LoweringBlocked
}

/// <summary>
/// Immutable source produced from backend-owned fragment IR
/// lowering. Authored RSX compilation paths accept this token instead of a raw
/// string so core oracle GLSL cannot accidentally become executable input.
/// Backend composition retains the token while replacing its exact source.
/// </summary>
internal sealed class MapRenderOpenGlAuthoredFragmentSource
{
    private MapRenderOpenGlAuthoredFragmentSource(string exactGlsl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exactGlsl);
        ExactGlsl = exactGlsl;
    }

    internal string ExactGlsl { get; }

    internal static MapRenderOpenGlAuthoredFragmentSource FromBackendLowering(
        string exactGlsl) =>
        new(exactGlsl);

    internal MapRenderOpenGlAuthoredFragmentSource WithBackendComposition(
        string exactGlsl) =>
        new(exactGlsl);
}

internal sealed record RsxFragmentGlsl330ProgramResolution(
    MapRenderOpenGlAuthoredFragmentSource? Source,
    bool IsReady,
    RsxFragmentGlsl330ProgramFailureKind FailureKind,
    string FailureReason,
    ImmutableArray<string> Blockers)
{
    internal static RsxFragmentGlsl330ProgramResolution Success(string glsl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(glsl);
        return new RsxFragmentGlsl330ProgramResolution(
            MapRenderOpenGlAuthoredFragmentSource.FromBackendLowering(glsl),
            IsReady: true,
            RsxFragmentGlsl330ProgramFailureKind.None,
            string.Empty,
            ImmutableArray<string>.Empty);
    }

    internal static RsxFragmentGlsl330ProgramResolution Failure(
        RsxFragmentGlsl330ProgramFailureKind failureKind,
        string failureReason,
        ImmutableArray<string> blockers)
    {
        if (failureKind == RsxFragmentGlsl330ProgramFailureKind.None)
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);
        if (blockers.IsDefault)
        {
            throw new ArgumentException(
                "Failure blockers must be initialized.",
                nameof(blockers));
        }

        return new RsxFragmentGlsl330ProgramResolution(
            Source: null,
            IsReady: false,
            failureKind,
            failureReason,
            blockers);
    }
}
