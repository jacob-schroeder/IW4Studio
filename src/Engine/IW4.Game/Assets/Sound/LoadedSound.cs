using IW4.Game.Pointers;
using IW4.Game.Zone;

namespace IW4.Game.Assets.Sound;

public sealed class LoadedSound : BaseAsset
{
    public const int SerializedSize = 0x1C;
    public override XAssetType SerializedAssetType => XAssetType.LoadedSound;

    public XPointer<string> NamePointer { get; init; }
    public string? Name { get; init; }
    public override string? SerializedAssetName => Name;
    public int PhysicalDataByteCount { get; init; }
    /// <summary>Playback duration in milliseconds.</summary>
    public ushort DurationMilliseconds { get; init; }
    public ushort ChannelCount { get; init; }
    public ushort SampleRate { get; init; }
    /// <summary>Decoded sample frames per channel skipped on the first read.</summary>
    public ushort InitialSkipSamples { get; init; }
    /// <summary>Trailing decoded sample frames per channel excluded from the authored duration.</summary>
    public ushort EndTrimSamples { get; init; }
    public ushort SeekTableCount { get; init; }
    public int SeekTableByteCount => checked(SeekTableCount * sizeof(uint));
    public XPointer<byte[]> SeekTablePointer { get; init; }
    public byte[]? SeekTable { get; init; }
    public XPointer<byte[]> PhysicalDataPointer { get; init; }
    public byte[]? PhysicalData { get; init; }
}
