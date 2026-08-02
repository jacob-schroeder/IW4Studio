using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.XAnim;

public sealed class XAnimFrameIndexStream
{
    public IReadOnlyList<ushort> FrameIndices { get; init; } = [];
    public int EncodedByteCount { get; init; }
    public bool IsByteEncoded { get; init; }
}
