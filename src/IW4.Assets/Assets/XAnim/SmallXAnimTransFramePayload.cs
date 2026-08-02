using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.XAnim;

public sealed class SmallXAnimTransFramePayload : XAnimTransFramePayload
{
    public IReadOnlyList<SmallXAnimTransFrame> Frames { get; init; } = [];
}
