using IW4.Render.OpenGl.Shaders;
using IW4.Render.Shaders;
using Xunit;

namespace IW4.Studio.Tests;

public sealed class RsxVertexRegisterUseAnalysisTests
{
    [Fact]
    public void Add_reads_source_zero_and_two_and_keeps_fixed_exports_sorted()
    {
        RsxVertexInstruction instruction = VectorAdd(
            source0: 2u,
            source1: (5u << 2) | 1u,
            source2: (9u << 2) | 1u);

        RsxVertexRegisterUsage usage = RsxVertexRegisterUseAnalysis.Analyze(
            [instruction],
            (_, _) => true);

        Assert.Equal([4], usage.InputRegisters);
        Assert.Equal([9], usage.TempRegisters);
        Assert.Equal(
            [0, 1, 2, 3, 7, 8, 9, 10, 11, 12, 13, 14],
            usage.OutputRegisters);

        var blockers = new SortedSet<string>(StringComparer.Ordinal);
        string glsl = RsxVertexGlsl330Lowerer.BuildGlsl(
            [instruction],
            blockers);
        Assert.Contains("  vec4 R[10];", glsl);
        Assert.Contains("  V[4] = aRsxInput4;", glsl);
        Assert.DoesNotContain("R[5]", glsl);
    }

    [Fact]
    public void Scalar_slot_discovers_source_destination_and_output()
    {
        uint source2 = (3u << 2) | 1u;
        var instruction = new RsxVertexInstruction(
            0,
            0,
            0,
            (uint)RsxVertexScalarOpcode.Move << 27,
            source2 >> 11,
            ((source2 & 0x7ffu) << 21) |
                ((uint)RsxVertexWriteMask.All << 17) |
                0x1000u |
                (4u << 7) |
                ((uint)RsxVertexResult.BackColor1 << 2));

        RsxVertexRegisterUsage usage = RsxVertexRegisterUseAnalysis.Analyze(
            [instruction],
            (_, _) => true);

        Assert.Empty(usage.InputRegisters);
        Assert.Equal([3, 4], usage.TempRegisters);
        Assert.Contains(4, usage.OutputRegisters);
    }

    [Fact]
    public void Unsupported_slot_does_not_discover_encoded_registers()
    {
        RsxVertexInstruction instruction = VectorAdd(
            source0: 2u,
            source1: (5u << 2) | 1u,
            source2: (9u << 2) | 1u);

        RsxVertexRegisterUsage usage = RsxVertexRegisterUseAnalysis.Analyze(
            [instruction],
            (_, _) => false);

        Assert.Empty(usage.InputRegisters);
        Assert.Empty(usage.TempRegisters);
        Assert.Equal(
            [0, 1, 2, 7, 8, 9, 10, 11, 12, 13, 14],
            usage.OutputRegisters);
    }

    private static RsxVertexInstruction VectorAdd(
        uint source0,
        uint source1,
        uint source2) =>
        new(
            0,
            0,
            0x4000_0000u | (0x3fu << 15),
            ((uint)RsxVertexVectorOpcode.Add << 22) |
                ((uint)RsxVertexInputAttribute.Color1 << 8) |
                (source0 >> 9),
            ((source0 & 0x1ffu) << 23) |
                ((source1 & 0x1ffffu) << 6) |
                (source2 >> 11),
            ((source2 & 0x7ffu) << 21) |
                ((uint)RsxVertexWriteMask.All << 13) |
                ((uint)RsxVertexResult.BackColor0 << 2));
}
