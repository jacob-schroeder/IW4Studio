using IW4.Game.Pointers;

namespace IW4.Game.Assets.XAnim;

public sealed class XAnimFrameIndexStream
{
    public IReadOnlyList<ushort> FrameIndices { get; init; } = [];
    public int EncodedByteCount { get; init; }
    public bool IsByteEncoded { get; init; }
}
