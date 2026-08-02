using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.MapEnts;

public sealed class MapEntsAsset : BaseAsset
{
    public const int SerializedSize = 0x2C;

    public XAssetType Type => XAssetType.MapEnts;

    // 0x00: XString name, materialized in LARGE.
    public XPointer<string> NamePointer { get; init; }
    public string? Name { get; init; }

    // 0x04: entity-string byte payload; 0x08 supplies its exact byte count.
    public XPointer<byte[]> EntityStringPointer { get; init; }
    public IReadOnlyList<byte> EntityStringBytes { get; init; } = [];
    public string? EntityString { get; init; }
    public int NumEntityChars { get; init; }

    // 0x0C: embedded 0x18-byte MapTriggers header.
    public MapTriggers Trigger { get; init; } = new();

    // 0x24: Stage array pointer; 0x28 is the one-byte element count.
    public XPointer<Stage[]> StagesPointer { get; init; }
    public IReadOnlyList<Stage> Stages { get; init; } = [];
    public byte StageCount { get; init; }
    public IReadOnlyList<byte> Pad29To2B { get; init; } = [];
}
