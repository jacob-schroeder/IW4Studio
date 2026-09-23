using IW4.Game.Assets.Image;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.XModel;
using IW4.Game.Pointers;
using IW4.Game.Zone;

namespace IW4.Game.Assets.GfxMap;

public sealed class GfxLightRegionHull
{
    public const int SerializedSize = 0x50;

    public IReadOnlyList<float> KdopMidPoint { get; init; } = [];
    public IReadOnlyList<float> KdopHalfSize { get; init; } = [];
    public uint AxisCount { get; init; }
    public XPointer<GfxLightRegionAxis[]> AxesPointer { get; init; }
    public IReadOnlyList<GfxLightRegionAxis> Axes { get; init; } = [];
}
