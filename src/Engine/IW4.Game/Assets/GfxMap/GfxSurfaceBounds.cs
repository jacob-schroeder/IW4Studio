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

    /// <summary>
    /// Streaming weights at 0x1A..0x1E. The renderer multiplies the view-scaled
    /// distance from the surface by each layer's weight when updating material
    /// streaming priority. Unlayered surfaces use slot zero.
    /// </summary>
    /// <remarks>
    /// For each streamed image aspect ratio, sort the N usable triangle UV
    /// densities in ascending order. Stock weights use the first density at or
    /// above index (N - 1) / 5 that is at least one sixteenth of the lower median.
    /// The smallest selected density and the smallest first-mip diagonal across
    /// the layer's images determine the byte.
    /// </remarks>
    public IReadOnlyList<byte> InvHighMipRadius { get; init; } = new byte[LayerSlotCount];

    // 0x1F: preserved tail byte; no PS3 consumer meaning is established.
    public byte TailPadding { get; init; }
}
