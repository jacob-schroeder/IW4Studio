using IW4.Game.Pointers;
using IW4.Game.Zone;

namespace IW4.Game.Assets.Localize;

public sealed class LocalizeAsset : BaseAsset
{
    public const int SerializedSize = 0x08;
    public override XAssetType SerializedAssetType => XAssetType.Localize;

    public XString ValuePointer { get; init; }
    public string? Value { get; init; }
    public XString NamePointer { get; init; }
    public string? Name { get; init; }
    public override string? SerializedAssetName => Name;
}
