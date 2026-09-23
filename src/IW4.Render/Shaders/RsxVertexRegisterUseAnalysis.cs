namespace IW4.Render.Shaders;

/// <summary>
/// Discovers the registers used by vertex slots that a backend can lower.
/// Expression support and source emission remain backend-owned.
/// </summary>
internal static class RsxVertexRegisterUseAnalysis
{
    internal static RsxVertexRegisterUsage Analyze(
        IReadOnlyList<RsxVertexInstruction> instructions,
        Func<RsxVertexInstruction, bool, bool> canLowerSlot)
    {
        var inputRegisters = new SortedSet<int>();
        var tempRegisters = new SortedSet<int>();
        var outputRegisters = new SortedSet<int>
        {
            // Fixed translated-shader exports. O[0] is native clip position;
            // O[1]/O[2] are colors; O[7]..O[14] are texture coordinates.
            (byte)RsxVertexResult.Position,
            (byte)RsxVertexResult.FrontColor0,
            (byte)RsxVertexResult.FrontColor1,
            (byte)RsxVertexResult.TextureCoordinate0,
            (byte)RsxVertexResult.TextureCoordinate1,
            (byte)RsxVertexResult.TextureCoordinate2,
            (byte)RsxVertexResult.TextureCoordinate3,
            (byte)RsxVertexResult.TextureCoordinate4,
            (byte)RsxVertexResult.TextureCoordinate5,
            (byte)RsxVertexResult.TextureCoordinate6,
            (byte)RsxVertexResult.TextureCoordinate7
        };

        foreach (RsxVertexInstruction instruction in instructions)
        {
            if (instruction.VectorOpcode != RsxVertexVectorOpcode.Nop &&
                instruction.VectorWriteMask != RsxVertexWriteMask.None &&
                canLowerSlot(instruction, false))
            {
                RsxSourceSlotMask sourceMask =
                    RsxVertexInstruction.VectorSourceMask(
                        instruction.VectorOpcode);
                if ((sourceMask & RsxSourceSlotMask.Source0) !=
                    RsxSourceSlotMask.None)
                {
                    AddSourceRegister(
                        instruction,
                        instruction.Source0,
                        inputRegisters,
                        tempRegisters);
                }
                if ((sourceMask & RsxSourceSlotMask.Source1) !=
                    RsxSourceSlotMask.None)
                {
                    AddSourceRegister(
                        instruction,
                        instruction.Source1,
                        inputRegisters,
                        tempRegisters);
                }
                if ((sourceMask & RsxSourceSlotMask.Source2) !=
                    RsxSourceSlotMask.None)
                {
                    AddSourceRegister(
                        instruction,
                        instruction.Source2,
                        inputRegisters,
                        tempRegisters);
                }
                AddDestinationRegister(
                    instruction,
                    scalar: false,
                    tempRegisters,
                    outputRegisters);
            }

            if (instruction.ScalarOpcode != RsxVertexScalarOpcode.Nop &&
                instruction.ScalarWriteMask != RsxVertexWriteMask.None &&
                RsxVertexInstruction.ScalarReadsSource2(
                    instruction.ScalarOpcode) &&
                canLowerSlot(instruction, true))
            {
                AddSourceRegister(
                    instruction,
                    instruction.Source2,
                    inputRegisters,
                    tempRegisters);
                AddDestinationRegister(
                    instruction,
                    scalar: true,
                    tempRegisters,
                    outputRegisters);
            }
        }

        return new RsxVertexRegisterUsage(
            inputRegisters.ToArray(),
            tempRegisters.ToArray(),
            outputRegisters.ToArray());
    }

    private static void AddSourceRegister(
        RsxVertexInstruction instruction,
        uint source,
        ISet<int> inputRegisters,
        ISet<int> tempRegisters)
    {
        switch (RsxVertexInstruction.SourceRegisterKind(source))
        {
            case RsxVertexRegisterType.Temporary:
                tempRegisters.Add((int)((source >> 2) & 0x3f));
                break;
            case RsxVertexRegisterType.Input:
                inputRegisters.Add((byte)instruction.InputAttribute);
                break;
        }
    }

    private static void AddDestinationRegister(
        RsxVertexInstruction instruction,
        bool scalar,
        ISet<int> tempRegisters,
        ISet<int> outputRegisters)
    {
        bool writesOutput = scalar
            ? instruction.ScaResult
            : instruction.VecResult;
        if (writesOutput && instruction.Result != RsxVertexResult.None)
            outputRegisters.Add((byte)instruction.Result);

        int temp = scalar
            ? instruction.ScaDestTemp
            : instruction.VecDestTemp;
        if (temp != 0x3f)
            tempRegisters.Add(temp);
    }
}

internal readonly record struct RsxVertexRegisterUsage(
    int[] InputRegisters,
    int[] TempRegisters,
    int[] OutputRegisters);
