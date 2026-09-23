using IW4.Game.Pointers;

namespace IW4.Game.Assets.XAnim;

public sealed class XAnimDeltaPartQuatDataFrames
{
    public const int SerializedSize = 0x04;

    public XPointer<XQuat[]> FramesPointer { get; init; }
    public int FrameCount { get; init; }
    public int DynamicIndexByteCount { get; init; }
    public XAnimDynamicFrames DynamicFrames { get; init; } = new();
    public IReadOnlyList<XQuat> Frames { get; init; } = [];
}
