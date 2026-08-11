using IW4.Assets.Assets.XAnim;
using IW4.FastFiles.Strings;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Model;

/// <summary>
/// Frozen XAnimParts module. Script-string cells are rebound through the
/// zone-wide table and every packed stream is materialized from semantic data.
/// </summary>
internal sealed class XAnimLinkRecipe : AssetLinkRecipe
{
    private const int BoneCountSlotCount = 10;

    private XAnimLinkRecipe(
        AssetKey key,
        string originalSerializedName,
        XAnimPartsAsset definition,
        LinkAssetFreezeScope freeze)
        : base(
            key,
            originalSerializedName,
            freeze.FreezeProviderName(originalSerializedName, 0, "Asset.Name"))
    {
        LinkStorageTarget? names = CreateScriptStringArray(
            definition.Names,
            definition.NamesPointer.Untyped,
            freeze,
            "XAnimParts.Names");
        LinkStorageTarget? notify = CreateNotifyArray(
            definition.Notify,
            definition.NotifyPointer.Untyped,
            freeze);
        LinkStorageTarget? delta = definition.DeltaPart is null
            ? null
            : CreateDelta(
                definition.DeltaPart,
                definition.DeltaPartPointer.Untyped,
                definition.NumFrames,
                freeze);
        LinkStorageTarget? dataBytes = CreateBytes(
            definition.PackedDataStreams.QuantizedBytes,
            definition.DataBytePointer.Untyped,
            alignment: 1,
            freeze,
            "XAnimParts.DataByte");
        LinkStorageTarget? dataShorts = CreateInt16s(
            definition.PackedDataStreams.QuantizedShorts,
            definition.DataShortPointer.Untyped,
            alignment: 2,
            freeze,
            "XAnimParts.DataShort");
        LinkStorageTarget? dataInts = CreateInt32s(
            definition.PackedDataStreams.QuantizedInts,
            definition.DataIntPointer.Untyped,
            alignment: 4,
            freeze,
            "XAnimParts.DataInt");
        LinkStorageTarget? randomShorts = CreateInt16s(
            definition.PackedDataStreams.RandomizedQuantizedShorts,
            definition.RandomDataShortPointer.Untyped,
            alignment: 2,
            freeze,
            "XAnimParts.RandomDataShort");
        LinkStorageTarget? randomBytes = CreateBytes(
            definition.PackedDataStreams.RandomizedQuantizedBytes,
            definition.RandomDataBytePointer.Untyped,
            alignment: 1,
            freeze,
            "XAnimParts.RandomDataByte");
        LinkStorageTarget? randomInts = CreateInt32s(
            definition.PackedDataStreams.RandomizedQuantizedInts,
            definition.RandomDataIntPointer.Untyped,
            alignment: 4,
            freeze,
            "XAnimParts.RandomDataInt");
        LinkStorageTarget? indices = CreateIndices(
            definition.Indices.FrameIndices,
            definition.IndicesPointer.Untyped,
            definition.NumFrames,
            freeze,
            "XAnimParts.Indices");

        var writer = new LinkTemplateWriter(XAnimPartsAsset.SerializedSize);
        writer.Skip(sizeof(int));
        writer.WriteUInt16(definition.DataByteCount);
        writer.WriteUInt16(definition.DataShortCount);
        writer.WriteUInt16(definition.DataIntCount);
        writer.WriteUInt16(definition.RandomDataByteCount);
        writer.WriteUInt16(definition.RandomDataIntCount);
        writer.WriteUInt16(definition.NumFrames);
        writer.WriteByte(definition.Flags);
        writer.WriteByte(definition.DeltaFlags);
        writer.WriteBytes(definition.BoneCounts.ToArray());
        writer.WriteByte(definition.BoneNameCount);
        writer.WriteByte(definition.NotifyCount);
        writer.WriteByte(definition.AssetType);
        writer.WriteByte(definition.Pad1F);
        writer.WriteInt32(definition.RandomDataShortCount);
        writer.WriteInt32(definition.IndexCount);
        writer.WriteInt32(BitConverter.SingleToInt32Bits(definition.Framerate));
        writer.WriteInt32(BitConverter.SingleToInt32Bits(definition.Frequency));
        writer.Skip(10 * sizeof(int));

        Root = LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            writer.Complete(),
            alignment: 4,
            root => CreateRootOperations(
                root,
                names,
                notify,
                delta,
                dataBytes,
                dataShorts,
                dataInts,
                randomShorts,
                randomBytes,
                randomInts,
                indices));
    }

    internal override LinkStorageSymbol Root { get; }

    public static AssetLinkRecipe Freeze(
        AssetKey key,
        string originalSerializedName,
        XAnimPartsAsset definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (originalSerializedName.StartsWith(','))
        {
            ValidateReferenceShape(definition);
            return ExternalAssetLinkRecipe.Create(
                key,
                XAssetType.XAnim,
                originalSerializedName,
                freeze);
        }

        ValidateOwned(definition);
        return new XAnimLinkRecipe(key, originalSerializedName, definition, freeze);
    }

    private IEnumerable<LinkOperation> CreateRootOperations(
        LinkStorageSymbol root,
        LinkStorageTarget? names,
        LinkStorageTarget? notify,
        LinkStorageTarget? delta,
        LinkStorageTarget? dataBytes,
        LinkStorageTarget? dataShorts,
        LinkStorageTarget? dataInts,
        LinkStorageTarget? randomShorts,
        LinkStorageTarget? randomBytes,
        LinkStorageTarget? randomInts,
        LinkStorageTarget? indices)
    {
        yield return NameOperation(root, 0);
        if (names is { } namesTarget)
            yield return Direct(root, 0x30, namesTarget, "XAnimParts.Names");
        if (notify is { } notifyTarget)
            yield return Direct(root, 0x50, notifyTarget, "XAnimParts.Notify");
        if (delta is { } deltaTarget)
            yield return Direct(root, 0x54, deltaTarget, "XAnimParts.DeltaPart");
        if (dataBytes is { } dataBytesTarget)
            yield return Direct(root, 0x34, dataBytesTarget, "XAnimParts.DataByte");
        if (dataShorts is { } dataShortsTarget)
            yield return Direct(root, 0x38, dataShortsTarget, "XAnimParts.DataShort");
        if (dataInts is { } dataIntsTarget)
            yield return Direct(root, 0x3c, dataIntsTarget, "XAnimParts.DataInt");
        if (randomShorts is { } randomShortsTarget)
            yield return Direct(root, 0x40, randomShortsTarget, "XAnimParts.RandomDataShort");
        if (randomBytes is { } randomBytesTarget)
            yield return Direct(root, 0x44, randomBytesTarget, "XAnimParts.RandomDataByte");
        if (randomInts is { } randomIntsTarget)
            yield return Direct(root, 0x48, randomIntsTarget, "XAnimParts.RandomDataInt");
        if (indices is { } indicesTarget)
            yield return Direct(root, 0x4c, indicesTarget, "XAnimParts.Indices");
    }

    private static LinkStorageTarget? CreateScriptStringArray(
        IReadOnlyList<ScriptStringReference> values,
        IW4.FastFiles.Pointers.XPointerReference pointer,
        LinkAssetFreezeScope freeze,
        string fieldPath)
    {
        if (values.Count == 0)
            return null;

        return freeze.FreezeStorage(
            pointer,
            new byte[checked(values.Count * sizeof(ushort))],
            XFileBlockType.LARGE,
            alignment: 2,
            (storage, baseAddend) => values.Select((value, index) =>
                new ScriptStringLinkOperation(
                    new LinkStorageCell(
                        storage,
                        checked(baseAddend + index * sizeof(ushort))),
                    value,
                    $"{fieldPath}[{index}]")),
            fieldPath);
    }

    private static LinkStorageTarget? CreateNotifyArray(
        IReadOnlyList<XAnimNotifyInfo> values,
        IW4.FastFiles.Pointers.XPointerReference pointer,
        LinkAssetFreezeScope freeze)
    {
        if (values.Count == 0)
            return null;

        var writer = new LinkTemplateWriter(
            checked(values.Count * XAnimNotifyInfo.SerializedSize));
        for (int index = 0; index < values.Count; index++)
        {
            XAnimNotifyInfo value = values[index] ?? throw new InvalidDataException(
                $"XAnimParts.Notify[{index}] cannot be null.");
            writer.Skip(sizeof(ushort));
            writer.Skip(sizeof(ushort));
            writer.WriteInt32(BitConverter.SingleToInt32Bits(value.Time));
        }

        return freeze.FreezeStorage(
            pointer,
            writer.Complete(),
            XFileBlockType.LARGE,
            alignment: 4,
            (storage, baseAddend) => values.Select((value, index) =>
                new ScriptStringLinkOperation(
                    new LinkStorageCell(
                        storage,
                        checked(baseAddend + index * XAnimNotifyInfo.SerializedSize)),
                    value.Name,
                    $"XAnimParts.Notify[{index}].Name")),
            "XAnimParts.Notify");
    }

    private static LinkStorageTarget CreateDelta(
        XAnimDeltaPart delta,
        IW4.FastFiles.Pointers.XPointerReference pointer,
        ushort numFrames,
        LinkAssetFreezeScope freeze)
    {
        LinkStorageTarget? trans = delta.Trans is null
            ? null
            : CreateTrans(delta.Trans, delta.TransPointer.Untyped, numFrames, freeze);
        LinkStorageTarget? quat2 = delta.Quat2 is null
            ? null
            : CreateQuat2(delta.Quat2, delta.Quat2Pointer.Untyped, numFrames, freeze);
        LinkStorageTarget? quat = delta.Quat is null
            ? null
            : CreateQuat(delta.Quat, delta.QuatPointer.Untyped, numFrames, freeze);

        return freeze.FreezeStorage(
            pointer,
            new byte[XAnimDeltaPart.SerializedSize],
            XFileBlockType.LARGE,
            alignment: 4,
            (root, baseAddend) => CreateOptionalDirectOperations(
                root,
                (baseAddend + 0x00, trans, "XAnimParts.DeltaPart.Trans"),
                (baseAddend + 0x04, quat2, "XAnimParts.DeltaPart.Quat2"),
                (baseAddend + 0x08, quat, "XAnimParts.DeltaPart.Quat")),
            "XAnimParts.DeltaPart");
    }

    private static LinkStorageTarget CreateTrans(
        XAnimPartTrans trans,
        IW4.FastFiles.Pointers.XPointerReference pointer,
        ushort numFrames,
        LinkAssetFreezeScope freeze)
    {
        var writer = new LinkTemplateWriter(XAnimPartTrans.SerializedSize);
        writer.WriteUInt16(trans.Size);
        writer.WriteByte(trans.SmallTrans);
        writer.WriteByte(trans.Pad3);

        LinkStorageSymbol child = trans.Size == 0
            ? CreateTransFrame0(trans.Frame0!)
            : CreateTransFrames(trans, numFrames, freeze);
        return freeze.FreezeStorage(
            pointer,
            writer.Complete(),
            XFileBlockType.LARGE,
            alignment: 4,
            (_, _) => [new MaterializeStorageLinkOperation(
                child,
                trans.Size == 0
                    ? "XAnimParts.DeltaPart.Trans.Frame0"
                    : "XAnimParts.DeltaPart.Trans.Frames")],
            "XAnimParts.DeltaPart.Trans");
    }

    private static LinkStorageSymbol CreateTransFrame0(XAnimPartTransFrame0 value)
    {
        var writer = new LinkTemplateWriter(XAnimPartTransFrame0.SerializedSize);
        writer.WriteInt32(BitConverter.SingleToInt32Bits(value.X));
        writer.WriteInt32(BitConverter.SingleToInt32Bits(value.Y));
        writer.WriteInt32(BitConverter.SingleToInt32Bits(value.Z));
        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.LARGE,
            writer.Complete(),
            alignment: 4);
    }

    private static LinkStorageSymbol CreateTransFrames(
        XAnimPartTrans trans,
        ushort numFrames,
        LinkAssetFreezeScope freeze)
    {
        XAnimPartTransFrames frames = trans.Frames!;
        LinkStorageSymbol dynamic = CreateFrameIndices(
            frames.DynamicFrames.FrameIndices,
            numFrames);
        LinkStorageTarget payload = trans.SmallTrans == 0
            ? CreateLargeTransFrames(
                ((LargeXAnimTransFramePayload)frames.FramePayload).Frames,
                frames.FramesPointer.Untyped,
                freeze,
                "XAnimParts.DeltaPart.Trans.Frames.Payload")
            : CreateSmallTransFrames(
                ((SmallXAnimTransFramePayload)frames.FramePayload).Frames,
                frames.FramesPointer.Untyped,
                freeze,
                "XAnimParts.DeltaPart.Trans.Frames.Payload");

        var writer = new LinkTemplateWriter(XAnimPartTransFrames.SerializedSize);
        WriteVec3(writer, frames.Mins);
        WriteVec3(writer, frames.Size);
        writer.Skip(sizeof(int));
        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.LARGE,
            writer.Complete(),
            alignment: 4,
            root => [
                new MaterializeStorageLinkOperation(
                    dynamic,
                    "XAnimParts.DeltaPart.Trans.Frames.DynamicFrames"),
                Direct(
                    root,
                    0x18,
                    payload,
                    "XAnimParts.DeltaPart.Trans.Frames.Payload")
            ]);
    }

    private static LinkStorageTarget CreateQuat2(
        XAnimDeltaPartQuat2 quat,
        IW4.FastFiles.Pointers.XPointerReference pointer,
        ushort numFrames,
        LinkAssetFreezeScope freeze)
    {
        var writer = new LinkTemplateWriter(XAnimDeltaPartQuat2.SerializedSize);
        writer.WriteUInt16(quat.Size);
        writer.WriteByte(quat.Pad2);
        writer.WriteByte(quat.Pad3);
        LinkStorageSymbol child = quat.Size == 0
            ? CreateQuat2Values([quat.Frame0!])
            : CreateQuat2Frames(quat.Frames!, numFrames, freeze);
        return freeze.FreezeStorage(
            pointer,
            writer.Complete(),
            XFileBlockType.LARGE,
            alignment: 4,
            (_, _) => [new MaterializeStorageLinkOperation(
                child,
                quat.Size == 0
                    ? "XAnimParts.DeltaPart.Quat2.Frame0"
                    : "XAnimParts.DeltaPart.Quat2.Frames")],
            "XAnimParts.DeltaPart.Quat2");
    }

    private static LinkStorageSymbol CreateQuat2Frames(
        XAnimDeltaPartQuatDataFrames2 frames,
        ushort numFrames,
        LinkAssetFreezeScope freeze)
    {
        LinkStorageSymbol dynamic = CreateFrameIndices(
            frames.DynamicFrames.FrameIndices,
            numFrames);
        LinkStorageTarget payload = CreateQuat2Values(
            frames.Frames,
            frames.FramesPointer.Untyped,
            freeze,
            "XAnimParts.DeltaPart.Quat2.Frames.Payload");
        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.LARGE,
            new byte[XAnimDeltaPartQuatDataFrames2.SerializedSize],
            alignment: 4,
            root => [
                new MaterializeStorageLinkOperation(
                    dynamic,
                    "XAnimParts.DeltaPart.Quat2.Frames.DynamicFrames"),
                Direct(
                    root,
                    0,
                    payload,
                    "XAnimParts.DeltaPart.Quat2.Frames.Payload")
            ]);
    }

    private static LinkStorageTarget CreateQuat(
        XAnimDeltaPartQuat quat,
        IW4.FastFiles.Pointers.XPointerReference pointer,
        ushort numFrames,
        LinkAssetFreezeScope freeze)
    {
        var writer = new LinkTemplateWriter(XAnimDeltaPartQuat.SerializedSize);
        writer.WriteUInt16(quat.Size);
        writer.WriteByte(quat.Pad2);
        writer.WriteByte(quat.Pad3);
        LinkStorageSymbol child = quat.Size == 0
            ? CreateQuatValues([quat.Frame0!])
            : CreateQuatFrames(quat.Frames!, numFrames, freeze);
        return freeze.FreezeStorage(
            pointer,
            writer.Complete(),
            XFileBlockType.LARGE,
            alignment: 4,
            (_, _) => [new MaterializeStorageLinkOperation(
                child,
                quat.Size == 0
                    ? "XAnimParts.DeltaPart.Quat.Frame0"
                    : "XAnimParts.DeltaPart.Quat.Frames")],
            "XAnimParts.DeltaPart.Quat");
    }

    private static LinkStorageSymbol CreateQuatFrames(
        XAnimDeltaPartQuatDataFrames frames,
        ushort numFrames,
        LinkAssetFreezeScope freeze)
    {
        LinkStorageSymbol dynamic = CreateFrameIndices(
            frames.DynamicFrames.FrameIndices,
            numFrames);
        LinkStorageTarget payload = CreateQuatValues(
            frames.Frames,
            frames.FramesPointer.Untyped,
            freeze,
            "XAnimParts.DeltaPart.Quat.Frames.Payload");
        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.LARGE,
            new byte[XAnimDeltaPartQuatDataFrames.SerializedSize],
            alignment: 4,
            root => [
                new MaterializeStorageLinkOperation(
                    dynamic,
                    "XAnimParts.DeltaPart.Quat.Frames.DynamicFrames"),
                Direct(
                    root,
                    0,
                    payload,
                    "XAnimParts.DeltaPart.Quat.Frames.Payload")
            ]);
    }

    private static LinkStorageSymbol CreateFrameIndices(
        IReadOnlyList<ushort> values,
        ushort numFrames) =>
        numFrames <= byte.MaxValue
            ? LinkStorageSymbol.SourceBytes(
                XFileBlockType.LARGE,
                values.Select(value => checked((byte)value)).ToArray(),
                alignment: 1)
            : CreateUInt16s(values, alignment: 2)!;

    private static LinkStorageTarget? CreateIndices(
        IReadOnlyList<ushort> values,
        IW4.FastFiles.Pointers.XPointerReference pointer,
        ushort numFrames,
        LinkAssetFreezeScope freeze,
        string fieldPath)
    {
        if (values.Count == 0)
            return null;
        byte[] bytes = numFrames <= byte.MaxValue
            ? values.Select(value => checked((byte)value)).ToArray()
            : EncodeUInt16s(values);
        return freeze.FreezeStorage(
            pointer,
            bytes,
            XFileBlockType.LARGE,
            numFrames <= byte.MaxValue ? 1 : 2,
            operations: null,
            fieldPath);
    }

    private static LinkStorageTarget CreateSmallTransFrames(
        IReadOnlyList<SmallXAnimTransFrame> values,
        IW4.FastFiles.Pointers.XPointerReference pointer,
        LinkAssetFreezeScope freeze,
        string fieldPath)
    {
        byte[] bytes = new byte[checked(values.Count * 3)];
        for (int index = 0; index < values.Count; index++)
        {
            SmallXAnimTransFrame value = values[index];
            int offset = index * 3;
            bytes[offset] = value.X;
            bytes[offset + 1] = value.Y;
            bytes[offset + 2] = value.Z;
        }
        return freeze.FreezeStorage(
            pointer,
            bytes,
            XFileBlockType.LARGE,
            alignment: 1,
            operations: null,
            fieldPath);
    }

    private static LinkStorageTarget CreateLargeTransFrames(
        IReadOnlyList<LargeXAnimTransFrame> values,
        IW4.FastFiles.Pointers.XPointerReference pointer,
        LinkAssetFreezeScope freeze,
        string fieldPath)
    {
        var writer = new LinkTemplateWriter(checked(values.Count * 6));
        foreach (LargeXAnimTransFrame value in values)
        {
            writer.WriteUInt16(unchecked((ushort)value.X));
            writer.WriteUInt16(unchecked((ushort)value.Y));
            writer.WriteUInt16(unchecked((ushort)value.Z));
        }
        return freeze.FreezeStorage(
            pointer,
            writer.Complete(),
            XFileBlockType.LARGE,
            alignment: 4,
            operations: null,
            fieldPath);
    }

    private static LinkStorageSymbol CreateQuat2Values(
        IReadOnlyList<XQuat2> values)
        => LinkStorageSymbol.SourceBytes(
            XFileBlockType.LARGE,
            EncodeQuat2Values(values),
            alignment: 4);

    private static LinkStorageTarget CreateQuat2Values(
        IReadOnlyList<XQuat2> values,
        IW4.FastFiles.Pointers.XPointerReference pointer,
        LinkAssetFreezeScope freeze,
        string fieldPath)
    {
        return freeze.FreezeStorage(
            pointer,
            EncodeQuat2Values(values),
            XFileBlockType.LARGE,
            alignment: 4,
            operations: null,
            fieldPath);
    }

    private static byte[] EncodeQuat2Values(IReadOnlyList<XQuat2> values)
    {
        var writer = new LinkTemplateWriter(
            checked(values.Count * XQuat2.SerializedSize));
        foreach (XQuat2 value in values)
        {
            writer.WriteUInt16(unchecked((ushort)value.Value0));
            writer.WriteUInt16(unchecked((ushort)value.Value1));
        }
        return writer.Complete();
    }

    private static LinkStorageSymbol CreateQuatValues(
        IReadOnlyList<XQuat> values)
        => LinkStorageSymbol.SourceBytes(
            XFileBlockType.LARGE,
            EncodeQuatValues(values),
            alignment: 4);

    private static LinkStorageTarget CreateQuatValues(
        IReadOnlyList<XQuat> values,
        IW4.FastFiles.Pointers.XPointerReference pointer,
        LinkAssetFreezeScope freeze,
        string fieldPath)
    {
        return freeze.FreezeStorage(
            pointer,
            EncodeQuatValues(values),
            XFileBlockType.LARGE,
            alignment: 4,
            operations: null,
            fieldPath);
    }

    private static byte[] EncodeQuatValues(IReadOnlyList<XQuat> values)
    {
        var writer = new LinkTemplateWriter(
            checked(values.Count * XQuat.SerializedSize));
        foreach (XQuat value in values)
        {
            writer.WriteUInt16(unchecked((ushort)value.Value0));
            writer.WriteUInt16(unchecked((ushort)value.Value1));
            writer.WriteUInt16(unchecked((ushort)value.Value2));
            writer.WriteUInt16(unchecked((ushort)value.Value3));
        }
        return writer.Complete();
    }

    private static LinkStorageTarget? CreateBytes(
        IReadOnlyList<byte> values,
        IW4.FastFiles.Pointers.XPointerReference pointer,
        int alignment,
        LinkAssetFreezeScope freeze,
        string fieldPath) =>
        values.Count == 0 ? null : freeze.FreezeStorage(
            pointer,
            values.ToArray(),
            XFileBlockType.LARGE,
            alignment,
            operations: null,
            fieldPath);

    private static LinkStorageTarget? CreateInt16s(
        IReadOnlyList<short> values,
        IW4.FastFiles.Pointers.XPointerReference pointer,
        int alignment,
        LinkAssetFreezeScope freeze,
        string fieldPath)
    {
        if (values.Count == 0)
            return null;
        var writer = new LinkTemplateWriter(checked(values.Count * sizeof(short)));
        foreach (short value in values)
            writer.WriteUInt16(unchecked((ushort)value));
        return freeze.FreezeStorage(
            pointer,
            writer.Complete(),
            XFileBlockType.LARGE,
            alignment,
            operations: null,
            fieldPath);
    }

    private static LinkStorageSymbol? CreateUInt16s(
        IReadOnlyList<ushort> values,
        int alignment)
    {
        if (values.Count == 0)
            return null;
        var writer = new LinkTemplateWriter(checked(values.Count * sizeof(ushort)));
        foreach (ushort value in values)
            writer.WriteUInt16(value);
        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.LARGE,
            writer.Complete(),
            alignment);
    }

    private static LinkStorageTarget? CreateInt32s(
        IReadOnlyList<int> values,
        IW4.FastFiles.Pointers.XPointerReference pointer,
        int alignment,
        LinkAssetFreezeScope freeze,
        string fieldPath)
    {
        if (values.Count == 0)
            return null;
        var writer = new LinkTemplateWriter(checked(values.Count * sizeof(int)));
        foreach (int value in values)
            writer.WriteInt32(value);
        return freeze.FreezeStorage(
            pointer,
            writer.Complete(),
            XFileBlockType.LARGE,
            alignment,
            operations: null,
            fieldPath);
    }

    private static IEnumerable<LinkOperation> CreateOptionalDirectOperations(
        LinkStorageSymbol owner,
        params (int Offset, LinkStorageTarget? Storage, string FieldPath)[] children)
    {
        foreach ((int offset, LinkStorageTarget? storage, string fieldPath) in children)
        {
            if (storage is { } target)
                yield return Direct(owner, offset, target, fieldPath);
        }
    }

    private static DirectStorageLinkOperation Direct(
        LinkStorageSymbol owner,
        int pointerOffset,
        LinkStorageTarget target,
        string fieldPath) =>
        new(
            new LinkStorageCell(owner, pointerOffset),
            target.View,
            target.CanMaterializeRoot,
            fieldPath);

    private static DirectStorageLinkOperation Direct(
        LinkStorageSymbol owner,
        int pointerOffset,
        LinkStorageSymbol target,
        string fieldPath) =>
        new(
            new LinkStorageCell(owner, pointerOffset),
            LinkStorageView.Whole(target),
            CanMaterializeRoot: true,
            fieldPath);

    private static byte[] EncodeUInt16s(IReadOnlyList<ushort> values)
    {
        var writer = new LinkTemplateWriter(checked(values.Count * sizeof(ushort)));
        foreach (ushort value in values)
            writer.WriteUInt16(value);
        return writer.Complete();
    }

    private static void WriteVec3(LinkTemplateWriter writer, XAnimVec3 value)
    {
        writer.WriteInt32(BitConverter.SingleToInt32Bits(value.X));
        writer.WriteInt32(BitConverter.SingleToInt32Bits(value.Y));
        writer.WriteInt32(BitConverter.SingleToInt32Bits(value.Z));
    }

    private static void ValidateOwned(XAnimPartsAsset definition)
    {
        IReadOnlyList<byte> boneCounts = definition.BoneCounts ??
            throw new InvalidDataException("XAnimParts.BoneCounts cannot be null.");
        IReadOnlyList<ScriptStringReference> names = definition.Names ??
            throw new InvalidDataException("XAnimParts.Names cannot be null.");
        IReadOnlyList<XAnimNotifyInfo> notify = definition.Notify ??
            throw new InvalidDataException("XAnimParts.Notify cannot be null.");
        XAnimPackedDataStreams streams = definition.PackedDataStreams ??
            throw new InvalidDataException("XAnimParts.PackedDataStreams cannot be null.");
        XAnimFrameIndexStream indices = definition.Indices ??
            throw new InvalidDataException("XAnimParts.Indices cannot be null.");

        if (boneCounts.Count != BoneCountSlotCount)
            throw new InvalidDataException("XAnimParts requires exactly ten bone-count bytes.");
        RequireCount(definition.BoneNameCount, names.Count, "XAnimParts.BoneNameCount");
        RequireCount(definition.NotifyCount, notify.Count, "XAnimParts.NotifyCount");
        RequireCount(definition.DataByteCount, streams.QuantizedBytes.Count, "XAnimParts.DataByteCount");
        RequireCount(definition.DataShortCount, streams.QuantizedShorts.Count, "XAnimParts.DataShortCount");
        RequireCount(definition.DataIntCount, streams.QuantizedInts.Count, "XAnimParts.DataIntCount");
        RequireCount(definition.RandomDataByteCount, streams.RandomizedQuantizedBytes.Count, "XAnimParts.RandomDataByteCount");
        RequireCount(definition.RandomDataIntCount, streams.RandomizedQuantizedInts.Count, "XAnimParts.RandomDataIntCount");
        RequireCount(definition.RandomDataShortCount, streams.RandomizedQuantizedShorts.Count, "XAnimParts.RandomDataShortCount");
        RequireCount(definition.IndexCount, indices.FrameIndices.Count, "XAnimParts.IndexCount");

        for (int index = 0; index < names.Count; index++)
        {
            if (names[index] is null)
                throw new InvalidDataException($"XAnimParts.Names[{index}] cannot be null.");
        }
        for (int index = 0; index < notify.Count; index++)
        {
            XAnimNotifyInfo value = notify[index] ?? throw new InvalidDataException(
                $"XAnimParts.Notify[{index}] cannot be null.");
            if (value.Name is null)
                throw new InvalidDataException($"XAnimParts.Notify[{index}].Name cannot be null.");
        }

        ValidateIndexValues(indices.FrameIndices, definition.NumFrames, "XAnimParts.Indices");
        if (definition.DeltaPart is not null)
            ValidateDelta(definition.DeltaPart, definition.NumFrames);
    }

    private static void ValidateDelta(XAnimDeltaPart delta, ushort numFrames)
    {
        if (delta.Trans is not null)
            ValidateTrans(delta.Trans, numFrames);
        if (delta.Quat2 is not null)
            ValidateQuat2(delta.Quat2, numFrames);
        if (delta.Quat is not null)
            ValidateQuat(delta.Quat, numFrames);
    }

    private static void ValidateTrans(XAnimPartTrans trans, ushort numFrames)
    {
        if (trans.Size == 0)
        {
            if (trans.Frame0 is null || trans.Frames is not null)
            {
                throw new InvalidDataException(
                    "Size-zero XAnim translation requires Frame0 and no dynamic frames.");
            }
            return;
        }

        if (trans.Frame0 is not null || trans.Frames is null)
        {
            throw new InvalidDataException(
                "Dynamic XAnim translation requires Frames and no Frame0.");
        }

        int count = checked(trans.Size + 1);
        XAnimPartTransFrames frames = trans.Frames;
        ValidateDynamicFrames(frames.DynamicFrames, count, numFrames, "XAnimParts.DeltaPart.Trans");
        if (trans.SmallTrans == 0)
        {
            if (frames.FramePayload is not LargeXAnimTransFramePayload large ||
                large.Frames.Count != count)
            {
                throw new InvalidDataException(
                    "Large XAnim translation payload count must equal Size + 1.");
            }
        }
        else if (frames.FramePayload is not SmallXAnimTransFramePayload small ||
            small.Frames.Count != count)
        {
            throw new InvalidDataException(
                "Small XAnim translation payload count must equal Size + 1.");
        }
    }

    private static void ValidateQuat2(XAnimDeltaPartQuat2 quat, ushort numFrames)
    {
        if (quat.Size == 0)
        {
            if (quat.Frame0 is null || quat.Frames is not null)
            {
                throw new InvalidDataException(
                    "Size-zero XAnim quat2 requires Frame0 and no dynamic frames.");
            }
            return;
        }

        if (quat.Frame0 is not null || quat.Frames is null)
        {
            throw new InvalidDataException(
                "Dynamic XAnim quat2 requires Frames and no Frame0.");
        }
        int count = checked(quat.Size + 1);
        if (quat.Frames.Frames.Count != count)
            throw new InvalidDataException("XAnim quat2 frame count must equal Size + 1.");
        ValidateDynamicFrames(
            quat.Frames.DynamicFrames,
            count,
            numFrames,
            "XAnimParts.DeltaPart.Quat2");
    }

    private static void ValidateQuat(XAnimDeltaPartQuat quat, ushort numFrames)
    {
        if (quat.Size == 0)
        {
            if (quat.Frame0 is null || quat.Frames is not null)
            {
                throw new InvalidDataException(
                    "Size-zero XAnim quat requires Frame0 and no dynamic frames.");
            }
            return;
        }

        if (quat.Frame0 is not null || quat.Frames is null)
        {
            throw new InvalidDataException(
                "Dynamic XAnim quat requires Frames and no Frame0.");
        }
        int count = checked(quat.Size + 1);
        if (quat.Frames.Frames.Count != count)
            throw new InvalidDataException("XAnim quat frame count must equal Size + 1.");
        ValidateDynamicFrames(
            quat.Frames.DynamicFrames,
            count,
            numFrames,
            "XAnimParts.DeltaPart.Quat");
    }

    private static void ValidateDynamicFrames(
        XAnimDynamicFrames frames,
        int expectedCount,
        ushort numFrames,
        string fieldPath)
    {
        if (frames is null)
            throw new InvalidDataException($"{fieldPath}.DynamicFrames cannot be null.");
        if (frames.FrameIndices.Count != expectedCount)
        {
            throw new InvalidDataException(
                $"{fieldPath}.DynamicFrames count must equal Size + 1.");
        }
        ValidateIndexValues(frames.FrameIndices, numFrames, $"{fieldPath}.DynamicFrames");
    }

    private static void ValidateIndexValues(
        IReadOnlyList<ushort> values,
        ushort numFrames,
        string fieldPath)
    {
        if (numFrames <= byte.MaxValue && values.Any(value => value > byte.MaxValue))
        {
            throw new InvalidDataException(
                $"{fieldPath} contains an index that cannot use the native byte encoding.");
        }
    }

    private static void RequireCount(int declared, int actual, string fieldPath)
    {
        if (declared < 0 || declared != actual)
        {
            throw new InvalidDataException(
                $"{fieldPath} ({declared}) must equal its semantic element count ({actual}).");
        }
    }

    private static void ValidateReferenceShape(XAnimPartsAsset definition)
    {
        XAnimPackedDataStreams streams = definition.PackedDataStreams ?? new();
        XAnimFrameIndexStream indices = definition.Indices ?? new();
        bool boneCountsZero = definition.BoneCounts is { Count: 0 } ||
            definition.BoneCounts is { Count: BoneCountSlotCount } &&
            definition.BoneCounts.All(value => value == 0);
        if (definition.DataByteCount != 0 ||
            definition.DataShortCount != 0 ||
            definition.DataIntCount != 0 ||
            definition.RandomDataByteCount != 0 ||
            definition.RandomDataIntCount != 0 ||
            definition.NumFrames != 0 ||
            definition.Flags != 0 ||
            definition.DeltaFlags != 0 ||
            !boneCountsZero ||
            definition.BoneNameCount != 0 ||
            definition.NotifyCount != 0 ||
            definition.AssetType != 0 ||
            definition.Pad1F != 0 ||
            definition.RandomDataShortCount != 0 ||
            definition.IndexCount != 0 ||
            BitConverter.SingleToInt32Bits(definition.Framerate) != 0 ||
            BitConverter.SingleToInt32Bits(definition.Frequency) != 0 ||
            definition.Names.Count != 0 ||
            definition.Notify.Count != 0 ||
            definition.DeltaPart is not null ||
            streams.QuantizedBytes.Count != 0 ||
            streams.QuantizedShorts.Count != 0 ||
            streams.QuantizedInts.Count != 0 ||
            streams.RandomizedQuantizedShorts.Count != 0 ||
            streams.RandomizedQuantizedBytes.Count != 0 ||
            streams.RandomizedQuantizedInts.Count != 0 ||
            indices.FrameIndices.Count != 0)
        {
            throw new InvalidDataException(
                "A comma-prefixed XAnim provider must have a zeroed reference body.");
        }
    }
}
