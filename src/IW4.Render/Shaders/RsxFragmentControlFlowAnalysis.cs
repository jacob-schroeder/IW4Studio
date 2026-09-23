namespace IW4.Render.Shaders;

/// <summary>
/// Backend-neutral recognition of the fragment flow shapes supported by the
/// shader lowerers.
/// </summary>
internal static class RsxFragmentControlFlowAnalysis
{
    internal static RsxFragmentControlFlow? TryAnalyze(
        IReadOnlyList<RsxFragmentInstruction> instructions)
    {
        RsxFragmentInstruction[] flowInstructions = instructions
            .Where(instruction => instruction.IsControlFlow)
            .ToArray();
        if (flowInstructions.Length != 1 || instructions.Count == 0)
            return null;

        RsxFragmentInstruction flow = flowInstructions[0];
        if (flow.ConditionWriteRegister1 ||
            flow.CondWriteEnabled ||
            !flow.NoDest ||
            flow.WriteMask != RsxFragmentWriteMask.None ||
            flow.Saturate ||
            flow.Scale != RsxFragmentResultScale.None)
        {
            return null;
        }

        int programEndOffset = ProgramEndOffset(instructions);
        if (flow.OpcodeType == RsxFragmentOpcode.Return)
        {
            return new RsxFragmentControlFlow(
                flow.Index,
                programEndOffset,
                flow.OpcodeType);
        }
        if (flow.OpcodeType != RsxFragmentOpcode.If ||
            (flow.Src1 & 0x7fff_ffffu) != flow.Src2)
        {
            return null;
        }

        uint targetSlot = flow.Src2 >> 2;
        if (targetSlot > (uint)(int.MaxValue / 16))
            return null;
        int closeOffset = checked(
            instructions[0].Offset + (int)targetSlot * 16);
        bool targetExists = closeOffset == programEndOffset ||
            instructions.Any(instruction => instruction.Offset == closeOffset);
        if (!targetExists || closeOffset <= flow.Offset)
            return null;

        return new RsxFragmentControlFlow(
            flow.Index,
            closeOffset,
            flow.OpcodeType);
    }

    internal static int ProgramEndOffset(
        IReadOnlyList<RsxFragmentInstruction> instructions) =>
        instructions.Count == 0
            ? 0
            : instructions.Max(instruction =>
                checked(instruction.Offset + instruction.ByteCount));
}

internal readonly record struct RsxFragmentControlFlow(
    int InstructionIndex,
    int CloseOffset,
    RsxFragmentOpcode Opcode);
