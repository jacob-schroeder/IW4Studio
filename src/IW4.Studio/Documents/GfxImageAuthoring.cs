using IW4.Assets.Assets.Image;
using IW4.FastFiles.Database.Streaming;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Assets;

namespace IW4.Studio.Documents;

public sealed class GfxImageAuthoredSnapshot : ITargetZoneDetachedSemanticSnapshot
{
    private readonly GfxImageBuildData _data;
    internal GfxImageAuthoredSnapshot(GfxImageBuildData data) => _data = data.Copy();
    internal GfxImageBuildData Data => _data.Copy();
    public XAssetType AssetType => XAssetType.Image;
    internal static GfxImageAuthoredSnapshot Import(TargetZoneRowSource source) => source.AuthoredDefinition?.SemanticSnapshot is GfxImageAuthoredSnapshot snapshot ? snapshot : throw new InvalidDataException("GfxImage editing requires capture-time detached image source data.");
    internal static GfxImageAuthoredSnapshot FromLoaded(GfxImageAsset asset) => new(new GfxImageBuildData(asset));
}

public sealed class GfxImageDraft
{
    private GfxImageBuildData _data;
    internal GfxImageDraft(GfxImageBuildData data) => _data = data.Copy();
    public GfxImageBuildData Data => _data.Copy();
    public void ReplacePayload(byte[]? value) => _data = _data.WithPayload(value);
    internal GfxImageDraft Clone() => new(_data);
}

public sealed record GfxImageBuildData : IGfxImageBuildData
{
    private readonly GfxImageStreamBuildData[] _streamData;
    private readonly DbHeaderImageStreamEntry[] _selectedLanguageStreamEntries;
    private readonly byte[]? _payload;
    private readonly uint[] _externalStreamPackageIndices;
    internal GfxImageBuildData(GfxImageAsset asset) : this(asset.Name, asset.Format, asset.LevelCount, asset.DimensionCount, asset.MultiFaceControl, asset.TextureFlags, asset.Width, asset.Height, asset.Depth, asset.PixelDataBlock, asset.Pad0F, asset.RenderTargetPitch, asset.PixelsOffset, asset.MapType, asset.TextureSemantic, asset.Category, asset.Pad1B, asset.CardMemory, asset.BaseWidth, asset.BaseHeight, asset.BaseDepth, asset.BaseLevelCount, asset.Cached, asset.StreamData.Select(value => new GfxImageStreamBuildData(value.Width, value.Height, value.LevelSizeAndOffset)), SnapshotSelectedLanguageStreamEntries(asset), asset.StreamEntries.Where(entry => entry.FileIndex != 0).Select(entry => entry.FileIndex).Distinct(), SnapshotPayload(asset)) { }
    internal GfxImageBuildData(string? name, byte format, byte levelCount, byte dimensionCount, byte multiFaceControl, uint textureFlags, ushort width, ushort height, ushort depth, byte pixelDataBlock, byte pad0F, uint renderTargetPitch, uint pixelsOffset, byte mapType, byte textureSemantic, byte category, byte pad1B, uint cardMemory, ushort baseWidth, ushort baseHeight, ushort baseDepth, byte baseLevelCount, byte cached, IEnumerable<GfxImageStreamBuildData> streamData, IEnumerable<DbHeaderImageStreamEntry> selectedLanguageStreamEntries, IEnumerable<uint> externalStreamPackageIndices, byte[]? payload)
    {
        Name = name; Format = format; LevelCount = levelCount; DimensionCount = dimensionCount; MultiFaceControl = multiFaceControl; TextureFlags = textureFlags; Width = width; Height = height; Depth = depth; PixelDataBlock = pixelDataBlock; Pad0F = pad0F; RenderTargetPitch = renderTargetPitch; PixelsOffset = pixelsOffset; MapType = mapType; TextureSemantic = textureSemantic; Category = category; Pad1B = pad1B; CardMemory = cardMemory; BaseWidth = baseWidth; BaseHeight = baseHeight; BaseDepth = baseDepth; BaseLevelCount = baseLevelCount; Cached = cached; _streamData = streamData.ToArray(); _selectedLanguageStreamEntries = selectedLanguageStreamEntries.ToArray(); _externalStreamPackageIndices = externalStreamPackageIndices.Distinct().OrderBy(value => value).ToArray(); _payload = payload?.ToArray();
    }
    public XAssetType AssetType => XAssetType.Image; public string? Name { get; } public byte Format { get; } public byte LevelCount { get; } public byte DimensionCount { get; } public byte MultiFaceControl { get; } public uint TextureFlags { get; } public ushort Width { get; } public ushort Height { get; } public ushort Depth { get; } public byte PixelDataBlock { get; } public byte Pad0F { get; } public uint RenderTargetPitch { get; } public uint PixelsOffset { get; } public byte MapType { get; } public byte TextureSemantic { get; } public byte Category { get; } public byte Pad1B { get; } public uint CardMemory { get; } public ushort BaseWidth { get; } public ushort BaseHeight { get; } public ushort BaseDepth { get; } public byte BaseLevelCount { get; } public byte Cached { get; }
    public IReadOnlyList<GfxImageStreamBuildData> StreamData => Array.AsReadOnly(_streamData); public IReadOnlyList<DbHeaderImageStreamEntry> SelectedLanguageStreamEntries => Array.AsReadOnly(_selectedLanguageStreamEntries); public IReadOnlyList<uint> ExternalStreamPackageIndices => Array.AsReadOnly(_externalStreamPackageIndices); public byte[]? GetPayloadCopy() => _payload?.ToArray();
    internal GfxImageBuildData Copy() => new(Name, Format, LevelCount, DimensionCount, MultiFaceControl, TextureFlags, Width, Height, Depth, PixelDataBlock, Pad0F, RenderTargetPitch, PixelsOffset, MapType, TextureSemantic, Category, Pad1B, CardMemory, BaseWidth, BaseHeight, BaseDepth, BaseLevelCount, Cached, _streamData, _selectedLanguageStreamEntries, _externalStreamPackageIndices, _payload);
    internal GfxImageBuildData WithPayload(byte[]? payload) => new(Name, Format, LevelCount, DimensionCount, MultiFaceControl, TextureFlags, Width, Height, Depth, PixelDataBlock, Pad0F, RenderTargetPitch, PixelsOffset, MapType, TextureSemantic, Category, Pad1B, CardMemory, BaseWidth, BaseHeight, BaseDepth, BaseLevelCount, Cached, _streamData, _selectedLanguageStreamEntries, _externalStreamPackageIndices, payload);
    private static DbHeaderImageStreamEntry[] SnapshotSelectedLanguageStreamEntries(GfxImageAsset asset)
    {
        if (!asset.StreamData.Any(value => value.HasStreamingData))
            return [];

        if (asset.StreamEntries.Count != GfxImageStreamData.EntryCount)
        {
            throw new InvalidDataException(
                $"Streamed GfxImage '{asset.Name}' must provide exactly {GfxImageStreamData.EntryCount} selected-language DB-header stream entries; found {asset.StreamEntries.Count}.");
        }

        return asset.StreamEntries
            .Select(entry =>
                new DbHeaderImageStreamEntry(
                    entry.FileIndex,
                    entry.SourceStart,
                    entry.SourceEnd,
                    entry.BlockOffset,
                    entry.StreamOffset,
                    SerializedOffset: -1))
            .ToArray();
    }
    private static byte[]? SnapshotPayload(GfxImageAsset asset)
    {
        if (asset.StreamData.Any(value => value.HasStreamingData))
            return null;

        // GfxImage +0x28 is a native presence field. A null serialized
        // pointer performs no PHYSICAL alignment, while a nonzero pointer
        // with a computed zero-byte payload still aligns the destination.
        return asset.PayloadPointer.Raw == 0
            ? null
            : asset.PayloadBytes.ToArray();
    }
}

public sealed class GfxImageAuthoringAdapter : AssetAuthoringAdapter<GfxImageAuthoredSnapshot, GfxImageDraft, GfxImageBuildData>
{
    private static readonly GfxImageBodyEmitter Validator = new();
    public override XAssetType AssetType => XAssetType.Image; public override GfxImageAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => GfxImageAuthoredSnapshot.Import(source); public override GfxImageDraft CreateDraft(GfxImageAuthoredSnapshot snapshot) => new(snapshot.Data); public override GfxImageDraft CloneDraft(GfxImageDraft draft) => draft.Clone();
    public override IReadOnlyList<AssetValidationIssue> ValidateDraft(GfxImageDraft draft) => Validator.Validate(draft.Data).Select(value => new AssetValidationIssue(value.Path, value.Message, AssetValidationSeverity.Error)).ToArray();
    public override bool SemanticallyEquals(GfxImageDraft left, GfxImageDraft right)
    {
        GfxImageBuildData a = left.Data, b = right.Data;
        return a.Name == b.Name && a.Format == b.Format && a.LevelCount == b.LevelCount &&
            a.DimensionCount == b.DimensionCount && a.MultiFaceControl == b.MultiFaceControl &&
            a.TextureFlags == b.TextureFlags && a.Width == b.Width && a.Height == b.Height &&
            a.Depth == b.Depth && a.PixelDataBlock == b.PixelDataBlock && a.Pad0F == b.Pad0F &&
            a.RenderTargetPitch == b.RenderTargetPitch && a.PixelsOffset == b.PixelsOffset &&
            a.MapType == b.MapType && a.TextureSemantic == b.TextureSemantic &&
            a.Category == b.Category && a.Pad1B == b.Pad1B && a.CardMemory == b.CardMemory &&
            a.BaseWidth == b.BaseWidth && a.BaseHeight == b.BaseHeight && a.BaseDepth == b.BaseDepth &&
            a.BaseLevelCount == b.BaseLevelCount && a.Cached == b.Cached &&
            a.StreamData.SequenceEqual(b.StreamData) &&
            a.SelectedLanguageStreamEntries.SequenceEqual(b.SelectedLanguageStreamEntries) &&
            a.ExternalStreamPackageIndices.SequenceEqual(b.ExternalStreamPackageIndices) &&
            PayloadEquals(a.GetPayloadCopy(), b.GetPayloadCopy());
    }
    private static bool PayloadEquals(byte[]? left, byte[]? right) => left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);
    public override GfxImageBuildData ExportBuildData(GfxImageDraft draft) { GfxImageBuildData data = draft.Data; if (Validator.Validate(data).Count != 0) throw new InvalidOperationException("GfxImage draft has validation errors."); return data; }
}
