using IW4.Game.Assets.Image;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.XModel;
using IW4.Game.Pointers;
using IW4.Game.Zone;

namespace IW4.Game.Assets.GfxMap;

public sealed class GfxLightRegion
{
    public const int SerializedSize = 0x08;

    public int HullCount { get; init; }
    public XPointer<GfxLightRegionHull[]> HullsPointer { get; init; }
    public IReadOnlyList<GfxLightRegionHull> Hulls { get; init; } = [];
}
