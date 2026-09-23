using IW4.Loaders.Database;
using IW4.Game.Assets.Image;
using IW4.Game.Database.Streaming;
using IW4.Game.Pointers;
using IW4.Game.Zone;
using IW4.Runtime.Database;
using IW4.Game.IO;

namespace IW4.Loaders.Assets.Image;

public sealed class GfxImageLoader : XAssetLoader<GfxImageAsset>
{
    public GfxImageLoader()
        : base(XAssetType.Image, GfxImageAsset.SerializedSize, "GfxImage")
    {
    }

    protected override GfxImageAsset? ResolvePackedPointer(
        XPointerReference pointer,
        DbLoadExecutionContext context,
        bool requireAsset)
    {
        if (ResolveAndPatchAliasCell(pointer, context).HasValue)
            return context.ResolveGfxImage(pointer);

        context.PointerReader.ValidateOffsetPointerRange<GfxImageAsset>(pointer, GfxImageAsset.SerializedSize, "GfxImage");
        return context.ResolveGfxImage(pointer);
    }

    protected override GfxImageAsset RegisterAsset(
        GfxImageAsset asset,
        ProviderRegistrationOccurrence providerRegistration,
        DbLoadExecutionContext context)
    {
        return context.DB_AddXAsset(asset, providerRegistration);
    }

    protected override GfxImageAsset ReadBody(
        FastFileCursor cursor,
        XBlockAddress rootAddress,
        DbLoadExecutionContext context)
    {
        int sourceOffset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(cursor, GfxImageAsset.SerializedSize, out XBlockAddress loadedAddress);
        if (loadedAddress != rootAddress)
            throw new InvalidDataException($"GfxImage pointer patched to {rootAddress}, but root loaded at {loadedAddress}.");

        var rootCursor = new FastFileCursor(rootBytes, rootAddress);
        byte format = rootCursor.ReadByte();
        byte levelCount = rootCursor.ReadByte();
        GfxImageDimension dimensionCount =
            (GfxImageDimension)rootCursor.ReadByte();
        byte multiFaceControl = rootCursor.ReadByte();
        uint textureControl1 = rootCursor.ReadUInt32();
        ushort width = rootCursor.ReadUInt16();
        ushort height = rootCursor.ReadUInt16();
        ushort depth = rootCursor.ReadUInt16();
        GfxImageMemoryLocation memoryLocation =
            (GfxImageMemoryLocation)rootCursor.ReadByte();
        byte minLodControl = rootCursor.ReadByte();
        uint renderTargetPitch = rootCursor.ReadUInt32();
        uint pixelsOffset = rootCursor.ReadUInt32();
        MapType mapType = (MapType)rootCursor.ReadByte();
        TextureSemantic textureSemantic =
            (TextureSemantic)rootCursor.ReadByte();
        ImageCategory category = (ImageCategory)rootCursor.ReadByte();
        byte useSrgbReads = rootCursor.ReadByte();
        uint cardMemory = rootCursor.ReadUInt32();
        ushort baseWidth = rootCursor.ReadUInt16();
        ushort baseHeight = rootCursor.ReadUInt16();
        ushort baseDepth = rootCursor.ReadUInt16();
        byte baseLevelCount = rootCursor.ReadByte();
        GfxImageCached cached = (GfxImageCached)rootCursor.ReadByte();
        XPointerReference payloadPointer = ReadRawCell(rootCursor, context, XPointerResolutionMode.Direct);
        IReadOnlyList<GfxImageStreamData> streamData = ReadStreamData(rootCursor);
        int[] streamPartByteCounts =
            GfxImageStreamData.ValidateProfileAndComputePartByteCounts(streamData);
        int? streamImageIndex = context.AllocateGfxImageStreamIndex(
            streamPartByteCounts.Any(byteCount => byteCount != 0));
        IReadOnlyList<DbHeaderImageStreamEntry> streamEntries = context.GetGfxImageStreamEntries(streamImageIndex);
        XPointer<string> namePointer = context.PointerReader.ReadPointer<string>(rootCursor, XPointerResolutionMode.Direct);

        if (rootCursor.Offset != GfxImageAsset.SerializedSize)
            throw new InvalidDataException($"GfxImage consumed 0x{rootCursor.Offset:X} bytes instead of 0x{GfxImageAsset.SerializedSize:X}.");

        string? name;
        byte[] payloadBytes;
        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            name = context.PointerReader.LoadXString(cursor, namePointer);
            payloadBytes = ReadPayload(
                cursor,
                payloadPointer,
                format,
                levelCount,
                multiFaceControl,
                textureControl1,
                width,
                height,
                depth,
                textureSemantic,
                context);
        }
        finally
        {
            context.Blocks.Pop();
        }


        var image = new GfxImageAsset
        {
            Offset = sourceOffset,
            RuntimeAddress = rootAddress,
            Format = format,
            LevelCount = levelCount,
            DimensionCount = dimensionCount,
            MultiFaceControl = multiFaceControl,
            TextureControl1 = textureControl1,
            Width = width,
            Height = height,
            Depth = depth,
            MemoryLocation = memoryLocation,
            MinLodControl = minLodControl,
            RenderTargetPitch = renderTargetPitch,
            PixelsOffset = pixelsOffset,
            MapType = mapType,
            TextureSemantic = textureSemantic,
            Category = category,
            UseSrgbReads = useSrgbReads,
            CardMemory = cardMemory,
            BaseWidth = baseWidth,
            BaseHeight = baseHeight,
            BaseDepth = baseDepth,
            BaseLevelCount = baseLevelCount,
            Cached = cached,
            PayloadPointer = payloadPointer,
            StreamData = streamData,
            StreamImageIndex = streamImageIndex,
            StreamEntries = streamEntries,
            PayloadByteCount = payloadBytes.Length,
            PayloadBytes = payloadBytes,
            NamePointer = namePointer,
            Name = name
        };
        return image;
    }

    private static IReadOnlyList<GfxImageStreamData> ReadStreamData(FastFileCursor cursor)
    {
        var entries = new GfxImageStreamData[GfxImageStreamData.EntryCount];
        for (int i = 0; i < entries.Length; i++)
        {
            entries[i] = new GfxImageStreamData(
                cursor.ReadUInt16(),
                cursor.ReadUInt16(),
                cursor.ReadUInt32());
        }

        return entries;
    }

    private static byte[] ReadPayload(
        FastFileCursor cursor,
        XPointerReference pointer,
        byte format,
        byte levelCount,
        byte multiFaceControl,
        uint textureControl1,
        ushort width,
        ushort height,
        ushort depth,
        TextureSemantic textureSemantic,
        DbLoadExecutionContext context)
    {
        // GfxImage +0x28 is a presence field, not a generic XFile pointer.
        // Every nonzero value owns a pixel payload, including values that look
        // like insert or packed-pointer encodings.
        if (pointer.Raw == 0)
            return [];

        int byteCount = GfxImagePixelLayout.ComputePayloadByteCount(
            new GfxImageFormat(format),
            levelCount,
            multiFaceControl != 0,
            new GfxImageTextureRemap(textureControl1),
            width,
            height,
            depth);

        if (pointer.CellAddress is not { } cellAddress)
            throw new InvalidDataException($"GfxImage payload pointer 0x{pointer.Raw:X8} has no destination cell address.");

        XFileBlockType payloadBlock =
            textureSemantic == TextureSemantic.WaterMap
            ? XFileBlockType.RUNTIME
            : XFileBlockType.PHYSICAL;

        context.Blocks.Push(payloadBlock);
        try
        {
            context.Blocks.AlignCurrent(128);
            XBlockAddress payloadAddress = context.Blocks.CurrentAddress;
            context.Blocks.WriteInt32(cellAddress, XPointerCodec.Encode(payloadAddress));
            byte[] payloadBytes = context.Blocks.Load(cursor, byteCount);
            return payloadBytes;
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    private static XPointerReference ReadRawCell(
        FastFileCursor cursor,
        DbLoadExecutionContext context,
        XPointerResolutionMode offsetMode) => context.PointerReader.ReadCell(cursor, offsetMode);
}
