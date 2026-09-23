using IW4.Game.Assets.Image;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.XModel;
using IW4.Game.Pointers;
using IW4.Game.Zone;

namespace IW4.Game.Assets.GfxMap;

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
