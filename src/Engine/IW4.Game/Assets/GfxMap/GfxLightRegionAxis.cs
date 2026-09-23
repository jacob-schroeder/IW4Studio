using IW4.Game.Assets.Image;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.XModel;
using IW4.Game.Pointers;
using IW4.Game.Zone;

namespace IW4.Game.Assets.GfxMap;

public sealed class GfxLightRegionAxis
{
    public const int SerializedSize = 0x14;

    public IReadOnlyList<float> Dir { get; init; } = [];
    public float MidPoint { get; init; }
    public float HalfSize { get; init; }
}
