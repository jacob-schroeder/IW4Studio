using IW4.Assets.Math;

namespace IW4.Assets.Assets.GfxMap;

public sealed class GfxSurfaceBounds
{
    public const int SerializedSize = 0x20;

    // 0x00: midpoint[3] followed by nonnegative halfSize[3].
    public Bounds Bounds { get; init; } = new();

    // 0x18..0x1F: uninterpreted trailing bytes preserved losslessly.
    public IReadOnlyList<byte> Unknown18To1F { get; init; } = new byte[8];
}
