using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.XAnim;

public sealed class XAnimDynamicFrames
{
    public IReadOnlyList<ushort> FrameIndices { get; init; } = [];
    public int EncodedByteCount { get; init; }
}
