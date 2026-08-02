using IW4.Assets.Assets.Fx;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.ImpactFx;

public sealed class FxImpactTableAsset : BaseAsset
{
    public const int SerializedSize = 0x08;
    public const int EntryCount = 15;

    public XPointer<string> NamePointer { get; init; }
    public string? Name { get; init; }
    public XPointer<FxImpactEntry[]> EntriesPointer { get; init; }
    public IReadOnlyList<FxImpactEntry> Entries { get; init; } = [];
}
