using System.Collections.Immutable;

using IW4.Render.Execution;
using IW4.Render.Shaders;

namespace IW4.Render.OpenGl.Shaders;

/// <summary>
/// Render-thread-owned cache for OpenGL vertex lowering. Executable GLSL is
/// produced solely from the immutable RSX IR retained by the shared contract.
/// </summary>
internal sealed class RsxVertexGlsl330ProgramResolver
{
    private static readonly RsxVertexGlsl330ProgramResolution
        CoreProgramIrNotReady =
            RsxVertexGlsl330ProgramResolution.Failure(
                RsxVertexGlsl330ProgramFailureKind.CoreProgramIrNotReady,
                "OPENGL_VERTEX_CORE_PROGRAM_IR_NOT_READY",
                ImmutableArray<string>.Empty);

    private static readonly RsxVertexGlsl330ProgramResolution IrMissing =
        RsxVertexGlsl330ProgramResolution.Failure(
            RsxVertexGlsl330ProgramFailureKind.ProgramIrMissing,
            "OPENGL_VERTEX_PROGRAM_IR_MISSING",
            ImmutableArray<string>.Empty);

    // Identity is a bucket prefilter only. Internally constructible IR can
    // retain an authored-byte identity while changing decoded instructions.
    private readonly Dictionary<string, CacheEntry> _buckets =
        new(StringComparer.Ordinal);

    private int _count;

    /// <summary>
    /// Number of cold IR lowerings. Cache hits do not advance this value.
    /// </summary>
    internal int LoweringCount { get; private set; }

    internal int Count => _count;

    internal RsxVertexGlsl330ProgramResolution Resolve(
        ShaderExecutionContract execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        RsxVertexGlsl330ProgramResolution vertex = Resolve(
            execution.VertexProgramIr);
        if (!vertex.IsReady)
            return vertex;

        return execution.ProgramIrReady
            ? vertex
            : CoreProgramIrNotReady;
    }

    internal RsxVertexGlsl330ProgramResolution Resolve(
        RsxVertexProgramIr? program)
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
        }

        return entry.Lowered;
    }

    private CacheEntry CreateEntry(
        RsxVertexProgramIr program,
        CacheEntry? next)
    {
        LoweringCount = checked(LoweringCount + 1);
        var key = new ExactLoweringKeySnapshot(program);
        RsxVertexGlsl330LoweringResult lowering =
            RsxVertexGlsl330Lowerer.Lower(program);
        if (!lowering.TranslationReady || lowering.Glsl is null)
        {
            string reason =
                $"OPENGL_VERTEX_GLSL330_LOWERING_BLOCKED:{program.Identity}:" +
                string.Join('|', lowering.Blockers);
            RsxVertexGlsl330ProgramResolution blocked =
                RsxVertexGlsl330ProgramResolution.Failure(
                    RsxVertexGlsl330ProgramFailureKind.LoweringBlocked,
                    reason,
                    lowering.Blockers);
            return new CacheEntry(key, blocked, next);
        }

        RsxVertexGlsl330ProgramResolution lowered =
            RsxVertexGlsl330ProgramResolution.Success(lowering.Glsl);
        return new CacheEntry(key, lowered, next);
    }

    private sealed record CacheEntry(
        ExactLoweringKeySnapshot Key,
        RsxVertexGlsl330ProgramResolution Lowered,
        CacheEntry? Next);

    /// <summary>
    /// Owned exact snapshot of every vertex-IR value consumed by the lowerer.
    /// No digest participates in equality.
    /// </summary>
    private sealed class ExactLoweringKeySnapshot
    {
        private readonly bool _hasValidUpload;
        private readonly InstructionSnapshot[] _instructions;

        internal ExactLoweringKeySnapshot(RsxVertexProgramIr program)
        {
            _hasValidUpload = program.HasValidUpload;
            _instructions = new InstructionSnapshot[
                program.Instructions.Length];
            for (int index = 0; index < _instructions.Length; index++)
            {
                _instructions[index] = new InstructionSnapshot(
                    program.Instructions[index]);
            }
        }

        internal bool Matches(RsxVertexProgramIr program)
        {
            if (_hasValidUpload != program.HasValidUpload ||
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

            return true;
        }
    }

    private readonly record struct InstructionSnapshot(
        int Index,
        int Offset,
        uint Word0,
        uint Word1,
        uint Word2,
        uint Word3)
    {
        internal InstructionSnapshot(RsxVertexInstruction instruction)
            : this(
                instruction.Index,
                instruction.Offset,
                instruction.Word0,
                instruction.Word1,
                instruction.Word2,
                instruction.Word3)
        {
        }

        internal bool Matches(RsxVertexInstruction instruction) =>
            Index == instruction.Index &&
            Offset == instruction.Offset &&
            Word0 == instruction.Word0 &&
            Word1 == instruction.Word1 &&
            Word2 == instruction.Word2 &&
            Word3 == instruction.Word3;
    }
}

internal enum RsxVertexGlsl330ProgramFailureKind
{
    None,
    CoreProgramIrNotReady,
    ProgramIrMissing,
    LoweringBlocked
}

internal sealed record RsxVertexGlsl330ProgramResolution(
    string? Glsl,
    bool IsReady,
    RsxVertexGlsl330ProgramFailureKind FailureKind,
    string FailureReason,
    ImmutableArray<string> Blockers)
{
    internal static RsxVertexGlsl330ProgramResolution Success(string glsl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(glsl);
        return new RsxVertexGlsl330ProgramResolution(
            glsl,
            IsReady: true,
            RsxVertexGlsl330ProgramFailureKind.None,
            string.Empty,
            ImmutableArray<string>.Empty);
    }

    internal static RsxVertexGlsl330ProgramResolution Failure(
        RsxVertexGlsl330ProgramFailureKind failureKind,
        string failureReason,
        ImmutableArray<string> blockers)
    {
        if (failureKind == RsxVertexGlsl330ProgramFailureKind.None)
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);
        if (blockers.IsDefault)
        {
            throw new ArgumentException(
                "Failure blockers must be initialized.",
                nameof(blockers));
        }

        return new RsxVertexGlsl330ProgramResolution(
            Glsl: null,
            IsReady: false,
            failureKind,
            failureReason,
            blockers);
    }
}
