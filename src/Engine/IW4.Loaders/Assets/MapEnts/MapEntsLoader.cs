using IW4.Loaders.Database;
using System.Text;
using IW4.Game.Assets.MapEnts;
using IW4.Game.Math;
using IW4.Game.Pointers;
using IW4.Game.Zone;
using IW4.Runtime.Database;
using IW4.Game.IO;

namespace IW4.Loaders.Assets.MapEnts;

public sealed class MapEntsLoader : XAssetLoader<MapEntsAsset>
{
    private readonly MapTriggersLoader _mapTriggersLoader = new();

    public MapEntsLoader() : base(XAssetType.MapEnts, MapEntsAsset.SerializedSize, "MapEnts")
    {
    }

    protected override MapEntsAsset? HandleUnresolvedReference(
        XPointerReference pointer, DbLoadExecutionContext context, bool requireAsset)
    {
        if (!requireAsset)
            return null;

        return base.HandleUnresolvedReference(pointer, context, requireAsset);
    }

    // The 0x2C-byte root is staged in TEMP. Its name, entity bytes, embedded
    // MapTriggers payloads, Stage rows, and Stage XStrings follow in LARGE.
    protected override MapEntsAsset ReadBody(
        FastFileCursor cursor,
        XBlockAddress expectedRootAddress,
        DbLoadExecutionContext context)
    {
        int sourceOffset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(
            cursor,
            MapEntsAsset.SerializedSize,
            out XBlockAddress rootAddress);
        if (rootAddress != expectedRootAddress)
        {
            throw new InvalidDataException(
                $"MapEnts pointer patched to {expectedRootAddress}, but root loaded at {rootAddress}.");
        }

        var rootCursor = new FastFileCursor(rootBytes, rootAddress);
        XPointer<string> namePointer = ReadPointer<string>(
            rootCursor,
            context,
            XPointerResolutionMode.Direct);
        XPointer<byte[]> entityStringPointer = ReadPointer<byte[]>(
            rootCursor,
            context,
            XPointerResolutionMode.Direct);
        int numEntityChars = rootCursor.ReadInt32();
        MapTriggers trigger = _mapTriggersLoader.ReadHeader(rootCursor, context);
        XPointer<Stage[]> stagesPointer = ReadPointer<Stage[]>(
            rootCursor,
            context,
            XPointerResolutionMode.Direct);
        byte stageCount = rootCursor.ReadByte();
        byte[] pad29To2B = rootCursor.ReadBytes(3);

        if (rootCursor.Offset != MapEntsAsset.SerializedSize)
        {
            throw new InvalidDataException(
                $"MapEnts consumed 0x{rootCursor.Offset:X} bytes instead of " +
                $"0x{MapEntsAsset.SerializedSize:X}.");
        }

        string? name;
        IReadOnlyList<byte> entityStringBytes;
        IReadOnlyList<Stage> stages;
        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            name = context.PointerReader.LoadXString(cursor, namePointer);
            entityStringBytes = ReadByteArray(
                cursor,
                entityStringPointer.Untyped,
                numEntityChars,
                context,
                "MapEnts.entityString");
            trigger = _mapTriggersLoader.LoadPayloads(cursor, trigger, context);
            stages = ReadStageArray(cursor, stagesPointer.Untyped, stageCount, context);
        }
        finally
        {
            context.Blocks.Pop();
        }

        return new MapEntsAsset
        {
            Offset = sourceOffset,
            RuntimeAddress = rootAddress,
            NamePointer = namePointer,
            Name = name,
            EntityStringPointer = entityStringPointer,
            EntityStringBytes = entityStringBytes,
            EntityString = entityStringBytes.Count == 0
                ? null
                : Encoding.Latin1.GetString(entityStringBytes.ToArray()).TrimEnd('\0'),
            NumEntityChars = numEntityChars,
            Trigger = trigger,
            StagesPointer = stagesPointer,
            Stages = stages,
            StageCount = stageCount,
            Pad29To2B = pad29To2B
        };
    }

    private static IReadOnlyList<Stage> ReadStageArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        (byte[] bytes, XBlockAddress address, bool hasPayload) = LoadArray<Stage[]>(
            cursor,
            pointer,
            count,
            Stage.SerializedSize,
            4,
            context,
            "MapEnts.stages");
        if (!hasPayload)
            return [];

        var rows = new Stage[count];
        for (int i = 0; i < rows.Length; i++)
        {
            var rowCursor = new FastFileCursor(bytes, address).Slice(
                checked(i * Stage.SerializedSize),
                Stage.SerializedSize);
            XPointer<string> stageNamePointer = ReadPointer<string>(
                rowCursor,
                context,
                XPointerResolutionMode.Direct);
            rows[i] = new Stage
            {
                StageNamePointer = stageNamePointer,
                StageName = context.PointerReader.LoadXString(cursor, stageNamePointer),
                Origin = ReadVec3(rowCursor),
                TriggerIndex = rowCursor.ReadUInt16(),
                SunPrimaryLightIndex = rowCursor.ReadByte(),
                Pad13 = rowCursor.ReadByte()
            };
        }

        return rows;
    }

    private static IReadOnlyList<byte> ReadByteArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        string memberName)
    {
        (byte[] bytes, _, bool hasPayload) = LoadArray<byte[]>(
            cursor,
            pointer,
            count,
            1,
            1,
            context,
            memberName);
        return hasPayload ? bytes : [];
    }

    private static (byte[] Bytes, XBlockAddress Address, bool HasPayload) LoadArray<T>(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        int stride,
        int alignment,
        DbLoadExecutionContext context,
        string memberName)
    {
        count = NonNegative(count, memberName);
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
                "MapEnts body only proves null/non-null inline array loading.");
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
            throw new InvalidDataException($"{memberName} pointer 0x{pointer.Raw:X8} is not null/inline/insert.");

        XBlockAddress targetAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment);
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
        DbLoadExecutionContext context,
        XPointerResolutionMode mode) => context.PointerReader.ReadPointer<T>(cursor, mode);


    private static Vec3 ReadVec3(FastFileCursor cursor) => new()
    {
        X = cursor.ReadSingle(),
        Y = cursor.ReadSingle(),
        Z = cursor.ReadSingle()
    };


    private static int NonNegative(int count, string name)
    {
        if (count < 0)
            throw new InvalidDataException($"{name} is negative: {count}.");

        return count;
    }
}
