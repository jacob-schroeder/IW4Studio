using IW4.FastFiles.Loaders.Database;
using IW4.Assets.Assets.Image;
using IW4.FastFiles.Database.Streaming;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;

namespace IW4.FastFiles.Loaders.Assets.Image;

public sealed class GfxImageLoader
{
    public GfxImageAsset LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        return LoadFromPointer(cursor, pointer, context)
            ?? throw new InvalidDataException("A top-level GfxImage XAsset cannot have a null body.");
    }

    public GfxImageAsset? LoadFromPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (ResolveAliasCellOffset<GfxImageAsset>(pointer, context, GfxImageAsset.SerializedSize, "GfxImage"))
            return context.ResolveGfxImage(pointer);

        if (pointer.Type == PointerType.Null)
            return null;

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<GfxImageAsset>(pointer, GfxImageAsset.SerializedSize, "GfxImage");
            return context.ResolveGfxImage(pointer);
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
            throw new NotSupportedException($"GfxImage pointer 0x{pointer.Raw:X8} uses unsupported source sentinel {pointer.Type}.");

        int sourceOffset = cursor.Offset;
        ProviderRegistrationOccurrence providerRegistration = context.BeginProviderRegistration(pointer);

        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            XBlockAddress rootAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            byte[] rootBytes = context.Blocks.Load(cursor, GfxImageAsset.SerializedSize, out XBlockAddress loadedAddress);
            if (loadedAddress != rootAddress)
                throw new InvalidDataException($"GfxImage pointer patched to {rootAddress}, but root loaded at {loadedAddress}.");

            var rootCursor = new FastFileCursor(rootBytes, rootAddress);
            byte format = rootCursor.ReadByte();
            byte levelCount = rootCursor.ReadByte();
            byte dimensionCount = rootCursor.ReadByte();
            byte multiFaceControl = rootCursor.ReadByte();
            uint textureFlags = rootCursor.ReadUInt32();
            ushort width = rootCursor.ReadUInt16();
            ushort height = rootCursor.ReadUInt16();
            ushort depth = rootCursor.ReadUInt16();
            byte pixelDataBlock = rootCursor.ReadByte();
            byte pad0F = rootCursor.ReadByte();
            uint renderTargetPitch = rootCursor.ReadUInt32();
            uint pixelsOffset = rootCursor.ReadUInt32();
            byte mapType = rootCursor.ReadByte();
            byte textureSemantic = rootCursor.ReadByte();
            byte category = rootCursor.ReadByte();
            byte pad1B = rootCursor.ReadByte();
            uint cardMemory = rootCursor.ReadUInt32();
            ushort baseWidth = rootCursor.ReadUInt16();
            ushort baseHeight = rootCursor.ReadUInt16();
            ushort baseDepth = rootCursor.ReadUInt16();
            byte baseLevelCount = rootCursor.ReadByte();
            byte cached = rootCursor.ReadByte();
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
                    textureFlags,
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
                TextureFlags = textureFlags,
                Width = width,
                Height = height,
                Depth = depth,
                PixelDataBlock = pixelDataBlock,
                Pad0F = pad0F,
                RenderTargetPitch = renderTargetPitch,
                PixelsOffset = pixelsOffset,
                MapType = mapType,
                TextureSemantic = textureSemantic,
                Category = category,
                Pad1B = pad1B,
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
            GfxImageAsset canonical = context.DB_AddXAsset(image, providerRegistration);

            return canonical;
        }
        finally
        {
            context.Blocks.Pop();
        }
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
        uint textureFlags,
        ushort width,
        ushort height,
        ushort depth,
        byte textureSemantic,
        DbLoadExecutionContext context)
    {
        // GfxImage +0x28 is a presence field, not a generic XFile pointer.
        // Every nonzero value owns a pixel payload, including values that look
        // like insert or packed-pointer encodings.
        if (pointer.Raw == 0)
            return [];

        int byteCount = GfxImagePixelLayout.ComputePayloadByteCount(
            format,
            levelCount,
            multiFaceControl,
            textureFlags,
            width,
            height,
            depth);

        if (pointer.CellAddress is not { } cellAddress)
            throw new InvalidDataException($"GfxImage payload pointer 0x{pointer.Raw:X8} has no destination cell address.");

        XFileBlockType payloadBlock = textureSemantic == 0x0b
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

    private static bool ResolveAliasCellOffset<T>(
        XPointerReference pointer,
        DbLoadExecutionContext context,
        int targetByteCount,
        string targetName)
    {
        if (pointer.Type != PointerType.Offset || pointer.ResolutionMode != XPointerResolutionMode.AliasCell)
            return false;

        if (pointer.CellAddress is not { } destinationCell)
            throw new InvalidDataException($"Alias-cell pointer 0x{pointer.Raw:X8} has no destination cell to patch.");

        int aliasedRaw = context.PointerReader.ReadAliasCellRaw(pointer);
        if (aliasedRaw != 0)
        {
            PointerType aliasedType = XPointerCodec.GetType(aliasedRaw);
            if (aliasedType != PointerType.Offset)
                throw new InvalidDataException($"Alias-cell pointer 0x{pointer.Raw:X8} resolved to unresolved sentinel 0x{aliasedRaw:X8} for {targetName}.");

            context.PointerReader.ValidateOffsetPointerRange<T>(
                XPointerReference.FromRaw(aliasedRaw, XPointerResolutionMode.Direct, pointer.PackedAddress),
                targetByteCount,
                targetName);
        }

        context.Blocks.WriteInt32(destinationCell, aliasedRaw);
        return true;
    }
}
