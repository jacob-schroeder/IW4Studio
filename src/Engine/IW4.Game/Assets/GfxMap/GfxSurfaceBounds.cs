using IW4.Game.Math;

namespace IW4.Game.Assets.GfxMap;

public sealed class GfxSurfaceBounds
{
    public const int SerializedSize = 0x20;
    public const int LayerSlotCount = 5;

    // 0x00: midpoint[3] followed by nonnegative halfSize[3].
    public Bounds Bounds { get; init; } = new();

    // 0x18: BSP streaming radius in world units around Bounds.MidPoint.
    public ushort MipRadius { get; init; }

    // 0x1A..0x1E: unsigned weights indexed by the surface material's layer order.
    public IReadOnlyList<byte> InvHighMipRadius { get; init; } = new byte[LayerSlotCount];

    // 0x1F: preserved tail byte; no PS3 consumer meaning is established.
    public byte TailPadding { get; init; }
}
