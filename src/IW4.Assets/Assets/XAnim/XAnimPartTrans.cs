using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.XAnim;

public sealed class XAnimPartTrans
{
    public const int SerializedSize = 0x04;

    public ushort Size { get; init; }
    public byte SmallTrans { get; init; }
    public byte Pad3 { get; init; }
    public XAnimPartTransFrame0? Frame0 { get; init; }
    public XAnimPartTransFrames? Frames { get; init; }
}
