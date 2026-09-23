using IW4.Loaders.Database;
using IW4.Game.Assets.Sound;
using IW4.Game.Pointers;
using IW4.Game.Zone;
using IW4.Game.IO;

namespace IW4.Loaders.Assets.Sound;

public sealed class LoadedSoundLoader : XAssetLoader<LoadedSound>
{
    public LoadedSoundLoader() : base(XAssetType.LoadedSound, LoadedSound.SerializedSize, "LoadedSound")
    {
    }

    protected override LoadedSound? HandleUnresolvedReference(
        XPointerReference pointer,
        DbLoadExecutionContext context,
        bool requireAsset)
    {
        if (!requireAsset)
            return null;

        return base.HandleUnresolvedReference(pointer, context, requireAsset);
    }

    protected override LoadedSound? ResolvePackedPointer(
        XPointerReference pointer,
        DbLoadExecutionContext context,
        bool requireAsset)
    {
        context.PointerReader.ValidateOffsetPointerRange<LoadedSound>(
            pointer, LoadedSound.SerializedSize, "LoadedSound");
        LoadedSound? canonical = context.ResolveCanonicalAsset<LoadedSound>(pointer, XAssetType.LoadedSound);
        if (canonical is null)
            return HandleUnresolvedReference(pointer, context, requireAsset);

        context.PatchCanonicalAssetPointerCellIfPresent(
            pointer,
            canonical,
            "Canonical LoadedSound has no runtime address.");
        return canonical;
    }

    // The fixed 0x1C-byte root is staged in TEMP. The seek table is loaded in
    // LARGE and PCM/codec bytes are loaded in PHYSICAL at 64-byte alignment.
    protected override LoadedSound ReadBody(
        FastFileCursor cursor,
        XBlockAddress expectedRootAddress,
        DbLoadExecutionContext context)
    {
        int sourceOffset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(
            cursor,
            LoadedSound.SerializedSize,
            out XBlockAddress rootAddress);
        if (rootAddress != expectedRootAddress)
        {
            throw new InvalidDataException(
                $"LoadedSound pointer patched to {expectedRootAddress}, but root loaded at {rootAddress}.");
        }

        var rootCursor = new FastFileCursor(rootBytes, rootAddress);
        XPointer<string> namePointer = context.PointerReader.ReadPointer<string>(
            rootCursor,
            XPointerResolutionMode.Direct);
        int physicalDataByteCount = rootCursor.ReadInt32();
        if (physicalDataByteCount > SoundFile.MaxInMemoryPayloadBytes)
        {
            throw new InvalidDataException(
                $"LoadedSound physical data size {physicalDataByteCount:N0} bytes exceeds the " +
                $"{SoundFile.MaxInMemoryPayloadBytes / (1024 * 1024)} MiB in-memory sound limit.");
        }
        ushort frameCount = rootCursor.ReadUInt16();
        ushort channelCount = rootCursor.ReadUInt16();
        ushort sampleRate = rootCursor.ReadUInt16();
        ushort pad0E = rootCursor.ReadUInt16();
        ushort pad10 = rootCursor.ReadUInt16();
        ushort seekTableCount = rootCursor.ReadUInt16();
        XPointer<byte[]> seekTablePointer = context.PointerReader.ReadPointer<byte[]>(
            rootCursor,
            XPointerResolutionMode.Direct);
        XPointer<byte[]> physicalDataPointer = context.PointerReader.ReadPointer<byte[]>(
            rootCursor,
            XPointerResolutionMode.Direct);

        if (rootCursor.Offset != LoadedSound.SerializedSize)
        {
            throw new InvalidDataException(
                $"LoadedSound consumed 0x{rootCursor.Offset:X} bytes instead of 0x{LoadedSound.SerializedSize:X}.");
        }

        string? name;
        byte[]? seekTable;
        byte[]? physicalData;
        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            name = context.PointerReader.LoadXString(cursor, namePointer);
            seekTable = ReadByteArrayPointer(
                cursor,
                seekTablePointer.Untyped,
                checked(seekTableCount * sizeof(uint)),
                "LoadedSound.seekTable",
                alignment: 4,
                context);

            context.Blocks.Push(XFileBlockType.PHYSICAL);
            try
            {
                physicalData = ReadByteArrayPointer(
                    cursor,
                    physicalDataPointer.Untyped,
                    physicalDataByteCount,
                    "LoadedSound.physicalData",
                    alignment: 64,
                    context);
            }
            finally
            {
                context.Blocks.Pop();
            }
        }
        finally
        {
            context.Blocks.Pop();
        }


        return new LoadedSound
        {
            Offset = sourceOffset,
            RuntimeAddress = rootAddress,
            NamePointer = namePointer,
            Name = name,
            PhysicalDataByteCount = physicalDataByteCount,
            FrameCount = frameCount,
            ChannelCount = channelCount,
            SampleRate = sampleRate,
            Pad0E = pad0E,
            Pad10 = pad10,
            SeekTableCount = seekTableCount,
            SeekTablePointer = seekTablePointer,
            SeekTable = seekTable,
            PhysicalDataPointer = physicalDataPointer,
            PhysicalData = physicalData
        };
    }

    private static byte[]? ReadByteArrayPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        int byteCount,
        string targetName,
        int alignment,
        DbLoadExecutionContext context)
    {
        if (byteCount < 0)
            throw new InvalidDataException($"{targetName} has invalid negative byte count {byteCount}.");

        if (pointer.Type == PointerType.Null)
            return null;

        if (pointer.Type == PointerType.Offset)
        {
            if (pointer.PackedAddress == context.Blocks.CurrentAddress)
                return context.Blocks.Load(cursor, byteCount);

            if (byteCount > 0)
                context.PointerReader.ValidateOffsetPointerRange<byte[]>(pointer, byteCount, targetName);
            return null;
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
        {
            throw new InvalidDataException(
                $"{targetName} pointer 0x{unchecked((uint)pointer.Raw):X8} has unsupported type {pointer.Type}.");
        }

        XBlockAddress? insertCell = pointer.Type == PointerType.Insert
            ? context.Blocks.AllocateInsertPointerCell()
            : null;
        XBlockAddress targetAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment);
        byte[] bytes = context.Blocks.Load(cursor, byteCount);
        if (insertCell is { } cell)
            context.Blocks.WriteInt32(cell, XPointerCodec.Encode(targetAddress));

        return bytes;
    }

    }
