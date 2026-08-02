using IW4.Assets.Math;

namespace IW4.Assets.Assets.MapEnts;

public sealed class TriggerHull
{
    public const int SerializedSize = 0x20;

    public Bounds Bounds { get; init; } = new();
    public int Contents { get; init; }
    public ushort SlabCount { get; init; }
    public ushort FirstSlab { get; init; }
}
