using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.GfxMap;

public sealed record DpvsPlane(
    float NormalX,
    float NormalY,
    float NormalZ,
    float Distance,
    byte Type,
    byte SignBits,
    ushort Pad12)
{
    public const int SerializedSize = 0x14;
}
