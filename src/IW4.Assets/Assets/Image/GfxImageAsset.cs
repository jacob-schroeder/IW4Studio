using IW4.FastFiles.Pointers;
using IW4.FastFiles.Database.Streaming;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.Image;

public sealed class GfxImageAsset : BaseAsset
{
    public const int SerializedSize = 0x50;

    private GfxImageMemoryLocation _serializedMemoryLocation;
    private uint _serializedPixelsOffset;
    private GfxImageMemoryLocation? _runtimeMemoryLocation;
    private uint? _runtimePixelsOffset;

    public override XAssetType SerializedAssetType => XAssetType.Image;

    // 0x00..0x03: RSX format, mip count, dimension, and cube/multiface state.
    public byte Format { get; init; }
    public GfxImageFormat FormatEncoding => new(Format);
    public byte LevelCount { get; init; }
    public GfxImageDimension DimensionCount { get; init; }
    // Native CellGcmTexture::cubemap boolean. The raw byte is retained so
    // malformed or not-yet-seen values still round-trip exactly.
    public byte MultiFaceControl { get; init; }
    public bool IsCubemap => MultiFaceControl != 0;
    // 0x04: RSX SET_TEXTURE_CONTROL1 payload. Its low 24 bits also participate
    // in the native image-storage format key.
    public uint TextureControl1 { get; init; }
    public GfxImageTextureRemap TextureRemap => new(TextureControl1);
    // 0x08..0x0D: current image dimensions.
    public ushort Width { get; init; }
    public ushort Height { get; init; }
    public ushort Depth { get; init; }
    // 0x0E..0x0F: CELL_GCM memory location and minimum-LOD control. Runtime
    // registration may override the effective location without changing the
    // serialized value.
    public GfxImageMemoryLocation MemoryLocation
    {
        get => _runtimeMemoryLocation ?? _serializedMemoryLocation;
        init => _serializedMemoryLocation = value;
    }
    /// <summary>The exact +0x0E value loaded or authored for wire output.</summary>
    public GfxImageMemoryLocation SerializedMemoryLocation =>
        _serializedMemoryLocation;
    public byte MinLodControl { get; init; }
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
    // 0x18..0x1B: map type, texture semantic, category, and sRGB-read control.
    public MapType MapType { get; init; }
    public TextureSemantic TextureSemantic { get; init; }
    public ImageCategory Category { get; init; }
    // Native useSrgbReads boolean. Only bit 0 reaches the RSX gamma-read mask;
    // the exact raw byte is retained for lossless wire output.
    public byte UseSrgbReads { get; init; }
    public bool UsesSrgbReads => (UseSrgbReads & 1) != 0;
    // 0x1C..0x27: card-memory and base-level image dimensions.
    public uint CardMemory { get; init; }
    public ushort BaseWidth { get; init; }
    public ushort BaseHeight { get; init; }
    public ushort BaseDepth { get; init; }
    public byte BaseLevelCount { get; init; }
    // 0x27: cached image state used by renderer release/replacement behavior.
    public GfxImageCached Cached { get; init; }
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
        _runtimeMemoryLocation = GfxImageMemoryLocation.Main;
        if (pixelsOffset.HasValue)
            _runtimePixelsOffset = pixelsOffset.Value;
    }
}
