using IW4.Game.Pointers;

namespace IW4.Game.Assets.XAnim;

public sealed class XAnimPartTransFrames
{
    public const int SerializedSize = 0x1c;

    public XAnimVec3 Mins { get; init; } = new(0, 0, 0);
    public XAnimVec3 Size { get; init; } = new(0, 0, 0);
    public XPointer<byte[]> FramesPointer { get; init; }
    public XAnimDynamicFrames DynamicFrames { get; init; } = new();
    public XAnimTransFramePayload FramePayload { get; init; } = new EmptyXAnimTransFramePayload();
}
