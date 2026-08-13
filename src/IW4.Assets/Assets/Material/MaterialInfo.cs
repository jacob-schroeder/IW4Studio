using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.Material;

public sealed class MaterialInfo
{
    public const int SerializedSize = 0x18;

    public XString NamePointer { get; init; }
    public string? Name { get; init; }
    public MaterialGameFlags GameFlags { get; init; }
    public MaterialSortKey SortKey { get; init; }
    public byte TextureAtlasRowCount { get; init; }
    public byte TextureAtlasColumnCount { get; init; }
    // Serialized as part of MaterialInfo, then rebuilt after the global
    // material sort completes.
    public GfxDrawSurf DrawSurf { get; set; }
    public MaterialSurfaceTypeBits SurfaceTypeBits { get; init; }
    public ushort HashIndex { get; init; }
    public ushort Pad16 { get; init; }
}
