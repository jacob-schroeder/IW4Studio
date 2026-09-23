using IW4.Game.Assets.Fx;
using IW4.Game.Pointers;
using IW4.Game.Zone;

namespace IW4.Game.Assets.ImpactFx;

public sealed class FxImpactTableAsset : BaseAsset
{
    public const int SerializedSize = 0x08;
    public const int EntryCount = 15;

    public override XAssetType SerializedAssetType => XAssetType.ImpactFx;
    public XPointer<string> NamePointer { get; init; }
    public string? Name { get; init; }
    public override string? SerializedAssetName => Name;
    public XPointer<FxImpactEntry[]> EntriesPointer { get; init; }
    public IReadOnlyList<FxImpactEntry> Entries { get; init; } = [];
}
