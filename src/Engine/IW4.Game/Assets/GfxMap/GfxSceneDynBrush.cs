using IW4.Game.Assets.Image;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.XModel;
using IW4.Game.Pointers;
using IW4.Game.Zone;

namespace IW4.Game.Assets.GfxMap;

public sealed record GfxSceneDynBrush(ushort SurfId, ushort DynEntId)
{
    public const int SerializedSize = 0x04;
}
