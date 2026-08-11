using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.XAnim;

public sealed class XAnimPartsAsset : BaseAsset
{
    public const int SerializedSize = 0x58;
    public override XAssetType SerializedAssetType => XAssetType.XAnim;

    public XPointer<string> NamePointer { get; init; }
    public string? Name { get; init; }
    public override string? SerializedAssetName => Name;
    public ushort DataByteCount { get; init; }
    public ushort DataShortCount { get; init; }
    public ushort DataIntCount { get; init; }
    public ushort RandomDataByteCount { get; init; }
    public ushort RandomDataIntCount { get; init; }
    public ushort NumFrames { get; init; }
    public byte Flags { get; init; }
    public byte DeltaFlags { get; init; }
    public IReadOnlyList<byte> BoneCounts { get; init; } = [];
    public byte BoneNameCount { get; init; }
    public byte NotifyCount { get; init; }
    public byte AssetType { get; init; }
    public byte Pad1F { get; init; }
    public int RandomDataShortCount { get; init; }
    public int IndexCount { get; init; }
    public float Framerate { get; init; }
    public float Frequency { get; init; }
    public XPointer<ushort[]> NamesPointer { get; init; }
    public IReadOnlyList<ushort> Names { get; init; } = [];
    public XPointer<byte[]> DataBytePointer { get; init; }
    public XPointer<short[]> DataShortPointer { get; init; }
    public XPointer<int[]> DataIntPointer { get; init; }
    public XPointer<short[]> RandomDataShortPointer { get; init; }
    public XPointer<byte[]> RandomDataBytePointer { get; init; }
    public XPointer<int[]> RandomDataIntPointer { get; init; }
    public XPointer<object> IndicesPointer { get; init; }
    public XAnimPackedDataStreams PackedDataStreams { get; init; } = new();
    public XAnimFrameIndexStream Indices { get; init; } = new();
    public XPointer<XAnimNotifyInfo[]> NotifyPointer { get; init; }
    public IReadOnlyList<XAnimNotifyInfo> Notify { get; init; } = [];
    public XPointer<XAnimDeltaPart> DeltaPartPointer { get; init; }
    public XAnimDeltaPart? DeltaPart { get; init; }
}
