using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.RawFile;

public sealed class RawFileAsset : BaseAsset
{
    public const int SerializedSize = 0x10;
    public override XAssetType SerializedAssetType => XAssetType.RawFile;

    // 0x00: XString asset name.
    public XString NamePointer { get; init; }
    public string? Name { get; init; }
    public override string? SerializedAssetName => Name;

    // 0x04: compressed byte count. When nonzero, the body consumes this many bytes.
    public int CompressedLen { get; init; }

    // 0x08: uncompressed byte count. When CompressedLen is zero, the body consumes Len + 1 bytes.
    public int Len { get; init; }

    // 0x0C: buffer presence/payload pointer patched to its LARGE destination before the byte copy.
    public XPointer<byte[]> BufferPointer { get; init; }
    public byte[]? Buffer { get; init; }
    public int BufferLength => CompressedLen != 0 ? CompressedLen : Len + 1;
}
