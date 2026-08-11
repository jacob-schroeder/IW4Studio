using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.Leaderboard;

public sealed class LeaderboardDefAsset : BaseAsset
{
    public const int SerializedSize = 0x18;
    public override XAssetType SerializedAssetType => XAssetType.LeaderboardDef;

    // 0x00: XString name.
    public XString NamePointer { get; init; }
    public string? Name { get; init; }
    public override string? SerializedAssetName => Name;

    // 0x04..0x10: leaderboard identity and special-column identifiers.
    public int Id { get; init; }
    public int ColumnCount { get; init; }
    public int XpColumnId { get; init; }
    public int PrestigeColumnId { get; init; }

    // 0x14: nonzero-presence pointer to ColumnCount fixed-size rows in LARGE.
    public XPointer<LbColumnDef[]> ColumnsPointer { get; init; }
    public IReadOnlyList<LbColumnDef> Columns { get; init; } = [];
}
