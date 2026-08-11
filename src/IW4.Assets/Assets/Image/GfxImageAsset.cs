using IW4.FastFiles.Pointers;
using IW4.FastFiles.Database.Streaming;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.Image;

public sealed class GfxImageAsset : BaseAsset
{
    public const int SerializedSize = 0x50;

    private byte _serializedPixelDataBlock;
    private uint _serializedPixelsOffset;
    private byte? _runtimePixelDataBlock;
    private uint? _runtimePixelsOffset;

    public override XAssetType SerializedAssetType => XAssetType.Image;

    // 0x00..0x03: RSX format, mip count, dimension, and cube/multiface state.
    public byte Format { get; init; }
    public byte LevelCount { get; init; }
    public byte DimensionCount { get; init; }
    public byte MultiFaceControl { get; init; }
    // 0x04: PS3 texture flags used with Format to select the storage layout.
    public uint TextureFlags { get; init; }
    // 0x08..0x0D: current image dimensions.
    public ushort Width { get; init; }
    public ushort Height { get; init; }
    public ushort Depth { get; init; }
    // 0x0E..0x0F: pixel block and copied alignment byte. Runtime registration
    // may override the effective block without changing the serialized value.
    public byte PixelDataBlock
    {
        get => _runtimePixelDataBlock ?? _serializedPixelDataBlock;
        init => _serializedPixelDataBlock = value;
    }
    /// <summary>The exact +0x0E value loaded or authored for wire output.</summary>
    public byte SerializedPixelDataBlock => _serializedPixelDataBlock;
    public byte Pad0F { get; init; }
    // 0x10..0x17: RSX pitch and pixel offset fields. Runtime registration may
    // override the effective offset without changing the serialized value.
    public uint RenderTargetPitch { get; init; }
    public uint PixelsOffset
    {
        get => _runtimePixelsOffset ?? _serializedPixelsOffset;
        init => _serializedPixelsOffset = value;
    }
    /// <summary>The exact +0x14 value loaded or authored for wire output.</summary>
    public uint SerializedPixelsOffset => _serializedPixelsOffset;
    // 0x18..0x1B: map type, texture semantic, category, and copied padding.
    public byte MapType { get; init; }
    public byte TextureSemantic { get; init; }
    public byte Category { get; init; }
    public byte Pad1B { get; init; }
    // 0x1C..0x27: card-memory and base-level image dimensions.
    public uint CardMemory { get; init; }
    public ushort BaseWidth { get; init; }
    public ushort BaseHeight { get; init; }
    public ushort BaseDepth { get; init; }
    public byte BaseLevelCount { get; init; }
    // 0x27: cached image state used by renderer release/replacement behavior.
    public byte Cached { get; init; }
    // 0x28: presence-controlled GfxImagePixels pointer.
    public XPointerReference PayloadPointer { get; init; }
    // 0x2C..0x4B: four inline GfxImageStreamData records.
    public IReadOnlyList<GfxImageStreamData> StreamData { get; init; } = [];
    public int? StreamImageIndex { get; init; }
    public IReadOnlyList<DbHeaderImageStreamEntry> StreamEntries { get; init; } = [];
    public int PayloadByteCount { get; init; }
    public IReadOnlyList<byte> PayloadBytes { get; init; } = [];
    // 0x4C: XString name pointer.
    public XPointer<string> NamePointer { get; init; }
    public string? Name { get; init; }
    public override string? SerializedAssetName => Name;

    internal void ApplyNullPayloadRuntimeHeader(uint? pixelsOffset)
    {
        _runtimePixelDataBlock = 1;
        if (pixelsOffset.HasValue)
            _runtimePixelsOffset = pixelsOffset.Value;
    }
}
