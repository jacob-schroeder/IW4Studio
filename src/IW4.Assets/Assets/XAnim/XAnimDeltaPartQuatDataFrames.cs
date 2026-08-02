using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.XAnim;

public sealed class XAnimDeltaPartQuatDataFrames
{
    public const int SerializedSize = 0x04;

    public XPointer<XQuat[]> FramesPointer { get; init; }
    public int FrameCount { get; init; }
    public int DynamicIndexByteCount { get; init; }
    public XAnimDynamicFrames DynamicFrames { get; init; } = new();
    public IReadOnlyList<XQuat> Frames { get; init; } = [];
}
