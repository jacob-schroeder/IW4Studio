using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Leaderboard;

public sealed class LbColumnDef
{
    public const int SerializedSize = 0x20;

    // 0x00: XString display name.
    public XString NamePointer { get; init; }
    public string? Name { get; init; }

    // 0x04..0x0C: column identity, property, and visibility.
    public int Id { get; init; }
    public int PropertyId { get; init; }
    public byte HiddenRaw { get; init; }
    public bool Hidden => HiddenRaw != 0;
    public byte[] Pad0DTo0F { get; init; } = [];

    // 0x10: XString stat name.
    public XString StatNamePointer { get; init; }
    public string? StatName { get; init; }

    // 0x14..0x1C: display type, precision, and aggregation.
    public LbColType Type { get; init; }
    public int Precision { get; init; }
    public LbAggType Aggregation { get; init; }
}
