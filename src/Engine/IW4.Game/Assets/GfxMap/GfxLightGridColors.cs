namespace IW4.Game.Assets.GfxMap;

public sealed record GfxLightGridColors(IReadOnlyList<byte> RgbBytes)
{
    public const int SerializedSize = 0xA8;
}
