namespace IW4.Assets.Assets.GfxMap;

public sealed record GfxPortalPlane(
    float NormalX,
    float NormalY,
    float NormalZ,
    float Distance)
{
    public const int SerializedSize = 0x10;
}
