using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.StructuredData;

public sealed class StructuredDataDefSetAsset : BaseAsset
{
    public const int SerializedSize = 0x0c;
    public override XAssetType SerializedAssetType => XAssetType.StructuredDataDef;

    public XString NamePointer { get; init; }
    public string? Name { get; init; }
    public override string? SerializedAssetName => Name;
    public int DefCount { get; init; }
    public XPointer<StructuredDataDef[]> DefsPointer { get; init; }
    public IReadOnlyList<StructuredDataDef> Defs { get; init; } = [];
}
