using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.MapEnts;

public sealed class AddonMapEntsAsset : BaseAsset
{
    public const int SerializedSize = 0x24;

    public override XAssetType SerializedAssetType => XAssetType.AddonMapEnts;

    // 0x00: XString name resolved in LARGE.
    public XPointer<string> NamePointer { get; init; }
    public string? Name { get; init; }
    public override string? SerializedAssetName => Name;

    // 0x04: entity-string byte payload; 0x08 supplies its exact byte count.
    public XPointer<byte[]> EntityStringPointer { get; init; }
    public IReadOnlyList<byte> EntityStringBytes { get; init; } = [];
    public string? EntityString { get; init; }
    public int NumEntityChars { get; init; }

    // 0x0C: embedded 0x18-byte MapTriggers header.
    public MapTriggers Trigger { get; init; } = new();
}
