using IW4.Assets.Assets;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.TechniqueSet;

public sealed class MaterialTechniqueSetAsset : BaseAsset
{
    public const int SerializedSize = 0x9c;
    public override XAssetType SerializedAssetType => XAssetType.Techset;

    // 0x00: XString name loaded in the LARGE block.
    public XString NamePointer { get; init; }
    public string? Name { get; init; }
    public override string? SerializedAssetName => Name;

    // 0x04: byte world vertex format; 0x05..0x07 are alignment padding.
    public MaterialWorldVertexFormat WorldVertexFormat { get; init; }

    // 0x08: 37 MaterialTechnique pointer cells, ending at root + 0x98.
    public IReadOnlyList<MaterialTechniqueSlot> TechniqueSlots { get; init; } = [];
}
