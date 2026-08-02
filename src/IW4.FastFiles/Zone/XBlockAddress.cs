namespace IW4.FastFiles.Zone;

public readonly record struct XBlockAddress(XFileBlockType BlockType, int Offset)
{
    public XBlockAddress Add(int byteOffset) => new(BlockType, checked(Offset + byteOffset));

    public override string ToString() => $"{BlockType}:0x{Offset:X}";
}
