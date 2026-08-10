using IW4.FastFiles.Loaders.Database;
using IW4.Assets.Assets.XAnim;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;
using XString = IW4.FastFiles.Pointers.XPointer<string>;

namespace IW4.FastFiles.Loaders.Assets.XAnim;

public sealed class XAnimPartsLoader
{
    public XAnimPartsAsset LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        return LoadFromPointerCore(cursor, pointer, context, requireAsset: true)
            ?? throw new InvalidDataException("Top-level XAnim pointer resolved to null.");
    }

    public XAnimPartsAsset? LoadFromPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        return LoadFromPointerCore(cursor, pointer, context, requireAsset: false);
    }

    private static XAnimPartsAsset? LoadFromPointerCore(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context,
        bool requireAsset)
    {
        if (pointer.Type == PointerType.Null)
        {
            if (requireAsset)
                throw new InvalidDataException("Top-level XAnim pointer is null.");

            return null;
        }

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<XAnimPartsAsset>(
                pointer,
                XAnimPartsAsset.SerializedSize,
                "XAnimParts");
            XAnimPartsAsset? canonical = context.ResolveCanonicalAsset<XAnimPartsAsset>(
                pointer,
                XAssetType.XAnim);
            if (canonical is null)
            {
                if (!requireAsset)
                    return null;

                throw new InvalidDataException(
                    $"Top-level XAnim pointer 0x{unchecked((uint)pointer.Raw):X8} " +
                    "does not resolve to a canonical XAnim asset.");
            }

            PatchCanonicalPointerCell(pointer, canonical, context);
            return canonical;
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
        {
            throw new InvalidDataException(
                $"XAnim pointer 0x{unchecked((uint)pointer.Raw):X8} has unsupported type {pointer.Type}.");
        }

        ProviderRegistrationOccurrence providerRegistration = context.BeginProviderRegistration(pointer);

        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            XBlockAddress rootAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            XAnimPartsAsset asset = ReadXAnimParts(cursor, rootAddress, context);
            XAnimPartsAsset canonical = context.DB_AddXAsset(
                XAssetType.XAnim,
                asset.Name,
                asset,
                providerRegistration);

            return canonical;
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    private static XAnimPartsAsset ReadXAnimParts(
        FastFileCursor cursor,
        XBlockAddress expectedRootAddress,
        DbLoadExecutionContext context)
    {
        int sourceOffset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(cursor, XAnimPartsAsset.SerializedSize, out XBlockAddress rootAddress);
        if (rootAddress != expectedRootAddress)
        {
            throw new InvalidDataException(
                $"XAnim pointer patched to {expectedRootAddress}, but root loaded at {rootAddress}.");
        }

        var rootCursor = new FastFileCursor(rootBytes, rootAddress);

        XString namePointer = ReadXStringPointer(rootCursor, context);
        ushort dataByteCount = rootCursor.ReadUInt16();
        ushort dataShortCount = rootCursor.ReadUInt16();
        ushort dataIntCount = rootCursor.ReadUInt16();
        ushort randomDataByteCount = rootCursor.ReadUInt16();
        ushort randomDataIntCount = rootCursor.ReadUInt16();
        ushort numFrames = rootCursor.ReadUInt16();
        byte flags = rootCursor.ReadByte();
        byte deltaFlags = rootCursor.ReadByte();
        byte[] boneCounts = rootCursor.ReadBytes(0x0a);
        byte boneNameCount = rootCursor.ReadByte();
        byte notifyCount = rootCursor.ReadByte();
        byte assetType = rootCursor.ReadByte();
        byte pad1F = rootCursor.ReadByte();
        int randomDataShortCount = rootCursor.ReadInt32();
        int indexCount = rootCursor.ReadInt32();
        float framerate = ReadSingle(rootCursor);
        float frequency = ReadSingle(rootCursor);
        XPointer<ushort[]> namesPointer = ReadPointer<ushort[]>(rootCursor, context, XPointerResolutionMode.Direct);
        XPointer<byte[]> dataBytePointer = ReadPointer<byte[]>(rootCursor, context, XPointerResolutionMode.Direct);
        XPointer<short[]> dataShortPointer = ReadPointer<short[]>(rootCursor, context, XPointerResolutionMode.Direct);
        XPointer<int[]> dataIntPointer = ReadPointer<int[]>(rootCursor, context, XPointerResolutionMode.Direct);
        XPointer<short[]> randomDataShortPointer = ReadPointer<short[]>(rootCursor, context, XPointerResolutionMode.Direct);
        XPointer<byte[]> randomDataBytePointer = ReadPointer<byte[]>(rootCursor, context, XPointerResolutionMode.Direct);
        XPointer<int[]> randomDataIntPointer = ReadPointer<int[]>(rootCursor, context, XPointerResolutionMode.Direct);
        XPointer<object> indicesPointer = ReadPointer<object>(rootCursor, context, XPointerResolutionMode.Direct);
        XPointer<XAnimNotifyInfo[]> notifyPointer = ReadPointer<XAnimNotifyInfo[]>(rootCursor, context, XPointerResolutionMode.Direct);
        XPointer<XAnimDeltaPart> deltaPartPointer = ReadPointer<XAnimDeltaPart>(rootCursor, context, XPointerResolutionMode.Direct);

        if (rootCursor.Offset != XAnimPartsAsset.SerializedSize)
            throw new InvalidDataException($"XAnimParts consumed 0x{rootCursor.Offset:X} bytes instead of 0x{XAnimPartsAsset.SerializedSize:X}.");

        string? name;
        IReadOnlyList<ushort> names;
        IReadOnlyList<XAnimNotifyInfo> notify;
        XAnimDeltaPart? deltaPart;
        XAnimPackedDataStreams packedDataStreams;
        XAnimFrameIndexStream indices;

        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            name = ReadXString(cursor, namePointer, context);
            names = ReadUInt16Array(cursor, namesPointer.Untyped, boneNameCount, alignment: 2, "XAnimParts.Names", context);
            notify = ReadXAnimNotifyInfoArray(cursor, notifyPointer.Untyped, notifyCount, context);
            deltaPart = ReadXAnimDeltaPart(cursor, deltaPartPointer.Untyped, numFrames, context);
            packedDataStreams = new XAnimPackedDataStreams
            {
                QuantizedBytes = ReadByteStream(cursor, dataBytePointer.Untyped, dataByteCount, alignment: 1, "XAnimParts.DataByte", context),
                QuantizedShorts = ReadInt16Stream(cursor, dataShortPointer.Untyped, dataShortCount, alignment: 2, "XAnimParts.DataShort", context),
                QuantizedInts = ReadInt32Stream(cursor, dataIntPointer.Untyped, dataIntCount, alignment: 4, "XAnimParts.DataInt", context),
                RandomizedQuantizedShorts = ReadInt16Stream(cursor, randomDataShortPointer.Untyped, randomDataShortCount, alignment: 2, "XAnimParts.RandomDataShort", context),
                RandomizedQuantizedBytes = ReadByteStream(cursor, randomDataBytePointer.Untyped, randomDataByteCount, alignment: 1, "XAnimParts.RandomDataByte", context),
                RandomizedQuantizedInts = ReadInt32Stream(cursor, randomDataIntPointer.Untyped, randomDataIntCount, alignment: 4, "XAnimParts.RandomDataInt", context)
            };
            indices = ReadXAnimIndices(cursor, indicesPointer.Untyped, numFrames, indexCount, context);
        }
        finally
        {
            context.Blocks.Pop();
        }


        return new XAnimPartsAsset
        {
            Offset = sourceOffset,
            RuntimeAddress = rootAddress,
            NamePointer = namePointer,
            Name = name,
            DataByteCount = dataByteCount,
            DataShortCount = dataShortCount,
            DataIntCount = dataIntCount,
            RandomDataByteCount = randomDataByteCount,
            RandomDataIntCount = randomDataIntCount,
            NumFrames = numFrames,
            Flags = flags,
            DeltaFlags = deltaFlags,
            BoneCounts = boneCounts,
            BoneNameCount = boneNameCount,
            NotifyCount = notifyCount,
            AssetType = assetType,
            Pad1F = pad1F,
            RandomDataShortCount = randomDataShortCount,
            IndexCount = indexCount,
            Framerate = framerate,
            Frequency = frequency,
            NamesPointer = namesPointer,
            Names = names,
            DataBytePointer = dataBytePointer,
            DataShortPointer = dataShortPointer,
            DataIntPointer = dataIntPointer,
            RandomDataShortPointer = randomDataShortPointer,
            RandomDataBytePointer = randomDataBytePointer,
            RandomDataIntPointer = randomDataIntPointer,
            IndicesPointer = indicesPointer,
            PackedDataStreams = packedDataStreams,
            Indices = indices,
            NotifyPointer = notifyPointer,
            Notify = notify,
            DeltaPartPointer = deltaPartPointer,
            DeltaPart = deltaPart
        };
    }

    private static IReadOnlyList<XAnimNotifyInfo> ReadXAnimNotifyInfoArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative XAnimNotifyInfo count {count}.");

        if (pointer.Type == PointerType.Null || count == 0)
            return [];

        XBlockAddress address = PatchCurrentPayloadPointer(pointer, alignment: 4, checked(count * XAnimNotifyInfo.SerializedSize), "XAnimNotifyInfo[]", context);
        byte[] bytes = context.Blocks.Load(cursor, checked(count * XAnimNotifyInfo.SerializedSize));
        var c = new FastFileCursor(bytes, address);
        var values = new XAnimNotifyInfo[count];
        for (int i = 0; i < values.Length; i++)
        {
            ushort name = c.ReadUInt16();
            c.Skip(0x04 - c.Offset % XAnimNotifyInfo.SerializedSize);
            values[i] = new XAnimNotifyInfo(name, ReadSingle(c));
        }

        return values;
    }

    private static XAnimDeltaPart? ReadXAnimDeltaPart(
        FastFileCursor cursor,
        XPointerReference pointer,
        ushort numFrames,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            return null;

        XBlockAddress address = PatchCurrentPayloadPointer(pointer, alignment: 4, XAnimDeltaPart.SerializedSize, "XAnimDeltaPart", context);
        byte[] bytes = context.Blocks.Load(cursor, XAnimDeltaPart.SerializedSize);
        var c = new FastFileCursor(bytes, address);

        XPointer<XAnimPartTrans> transPointer = ReadPointer<XAnimPartTrans>(c, context, XPointerResolutionMode.Direct);
        XPointer<XAnimDeltaPartQuat2> quat2Pointer = ReadPointer<XAnimDeltaPartQuat2>(c, context, XPointerResolutionMode.Direct);
        XPointer<XAnimDeltaPartQuat> quatPointer = ReadPointer<XAnimDeltaPartQuat>(c, context, XPointerResolutionMode.Direct);

        return new XAnimDeltaPart
        {
            TransPointer = transPointer,
            Trans = ReadXAnimPartTrans(cursor, transPointer.Untyped, numFrames, context),
            Quat2Pointer = quat2Pointer,
            Quat2 = ReadXAnimDeltaPartQuat2(cursor, quat2Pointer.Untyped, numFrames, context),
            QuatPointer = quatPointer,
            Quat = ReadXAnimDeltaPartQuat(cursor, quatPointer.Untyped, numFrames, context)
        };
    }

    private static XAnimPartTrans? ReadXAnimPartTrans(
        FastFileCursor cursor,
        XPointerReference pointer,
        ushort numFrames,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            return null;

        XBlockAddress address = PatchCurrentPayloadPointer(pointer, alignment: 4, XAnimPartTrans.SerializedSize, "XAnimPartTrans", context);
        byte[] bytes = context.Blocks.Load(cursor, XAnimPartTrans.SerializedSize);
        var c = new FastFileCursor(bytes, address);
        ushort size = c.ReadUInt16();
        byte smallTrans = c.ReadByte();
        byte pad3 = c.ReadByte();

        if (size == 0)
        {
            byte[] frame0 = context.Blocks.Load(cursor, XAnimPartTransFrame0.SerializedSize);
            var frameCursor = new FastFileCursor(frame0);
            return new XAnimPartTrans
            {
                Size = size,
                SmallTrans = smallTrans,
                Pad3 = pad3,
                Frame0 = new XAnimPartTransFrame0(ReadSingle(frameCursor), ReadSingle(frameCursor), ReadSingle(frameCursor))
            };
        }

        XAnimPartTransFrames frames = ReadXAnimPartTransFrames(cursor, size, smallTrans, numFrames, context);
        return new XAnimPartTrans
        {
            Size = size,
            SmallTrans = smallTrans,
            Pad3 = pad3,
            Frames = frames
        };
    }

    private static XAnimPartTransFrames ReadXAnimPartTransFrames(
        FastFileCursor cursor,
        ushort size,
        byte smallTrans,
        ushort numFrames,
        DbLoadExecutionContext context)
    {
        byte[] bytes = context.Blocks.Load(cursor, XAnimPartTransFrames.SerializedSize, out XBlockAddress framesAddress);
        var c = new FastFileCursor(bytes, framesAddress);
        XAnimVec3 mins = ReadVec3(c);
        XAnimVec3 frameSize = ReadVec3(c);
        XPointer<byte[]> framesPointer = ReadPointer<byte[]>(c, context, XPointerResolutionMode.Direct);
        int frameCount = checked(size + 1);
        int dynamicByteCount = GetDynamicIndexByteCount(numFrames, frameCount);
        byte[] dynamicBytes = context.Blocks.Load(cursor, dynamicByteCount);
        int framePayloadByteCount = checked(frameCount * (smallTrans != 0 ? 3 : 6));
        byte[] framePayloadBytes = ReadRawBytes(cursor, framesPointer.Untyped, framePayloadByteCount, smallTrans != 0 ? 1 : 4, "XAnimPartTransFrames.Frames", context);

        return new XAnimPartTransFrames
        {
            Mins = mins,
            Size = frameSize,
            FramesPointer = framesPointer,
            DynamicFrames = ReadDynamicFrames(dynamicBytes, numFrames, frameCount),
            FramePayload = ReadTransFramePayload(framePayloadBytes, smallTrans, frameCount)
        };
    }

    private static XAnimDeltaPartQuat2? ReadXAnimDeltaPartQuat2(
        FastFileCursor cursor,
        XPointerReference pointer,
        ushort numFrames,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            return null;

        XBlockAddress address = PatchCurrentPayloadPointer(pointer, alignment: 4, XAnimDeltaPartQuat2.SerializedSize, "XAnimDeltaPartQuat2", context);
        byte[] bytes = context.Blocks.Load(cursor, XAnimDeltaPartQuat2.SerializedSize);
        var c = new FastFileCursor(bytes, address);
        ushort size = c.ReadUInt16();
        byte pad2 = c.ReadByte();
        byte pad3 = c.ReadByte();

        if (size == 0)
        {
            byte[] frame0 = context.Blocks.Load(cursor, XQuat2.SerializedSize);
            var frameCursor = new FastFileCursor(frame0);
            return new XAnimDeltaPartQuat2
            {
                Size = size,
                Pad2 = pad2,
                Pad3 = pad3,
                Frame0 = new XQuat2(unchecked((short)frameCursor.ReadUInt16()), unchecked((short)frameCursor.ReadUInt16()))
            };
        }

        XAnimDeltaPartQuatDataFrames2 frames = ReadXAnimDeltaPartQuatDataFrames2(cursor, size, numFrames, context);
        return new XAnimDeltaPartQuat2
        {
            Size = size,
            Pad2 = pad2,
            Pad3 = pad3,
            Frames = frames
        };
    }

    private static XAnimDeltaPartQuatDataFrames2 ReadXAnimDeltaPartQuatDataFrames2(
        FastFileCursor cursor,
        ushort size,
        ushort numFrames,
        DbLoadExecutionContext context)
    {
        byte[] bytes = context.Blocks.Load(cursor, XAnimDeltaPartQuatDataFrames2.SerializedSize, out XBlockAddress framesAddress);
        var c = new FastFileCursor(bytes, framesAddress);
        XPointer<XQuat2[]> framesPointer = ReadPointer<XQuat2[]>(c, context, XPointerResolutionMode.Direct);
        int frameCount = checked(size + 1);
        int dynamicByteCount = GetDynamicIndexByteCount(numFrames, frameCount);
        byte[] dynamicBytes = context.Blocks.Load(cursor, dynamicByteCount);
        byte[] frameBytes = ReadRawBytes(cursor, framesPointer.Untyped, checked(frameCount * XQuat2.SerializedSize), alignment: 4, "XAnimDeltaPartQuatDataFrames2.Frames", context);
        return new XAnimDeltaPartQuatDataFrames2
        {
            FramesPointer = framesPointer,
            FrameCount = frameCount,
            DynamicIndexByteCount = dynamicByteCount,
            DynamicFrames = ReadDynamicFrames(dynamicBytes, numFrames, frameCount),
            Frames = ReadQuat2Frames(frameBytes, frameCount)
        };
    }

    private static XAnimDeltaPartQuat? ReadXAnimDeltaPartQuat(
        FastFileCursor cursor,
        XPointerReference pointer,
        ushort numFrames,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            return null;

        XBlockAddress address = PatchCurrentPayloadPointer(pointer, alignment: 4, XAnimDeltaPartQuat.SerializedSize, "XAnimDeltaPartQuat", context);
        byte[] bytes = context.Blocks.Load(cursor, XAnimDeltaPartQuat.SerializedSize);
        var c = new FastFileCursor(bytes, address);
        ushort size = c.ReadUInt16();
        byte pad2 = c.ReadByte();
        byte pad3 = c.ReadByte();

        if (size == 0)
        {
            byte[] frame0 = context.Blocks.Load(cursor, XQuat.SerializedSize);
            var frameCursor = new FastFileCursor(frame0);
            return new XAnimDeltaPartQuat
            {
                Size = size,
                Pad2 = pad2,
                Pad3 = pad3,
                Frame0 = new XQuat(
                    unchecked((short)frameCursor.ReadUInt16()),
                    unchecked((short)frameCursor.ReadUInt16()),
                    unchecked((short)frameCursor.ReadUInt16()),
                    unchecked((short)frameCursor.ReadUInt16()))
            };
        }

        XAnimDeltaPartQuatDataFrames frames = ReadXAnimDeltaPartQuatDataFrames(cursor, size, numFrames, context);
        return new XAnimDeltaPartQuat
        {
            Size = size,
            Pad2 = pad2,
            Pad3 = pad3,
            Frames = frames
        };
    }

    private static XAnimDeltaPartQuatDataFrames ReadXAnimDeltaPartQuatDataFrames(
        FastFileCursor cursor,
        ushort size,
        ushort numFrames,
        DbLoadExecutionContext context)
    {
        byte[] bytes = context.Blocks.Load(cursor, XAnimDeltaPartQuatDataFrames.SerializedSize, out XBlockAddress framesAddress);
        var c = new FastFileCursor(bytes, framesAddress);
        XPointer<XQuat[]> framesPointer = ReadPointer<XQuat[]>(c, context, XPointerResolutionMode.Direct);
        int frameCount = checked(size + 1);
        int dynamicByteCount = GetDynamicIndexByteCount(numFrames, frameCount);
        byte[] dynamicBytes = context.Blocks.Load(cursor, dynamicByteCount);
        byte[] frameBytes = ReadRawBytes(cursor, framesPointer.Untyped, checked(frameCount * XQuat.SerializedSize), alignment: 4, "XAnimDeltaPartQuatDataFrames.Frames", context);
        return new XAnimDeltaPartQuatDataFrames
        {
            FramesPointer = framesPointer,
            FrameCount = frameCount,
            DynamicIndexByteCount = dynamicByteCount,
            DynamicFrames = ReadDynamicFrames(dynamicBytes, numFrames, frameCount),
            Frames = ReadQuatFrames(frameBytes, frameCount)
        };
    }

    private static XAnimFrameIndexStream ReadXAnimIndices(
        FastFileCursor cursor,
        XPointerReference pointer,
        ushort numFrames,
        int indexCount,
        DbLoadExecutionContext context)
    {
        int elementSize = numFrames <= byte.MaxValue ? sizeof(byte) : sizeof(ushort);
        int alignment = numFrames <= byte.MaxValue ? 1 : 2;
        byte[] bytes = ReadRawBytes(cursor, pointer, checked(indexCount * elementSize), alignment, "XAnimIndices", context);
        var indices = new ushort[indexCount];
        var c = new FastFileCursor(bytes);
        if (numFrames <= byte.MaxValue)
        {
            for (int i = 0; i < indices.Length; i++)
                indices[i] = c.ReadByte();
        }
        else
        {
            for (int i = 0; i < indices.Length; i++)
                indices[i] = c.ReadUInt16();
        }

        return new XAnimFrameIndexStream
        {
            FrameIndices = indices,
            EncodedByteCount = bytes.Length,
            IsByteEncoded = numFrames <= byte.MaxValue
        };
    }

    private static IReadOnlyList<ushort> ReadUInt16Array(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        int alignment,
        string targetName,
        DbLoadExecutionContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative ushort count {count} for {targetName}.");

        if (pointer.Type == PointerType.Null || count == 0)
            return [];

        XBlockAddress address = PatchCurrentPayloadPointer(pointer, alignment, checked(count * sizeof(ushort)), targetName, context);
        byte[] bytes = context.Blocks.Load(cursor, checked(count * sizeof(ushort)));
        var c = new FastFileCursor(bytes, address);
        var values = new ushort[count];
        for (int i = 0; i < values.Length; i++)
            values[i] = c.ReadUInt16();
        return values;
    }

    private static byte[] ReadRawBytes(
        FastFileCursor cursor,
        XPointerReference pointer,
        int byteCount,
        int alignment,
        string targetName,
        DbLoadExecutionContext context)
    {
        if (byteCount < 0)
            throw new InvalidDataException($"Invalid negative byte count {byteCount} for {targetName}.");

        if (pointer.Type == PointerType.Null)
            return [];

        PatchCurrentPayloadPointer(pointer, alignment, byteCount, targetName, context);
        return context.Blocks.Load(cursor, byteCount);
    }

    private static IReadOnlyList<byte> ReadByteStream(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        int alignment,
        string targetName,
        DbLoadExecutionContext context)
    {
        return ReadRawBytes(cursor, pointer, count, alignment, targetName, context);
    }

    private static IReadOnlyList<short> ReadInt16Stream(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        int alignment,
        string targetName,
        DbLoadExecutionContext context)
    {
        byte[] bytes = ReadRawBytes(cursor, pointer, checked(count * sizeof(short)), alignment, targetName, context);
        var values = new short[count];
        var c = new FastFileCursor(bytes);
        for (int i = 0; i < values.Length; i++)
            values[i] = unchecked((short)c.ReadUInt16());

        return values;
    }

    private static IReadOnlyList<int> ReadInt32Stream(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        int alignment,
        string targetName,
        DbLoadExecutionContext context)
    {
        byte[] bytes = ReadRawBytes(cursor, pointer, checked(count * sizeof(int)), alignment, targetName, context);
        var values = new int[count];
        var c = new FastFileCursor(bytes);
        for (int i = 0; i < values.Length; i++)
            values[i] = c.ReadInt32();

        return values;
    }

    private static XAnimDynamicFrames ReadDynamicFrames(
        IReadOnlyList<byte> bytes,
        ushort numFrames,
        int frameCount)
    {
        var indices = new ushort[frameCount];
        var cursor = new FastFileCursor(bytes.ToArray());
        if (numFrames <= byte.MaxValue)
        {
            for (int i = 0; i < indices.Length; i++)
                indices[i] = cursor.ReadByte();
        }
        else
        {
            for (int i = 0; i < indices.Length; i++)
                indices[i] = cursor.ReadUInt16();
        }

        return new XAnimDynamicFrames
        {
            FrameIndices = indices,
            EncodedByteCount = bytes.Count
        };
    }

    private static XAnimTransFramePayload ReadTransFramePayload(
        IReadOnlyList<byte> bytes,
        byte smallTrans,
        int frameCount)
    {
        if (bytes.Count == 0)
            return new EmptyXAnimTransFramePayload();

        var cursor = new FastFileCursor(bytes.ToArray());
        if (smallTrans != 0)
        {
            var frames = new SmallXAnimTransFrame[frameCount];
            for (int i = 0; i < frames.Length; i++)
                frames[i] = new SmallXAnimTransFrame(cursor.ReadByte(), cursor.ReadByte(), cursor.ReadByte());

            return new SmallXAnimTransFramePayload { Frames = frames };
        }

        var fullFrames = new LargeXAnimTransFrame[frameCount];
        for (int i = 0; i < fullFrames.Length; i++)
        {
            fullFrames[i] = new LargeXAnimTransFrame(
                unchecked((short)cursor.ReadUInt16()),
                unchecked((short)cursor.ReadUInt16()),
                unchecked((short)cursor.ReadUInt16()));
        }

        return new LargeXAnimTransFramePayload { Frames = fullFrames };
    }

    private static IReadOnlyList<XQuat2> ReadQuat2Frames(ReadOnlySpan<byte> bytes, int count)
    {
        var cursor = new FastFileCursor(bytes.ToArray()); var values = new XQuat2[count];
        for (int index = 0; index < values.Length; index++) values[index] = new(unchecked((short)cursor.ReadUInt16()), unchecked((short)cursor.ReadUInt16()));
        return values;
    }

    private static IReadOnlyList<XQuat> ReadQuatFrames(ReadOnlySpan<byte> bytes, int count)
    {
        var cursor = new FastFileCursor(bytes.ToArray()); var values = new XQuat[count];
        for (int index = 0; index < values.Length; index++) values[index] = new(unchecked((short)cursor.ReadUInt16()), unchecked((short)cursor.ReadUInt16()), unchecked((short)cursor.ReadUInt16()), unchecked((short)cursor.ReadUInt16()));
        return values;
    }

    private static XBlockAddress PatchCurrentPayloadPointer(
        XPointerReference pointer,
        int alignment,
        int byteCount,
        string targetName,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange(pointer, byteCount, targetName);
            return pointer.PackedAddress ?? throw new InvalidDataException($"Offset pointer 0x{pointer.Raw:X8} has no packed address for {targetName}.");
        }

        if (pointer.Type != PointerType.Inline)
            throw new NotSupportedException($"XAnim {targetName} pointer 0x{pointer.Raw:X8} uses unsupported source sentinel {pointer.Type}.");

        return context.PointerReader.PatchInlinePointerCell(pointer, alignment);
    }

    private static int GetDynamicIndexByteCount(
        ushort numFrames,
        int frameCount)
    {
        return checked(frameCount * (numFrames <= byte.MaxValue ? 1 : 2));
    }

    private static string? ReadXString(
        FastFileCursor cursor,
        XString pointer,
        DbLoadExecutionContext context)
    {
        return context.PointerReader.LoadXString(cursor, pointer);
    }

    private static XPointer<T> ReadPointer<T>(
        FastFileCursor cursor,
        DbLoadExecutionContext context,
        XPointerResolutionMode mode) => context.PointerReader.ReadDeferredPointer<T>(cursor, mode);

    private static XString ReadXStringPointer(FastFileCursor cursor, DbLoadExecutionContext context) =>
        ReadPointer<string>(cursor, context, XPointerResolutionMode.Direct);

    private static XAnimVec3 ReadVec3(FastFileCursor cursor)
    {
        return new XAnimVec3(ReadSingle(cursor), ReadSingle(cursor), ReadSingle(cursor));
    }

    private static float ReadSingle(FastFileCursor cursor)
    {
        return BitConverter.Int32BitsToSingle(cursor.ReadInt32());
    }

    private static void PatchCanonicalPointerCell(
        XPointerReference pointer,
        XAnimPartsAsset canonical,
        DbLoadExecutionContext context)
    {
        XBlockAddress pointerCellAddress = pointer.CellAddress
            ?? throw new InvalidDataException("Packed XAnim pointer has no destination cell.");
        int canonicalRaw = canonical.RuntimeAddress?.RawValue
            ?? throw new InvalidDataException("Canonical XAnim has no runtime address.");
        context.Blocks.WriteInt32(pointerCellAddress, canonicalRaw);
    }
}
