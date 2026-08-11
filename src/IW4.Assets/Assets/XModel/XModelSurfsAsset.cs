using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.XModel;

public sealed class XModelSurfsAsset : BaseAsset
{
    public const int SerializedSize = 0x24;
    public override XAssetType SerializedAssetType => XAssetType.XModelSurfs;

    public XPointer<string> NamePointer { get; init; }
    public string? Name { get; init; }
    public override string? SerializedAssetName => Name;
    public XPointer<byte[]> SurfsPointer { get; init; }
    public ushort NumSurfs { get; init; }
    public ushort Pad0A { get; init; }
    public IReadOnlyList<uint> PartBits { get; init; } = [];
    public IReadOnlyList<XSurface> Surfaces { get; init; } = [];
}
