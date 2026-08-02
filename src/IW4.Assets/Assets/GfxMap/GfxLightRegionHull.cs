using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.GfxMap;

public sealed class GfxLightRegionHull
{
    public const int SerializedSize = 0x50;

    public IReadOnlyList<float> KdopMidPoint { get; init; } = [];
    public IReadOnlyList<float> KdopHalfSize { get; init; } = [];
    public uint AxisCount { get; init; }
    public XPointer<GfxLightRegionAxis[]> AxesPointer { get; init; }
    public IReadOnlyList<GfxLightRegionAxis> Axes { get; init; } = [];
}
