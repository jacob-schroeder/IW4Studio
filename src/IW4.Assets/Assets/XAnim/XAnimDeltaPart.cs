using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.XAnim;

public sealed class XAnimDeltaPart
{
    public const int SerializedSize = 0x0c;

    public XPointer<XAnimPartTrans> TransPointer { get; init; }
    public XAnimPartTrans? Trans { get; init; }
    public XPointer<XAnimDeltaPartQuat2> Quat2Pointer { get; init; }
    public XAnimDeltaPartQuat2? Quat2 { get; init; }
    public XPointer<XAnimDeltaPartQuat> QuatPointer { get; init; }
    public XAnimDeltaPartQuat? Quat { get; init; }
}
