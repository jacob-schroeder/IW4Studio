using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.GameMap;

public sealed class GameWorldSpAsset : BaseAsset
{
    public const int SerializedSize = 0x38;

    public override XAssetType SerializedAssetType => XAssetType.GameMapSp;

    // 0x00: XString name resolved in LARGE.
    public XPointer<string> NamePointer { get; init; }
    public string? Name { get; init; }
    public override string? SerializedAssetName => Name;

    // 0x04: embedded 0x28-byte PathData.
    public PathData Path { get; init; } = new();

    // 0x2C: embedded 0x08-byte VehicleTrack.
    public VehicleTrack VehicleTrack { get; init; } = new();

    // 0x34: G_GlassData*. PS3 allocates the 0x80-byte body when non-null.
    public XPointer<GGlassData> GlassDataPointer { get; init; }
    public GGlassData? GlassData { get; init; }
}
