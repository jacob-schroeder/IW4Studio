namespace IW4.Render.Shaders;

/// <summary>Exact fragment-program patch-table site for one CodePixel source.</summary>
public sealed record MapRenderCodePixelConstantPatchSite
{
    public MapRenderCodePixelConstantPatchSite(
        ushort relativePatchOffset,
        int programByteOffset,
        int? instructionIndex)
    {
        if (programByteOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(programByteOffset));
        if (instructionIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(instructionIndex));

        RelativePatchOffset = relativePatchOffset;
        ProgramByteOffset = programByteOffset;
        InstructionIndex = instructionIndex;
    }

    public ushort RelativePatchOffset { get; }

    public int ProgramByteOffset { get; }

    public int? InstructionIndex { get; }
}
