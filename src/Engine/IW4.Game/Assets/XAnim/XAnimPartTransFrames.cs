using IW4.Game.Math;
using IW4.Game.Pointers;

namespace IW4.Game.Assets.XAnim;

public sealed class XAnimPartTransFrames
{
    public const int SerializedSize = 0x1c;

    public Vec3 Mins { get; init; }
    public Vec3 Size { get; init; }
    public XPointer<byte[]> FramesPointer { get; init; }
    public XAnimDynamicFrames DynamicFrames { get; init; } = new();
    public XAnimTransFramePayload FramePayload { get; init; } = new EmptyXAnimTransFramePayload();
}
