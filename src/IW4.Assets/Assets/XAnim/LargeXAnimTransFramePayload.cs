using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.XAnim;

public sealed class LargeXAnimTransFramePayload : XAnimTransFramePayload
{
    public IReadOnlyList<LargeXAnimTransFrame> Frames { get; init; } = [];
}
