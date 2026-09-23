using IW4.Game.Pointers;

namespace IW4.Game.Assets.XAnim;

public sealed class LargeXAnimTransFramePayload : XAnimTransFramePayload
{
    public IReadOnlyList<LargeXAnimTransFrame> Frames { get; init; } = [];
}
