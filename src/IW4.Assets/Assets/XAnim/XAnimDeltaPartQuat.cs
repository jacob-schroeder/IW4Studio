using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.XAnim;

public sealed class XAnimDeltaPartQuat
{
    public const int SerializedSize = 0x04;

    public ushort Size { get; init; }
    public byte Pad2 { get; init; }
    public byte Pad3 { get; init; }
    public XQuat? Frame0 { get; init; }
    public XAnimDeltaPartQuatDataFrames? Frames { get; init; }
}
