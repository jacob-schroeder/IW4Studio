using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.StructuredData;

public sealed class StructuredDataDefSetAsset : BaseAsset
{
    public const int SerializedSize = 0x0c;

    public XString NamePointer { get; init; }
    public string? Name { get; init; }
    public int DefCount { get; init; }
    public XPointer<StructuredDataDef[]> DefsPointer { get; init; }
    public IReadOnlyList<StructuredDataDef> Defs { get; init; } = [];
}
