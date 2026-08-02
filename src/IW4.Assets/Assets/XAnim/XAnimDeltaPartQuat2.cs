using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.XAnim;

public sealed class XAnimDeltaPartQuat2
{
    public const int SerializedSize = 0x04;

    public ushort Size { get; init; }
    public byte Pad2 { get; init; }
    public byte Pad3 { get; init; }
    public XQuat2? Frame0 { get; init; }
    public XAnimDeltaPartQuatDataFrames2? Frames { get; init; }
}
