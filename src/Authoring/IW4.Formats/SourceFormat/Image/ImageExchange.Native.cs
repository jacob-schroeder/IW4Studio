using System.Text.Json;
using IW4.Game.Assets.Image;
using IW4.Game.Pointers;

namespace IW4.Formats.SourceFormat.Image;

public sealed partial class ImageExchange
{
    /// <summary>Writes the retained PS3 descriptor and exact inline or streamed pixel bytes.</summary>
    public IReadOnlyList<string> UnlinkNative(
        string sourceDirectory,
        GfxImageAsset asset,
        IReadOnlyList<byte[]>? streamParts = null)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string name = SourceOutput.NormalizeOwnedAssetName(asset.Name, "Image");
        byte[] payload = asset.PayloadBytes?.ToArray() ??
            throw new InvalidDataException($"Image '{name}' has no pixel payload state.");
        GfxImageStreamData[] streamData = asset.StreamData.Count == 0
            ? FourEmptyStreamRecords() : asset.StreamData.ToArray();
        byte[][] parts = CopyAndValidateStreamParts(asset, payload, streamData, streamParts, name);

        var metadata = new NativeMetadata
        {
            Format = "iw4-image-source",
            Version = 2,
            Name = name,
            ImageFormat = asset.Format,
            LevelCount = asset.LevelCount,
            DimensionCount = (byte)asset.DimensionCount,
            MultiFaceControl = asset.MultiFaceControl,
            TextureControl1 = asset.TextureControl1,
            Width = asset.Width,
            Height = asset.Height,
            Depth = asset.Depth,
            MemoryLocation = (byte)asset.SerializedMemoryLocation,
            MinLodControl = asset.MinLodControl,
            RenderTargetPitch = asset.RenderTargetPitch,
            PixelsOffset = asset.SerializedPixelsOffset,
            MapType = (byte)asset.MapType,
            Semantic = (byte)asset.TextureSemantic,
            Category = (byte)asset.Category,
            UseSrgbReads = asset.UseSrgbReads,
            CardMemory = asset.CardMemory,
            BaseWidth = asset.BaseWidth,
            BaseHeight = asset.BaseHeight,
            BaseDepth = asset.BaseDepth,
            BaseLevelCount = asset.BaseLevelCount,
            Cached = (byte)asset.Cached,
            StreamData = streamData,
            PayloadByteCount = payload.Length
        };
        string imagePath = $"images/{name.Replace('*', '_')}";
        var files = new List<(string RelativePath, Action<Stream> Write)>
        {
            ($"{imagePath}.image.json", stream =>
                JsonSerializer.Serialize(stream, metadata, MetadataOptions)),
            ($"{imagePath}.pixels.bin", stream => stream.Write(payload))
        };
        for (int index = 0; index < parts.Length; index++)
        {
            if (parts[index].Length == 0)
                continue;
            int partIndex = index;
            files.Add(($"{imagePath}.stream{partIndex}.bin", stream => stream.Write(parts[partIndex])));
        }
        return new SourceOutput(sourceDirectory).WriteBinaryBatch(files);
    }

    private static GfxImageAsset LinkNative(
        string imagePath,
        string name,
        JsonElement source,
        out IReadOnlyList<byte[]> streamParts)
    {
        NativeMetadata metadata = source.Deserialize<NativeMetadata>(MetadataOptions)
            ?? throw new InvalidDataException($"Image '{name}' has empty native metadata.");
        if (metadata.Format != "iw4-image-source" || metadata.Version != 2 ||
            metadata.Name != name || metadata.StreamData is null ||
            metadata.PayloadByteCount < 0)
            throw new InvalidDataException($"Image '{name}' has invalid native metadata.");
        int[] byteCounts = GfxImageStreamData.ValidateProfileAndComputePartByteCounts(metadata.StreamData);
        string payloadPath = imagePath + ".pixels.bin";
        if (new FileInfo(payloadPath).Length != metadata.PayloadByteCount)
            throw new InvalidDataException($"Image '{name}' native pixel byte count does not match metadata.");
        byte[] payload = File.ReadAllBytes(payloadPath);
        var parts = new byte[GfxImageStreamData.EntryCount][];
        for (int index = 0; index < parts.Length; index++)
        {
            if (byteCounts[index] == 0)
            {
                parts[index] = [];
                continue;
            }
            string partPath = $"{imagePath}.stream{index}.bin";
            if (new FileInfo(partPath).Length != byteCounts[index])
                throw new InvalidDataException($"Image '{name}' stream part {index} byte count does not match its profile.");
            parts[index] = File.ReadAllBytes(partPath);
        }

        var asset = new GfxImageAsset
        {
            Name = name,
            Format = metadata.ImageFormat,
            LevelCount = metadata.LevelCount,
            DimensionCount = (GfxImageDimension)metadata.DimensionCount,
            MultiFaceControl = metadata.MultiFaceControl,
            TextureControl1 = metadata.TextureControl1,
            Width = metadata.Width,
            Height = metadata.Height,
            Depth = metadata.Depth,
            MemoryLocation = (GfxImageMemoryLocation)metadata.MemoryLocation,
            MinLodControl = metadata.MinLodControl,
            RenderTargetPitch = metadata.RenderTargetPitch,
            PixelsOffset = metadata.PixelsOffset,
            MapType = (MapType)metadata.MapType,
            TextureSemantic = (TextureSemantic)metadata.Semantic,
            Category = (ImageCategory)metadata.Category,
            UseSrgbReads = metadata.UseSrgbReads,
            CardMemory = metadata.CardMemory,
            BaseWidth = metadata.BaseWidth,
            BaseHeight = metadata.BaseHeight,
            BaseDepth = metadata.BaseDepth,
            BaseLevelCount = metadata.BaseLevelCount,
            Cached = (GfxImageCached)metadata.Cached,
            StreamData = metadata.StreamData,
            PayloadByteCount = payload.Length,
            PayloadBytes = payload
        };
        CopyAndValidateStreamParts(asset, payload, metadata.StreamData, parts, name);
        streamParts = Array.AsReadOnly(parts);
        return asset;
    }

    private static byte[][] CopyAndValidateStreamParts(
        GfxImageAsset asset,
        byte[] payload,
        IReadOnlyList<GfxImageStreamData> streamData,
        IReadOnlyList<byte[]>? streamParts,
        string name)
    {
        int[] expectedPartLengths = GfxImageStreamData.ValidateProfileAndComputePartByteCounts(streamData);
        if (streamParts is not null && streamParts.Count != GfxImageStreamData.EntryCount)
            throw new InvalidDataException($"Image '{name}' requires four ordered stream part buffers.");
        byte[][] parts = Enumerable.Range(0, GfxImageStreamData.EntryCount)
            .Select(index => streamParts is null ? Array.Empty<byte>() :
                streamParts[index]?.ToArray() ?? throw new InvalidDataException(
                    $"Image '{name}' stream part {index} is null.")).ToArray();
        for (int index = 0; index < parts.Length; index++)
        {
            if (parts[index].Length != expectedPartLengths[index])
                throw new InvalidDataException(
                    $"Image '{name}' stream part {index} has {parts[index].Length} bytes; its profile requires {expectedPartLengths[index]}.");
        }
        bool isStreamed = expectedPartLengths.Any(length => length != 0);
        if (asset.PayloadByteCount != payload.Length ||
            (payload.Length == 0 && !isStreamed && asset.PayloadPointer.Type != PointerType.Null) ||
            (isStreamed && (payload.Length != 0 || asset.PayloadPointer.Type != PointerType.Null)))
            throw new InvalidDataException($"Image '{name}' has inconsistent inline and streamed pixel presence.");
        if (payload.Length == 0)
            return parts;

        int expectedByteCount = GfxImagePixelLayout.ComputePayloadByteCount(
            asset.FormatEncoding,
            asset.LevelCount,
            asset.IsCubemap,
            asset.TextureRemap,
            asset.Width,
            asset.Height,
            asset.Depth);
        if (expectedByteCount <= 0 || payload.Length != expectedByteCount)
            throw new InvalidDataException(
                $"Image '{name}' has {payload.Length} native pixel bytes; its descriptor requires {expectedByteCount}.");
        if (asset.TextureSemantic == TextureSemantic.WaterMap && payload.Any(value => value != 0))
            throw new InvalidDataException($"Image '{name}' runtime water pixels must be zero-filled.");
        return parts;
    }

    private static GfxImageStreamData[] FourEmptyStreamRecords() =>
        Enumerable.Range(0, GfxImageStreamData.EntryCount)
            .Select(_ => new GfxImageStreamData(0, 0, 0)).ToArray();

    private sealed record NativeMetadata
    {
        public required string Format { get; init; }
        public required int Version { get; init; }
        public required string Name { get; init; }
        public required byte ImageFormat { get; init; }
        public required byte LevelCount { get; init; }
        public required byte DimensionCount { get; init; }
        public required byte MultiFaceControl { get; init; }
        public required uint TextureControl1 { get; init; }
        public required ushort Width { get; init; }
        public required ushort Height { get; init; }
        public required ushort Depth { get; init; }
        public required byte MemoryLocation { get; init; }
        public required byte MinLodControl { get; init; }
        public required uint RenderTargetPitch { get; init; }
        public required uint PixelsOffset { get; init; }
        public required byte MapType { get; init; }
        public required byte Semantic { get; init; }
        public required byte Category { get; init; }
        public required byte UseSrgbReads { get; init; }
        public required uint CardMemory { get; init; }
        public required ushort BaseWidth { get; init; }
        public required ushort BaseHeight { get; init; }
        public required ushort BaseDepth { get; init; }
        public required byte BaseLevelCount { get; init; }
        public required byte Cached { get; init; }
        public required GfxImageStreamData[] StreamData { get; init; }
        public required int PayloadByteCount { get; init; }
    }
}
