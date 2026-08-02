using IW4.FastFiles.Loaders.Database;
using IW4.Assets.Assets.MapEnts;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;

namespace IW4.FastFiles.Loaders.Assets.MapEnts;

public sealed class MapTriggersLoader
{
    public MapTriggers ReadHeader(
        FastFileCursor cursor,
        DbLoadExecutionContext context)
    {
        return new MapTriggers
        {
            Count = cursor.ReadUInt32(),
            ModelsPointer = ReadPointer<TriggerModel[]>(cursor, context),
            HullCount = cursor.ReadUInt32(),
            HullsPointer = ReadPointer<TriggerHull[]>(cursor, context),
            SlabCount = cursor.ReadUInt32(),
            SlabsPointer = ReadPointer<TriggerSlab[]>(cursor, context)
        };
    }

    public MapTriggers LoadPayloads(
        FastFileCursor cursor,
        MapTriggers trigger,
        DbLoadExecutionContext context)
    {
        return new MapTriggers
        {
            Count = trigger.Count,
            ModelsPointer = trigger.ModelsPointer,
            Models = ReadTriggerModelArray(
                cursor,
                trigger.ModelsPointer.Untyped,
                Count(trigger.Count, "MapTriggers.count"),
                context),
            HullCount = trigger.HullCount,
            HullsPointer = trigger.HullsPointer,
            Hulls = ReadTriggerHullArray(
                cursor,
                trigger.HullsPointer.Untyped,
                Count(trigger.HullCount, "MapTriggers.hullCount"),
                context),
            SlabCount = trigger.SlabCount,
            SlabsPointer = trigger.SlabsPointer,
            Slabs = ReadTriggerSlabArray(
                cursor,
                trigger.SlabsPointer.Untyped,
                Count(trigger.SlabCount, "MapTriggers.slabCount"),
                context)
        };
    }

    private static IReadOnlyList<TriggerModel> ReadTriggerModelArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        (byte[] bytes, XBlockAddress address, bool hasPayload) = LoadArray<TriggerModel[]>(
            cursor,
            pointer,
            count,
            TriggerModel.SerializedSize,
            context,
            "MapTriggers.models");
        if (!hasPayload)
            return [];

        var rows = new TriggerModel[count];
        for (int i = 0; i < rows.Length; i++)
        {
            var rowCursor = RowCursor(bytes, address, i, TriggerModel.SerializedSize);
            rows[i] = new TriggerModel
            {
                Contents = rowCursor.ReadInt32(),
                HullCount = rowCursor.ReadUInt16(),
                FirstHull = rowCursor.ReadUInt16()
            };
        }

        return rows;
    }

    private static IReadOnlyList<TriggerHull> ReadTriggerHullArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        (byte[] bytes, XBlockAddress address, bool hasPayload) = LoadArray<TriggerHull[]>(
            cursor,
            pointer,
            count,
            TriggerHull.SerializedSize,
            context,
            "MapTriggers.hulls");
        if (!hasPayload)
            return [];

        var rows = new TriggerHull[count];
        for (int i = 0; i < rows.Length; i++)
        {
            var rowCursor = RowCursor(bytes, address, i, TriggerHull.SerializedSize);
            rows[i] = new TriggerHull
            {
                Bounds = ReadBounds(rowCursor),
                Contents = rowCursor.ReadInt32(),
                SlabCount = rowCursor.ReadUInt16(),
                FirstSlab = rowCursor.ReadUInt16()
            };
        }

        return rows;
    }

    private static IReadOnlyList<TriggerSlab> ReadTriggerSlabArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        (byte[] bytes, XBlockAddress address, bool hasPayload) = LoadArray<TriggerSlab[]>(
            cursor,
            pointer,
            count,
            TriggerSlab.SerializedSize,
            context,
            "MapTriggers.slabs");
        if (!hasPayload)
            return [];

        var rows = new TriggerSlab[count];
        for (int i = 0; i < rows.Length; i++)
        {
            var rowCursor = RowCursor(bytes, address, i, TriggerSlab.SerializedSize);
            rows[i] = new TriggerSlab
            {
                Dir = ReadVec3(rowCursor),
                MidPoint = ReadSingle(rowCursor),
                HalfSize = ReadSingle(rowCursor)
            };
        }

        return rows;
    }

    private static (byte[] Bytes, XBlockAddress Address, bool HasPayload) LoadArray<T>(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        int stride,
        DbLoadExecutionContext context,
        string memberName)
    {
        int byteCount = checked(count * stride);
        if (pointer.Type == PointerType.Null)
        {
            if (count != 0)
                throw new InvalidDataException($"{memberName} is null with non-zero count {count}.");

            return ([], context.Blocks.CurrentAddress, HasPayload: false);
        }

        if (pointer.Type == PointerType.Offset)
        {
            throw new InvalidDataException(
                $"{memberName} pointer 0x{pointer.Raw:X8} is packed, but the PS3 " +
                "MapTriggers path only proves null/non-null inline array loading.");
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
            throw new InvalidDataException($"{memberName} pointer 0x{pointer.Raw:X8} is not null/inline/insert.");

        XBlockAddress targetAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        byte[] bytes = context.Blocks.Load(cursor, byteCount, out XBlockAddress loadedAddress);
        if (loadedAddress != targetAddress)
        {
            throw new InvalidDataException(
                $"{memberName} pointer patched to {targetAddress}, but data loaded at {loadedAddress}.");
        }

        return (bytes, targetAddress, HasPayload: true);
    }

    private static XPointer<T> ReadPointer<T>(
        FastFileCursor cursor,
        DbLoadExecutionContext context) => context.PointerReader.ReadPointer<T>(
            cursor,
            XPointerResolutionMode.Direct);

    private static FastFileCursor RowCursor(
        byte[] bytes,
        XBlockAddress address,
        int index,
        int stride)
    {
        int offset = checked(index * stride);
        return new FastFileCursor(bytes.AsSpan(offset, stride).ToArray(), address.Add(offset));
    }

    private static Bounds ReadBounds(FastFileCursor cursor) => new()
    {
        MidPoint = ReadVec3(cursor),
        HalfSize = ReadVec3(cursor)
    };

    private static Vec3 ReadVec3(FastFileCursor cursor) => new()
    {
        X = ReadSingle(cursor),
        Y = ReadSingle(cursor),
        Z = ReadSingle(cursor)
    };

    private static float ReadSingle(FastFileCursor cursor) =>
        BitConverter.Int32BitsToSingle(cursor.ReadInt32());

    private static int Count(uint count, string name)
    {
        if (count > int.MaxValue)
            throw new InvalidDataException($"{name} exceeds Int32: 0x{count:X8}.");

        return (int)count;
    }
}
