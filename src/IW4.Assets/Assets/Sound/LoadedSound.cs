using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.Sound;

public sealed class LoadedSound : BaseAsset
{
    public const int SerializedSize = 0x1C;
    public override XAssetType SerializedAssetType => XAssetType.LoadedSound;

    public XPointer<string> NamePointer { get; init; }
    public string? Name { get; init; }
    public override string? SerializedAssetName => Name;
    public int PhysicalDataByteCount { get; init; }
    public ushort FrameCount { get; init; }
    public ushort ChannelCount { get; init; }
    public ushort SampleRate { get; init; }
    public ushort Pad0E { get; init; }
    public ushort Pad10 { get; init; }
    public ushort SeekTableCount { get; init; }
    public int SeekTableByteCount => checked(SeekTableCount * sizeof(uint));
    public XPointer<byte[]> SeekTablePointer { get; init; }
    public byte[]? SeekTable { get; init; }
    public XPointer<byte[]> PhysicalDataPointer { get; init; }
    public byte[]? PhysicalData { get; init; }
}
