namespace IW4.Assets.Assets.FxMap;

public sealed record FxGlassPiecePlace(FxSpatialFrame Frame, float Radius, uint NextFree)
{
    public const int SerializedSize = 0x20;
}
