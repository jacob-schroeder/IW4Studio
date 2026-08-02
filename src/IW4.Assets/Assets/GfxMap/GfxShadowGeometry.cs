using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.GfxMap;

public sealed class GfxShadowGeometry
{
    public const int SerializedSize = 0x0C;

    public ushort SurfaceCount { get; init; }
    public ushort SModelCount { get; init; }
    public XPointer<ushort[]> SortedSurfIndexPointer { get; init; }
    public IReadOnlyList<ushort> SortedSurfIndex { get; init; } = [];
    public XPointer<ushort[]> SModelIndexPointer { get; init; }
    public IReadOnlyList<ushort> SModelIndex { get; init; } = [];
}
