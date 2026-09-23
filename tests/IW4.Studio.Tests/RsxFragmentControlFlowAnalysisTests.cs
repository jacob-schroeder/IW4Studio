using IW4.Render.OpenGl.Shaders;
using IW4.Render.Shaders;
using Xunit;

namespace IW4.Studio.Tests;

public sealed class RsxFragmentControlFlowAnalysisTests
{
    [Fact]
    public void Return_closes_at_program_end()
    {
        RsxFragmentInstruction[] instructions =
        [
            Flow(0, 0, RsxFragmentOpcode.Return),
            Nop(1, 16)
        ];

        RsxFragmentControlFlow? flow =
            RsxFragmentControlFlowAnalysis.TryAnalyze(instructions);

        Assert.Equal(
            new RsxFragmentControlFlow(0, 32, RsxFragmentOpcode.Return),
            flow);
    }

    [Fact]
    public void Supported_return_keeps_conditional_glsl_without_flow_blocker()
    {
        RsxFragmentInstruction[] instructions =
        [
            Flow(0, 0, RsxFragmentOpcode.Return),
            Nop(1, 16)
        ];
        var blockers = new SortedSet<string>(StringComparer.Ordinal);

        string glsl = RsxFragmentGlsl330Lowerer.BuildGlsl(
            instructions,
            blockers,
            new HashSet<int>(),
            new HashSet<int>());

        Assert.Contains("  if (!(rsxCcTestFL(", glsl);
        Assert.Contains("  }\n  FragColor = ", glsl);
        Assert.DoesNotContain("fragmentBranchControlFlow=unlowered", blockers);
    }

    [Theory]
    [InlineData(8u, 32)]
    [InlineData(12u, 48)]
    public void Forward_if_closes_at_existing_instruction_or_program_end(
        uint encodedTarget,
        int expectedCloseOffset)
    {
        RsxFragmentInstruction[] instructions =
        [
            Flow(0, 0, RsxFragmentOpcode.If, encodedTarget),
            Nop(1, 16),
            Nop(2, 32)
        ];

        RsxFragmentControlFlow? flow =
            RsxFragmentControlFlowAnalysis.TryAnalyze(instructions);

        Assert.Equal(
            new RsxFragmentControlFlow(
                0,
                expectedCloseOffset,
                RsxFragmentOpcode.If),
            flow);
    }

    [Theory]
    [InlineData(0u, 0u)]
    [InlineData(4u, 8u)]
    [InlineData(16u, 16u)]
    public void Unsupported_if_keeps_existing_unlowered_blocker(
        uint source1,
        uint source2)
    {
        RsxFragmentInstruction[] instructions =
        [
            Flow(0, 0, RsxFragmentOpcode.If, source2) with
            {
                Src1 = source1
            },
            Nop(1, 16)
        ];
        var blockers = new SortedSet<string>(StringComparer.Ordinal);

        string glsl = RsxFragmentGlsl330Lowerer.BuildGlsl(
            instructions,
            blockers,
            new HashSet<int>(),
            new HashSet<int>());

        Assert.Null(RsxFragmentControlFlowAnalysis.TryAnalyze(instructions));
        Assert.Contains("fragmentBranchControlFlow=unlowered", blockers);
        Assert.Contains(
            "// control-flow instruction; no behavior invented",
            glsl);
    }

    [Fact]
    public void Flow_with_condition_write_is_not_lowered()
    {
        RsxFragmentInstruction[] instructions =
        [
            Flow(0, 0, RsxFragmentOpcode.Return) with
            {
                Dst = 0x4000_0100u
            }
        ];

        Assert.Null(RsxFragmentControlFlowAnalysis.TryAnalyze(instructions));
    }

    [Fact]
    public void Multiple_flow_instructions_are_not_lowered()
    {
        RsxFragmentInstruction[] instructions =
        [
            Flow(0, 0, RsxFragmentOpcode.Return),
            Flow(1, 16, RsxFragmentOpcode.Return)
        ];

        Assert.Null(RsxFragmentControlFlowAnalysis.TryAnalyze(instructions));
    }

    [Fact]
    public void If_target_offset_uses_checked_arithmetic()
    {
        RsxFragmentInstruction[] instructions =
        [
            Flow(0, int.MaxValue - 8, RsxFragmentOpcode.If, 4) with
            {
                ByteCount = 0
            }
        ];

        Assert.Throws<OverflowException>(() =>
            RsxFragmentControlFlowAnalysis.TryAnalyze(instructions));
    }

    private static RsxFragmentInstruction Flow(
        int index,
        int offset,
        RsxFragmentOpcode opcode,
        uint encodedTarget = 0) =>
        new(
            index,
            offset,
            0x4000_0000u,
            0,
            encodedTarget,
            encodedTarget,
            opcode,
            16,
            null);

    private static RsxFragmentInstruction Nop(int index, int offset) =>
        new(index, offset, 0, 0, 0, 0, RsxFragmentOpcode.Nop, 16, null);
}
