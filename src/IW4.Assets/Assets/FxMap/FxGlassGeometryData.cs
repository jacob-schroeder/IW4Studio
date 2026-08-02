namespace IW4.Assets.Assets.FxMap;

public readonly record struct FxGlassGeometryData(uint PackedValue)
{
    public const int SerializedSize = 0x04;

    // Packed union used as vertex, hole, crack, and fan geometry data.
}
