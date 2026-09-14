namespace IW4.Assets.Assets.GfxMap;

public sealed record GfxLightGridColors(IReadOnlyList<byte> RgbBytes)
{
    public const int SerializedSize = 0xA8;
}
