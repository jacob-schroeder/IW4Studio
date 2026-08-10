using IW4.FastFiles.Loaders.Database;
using IW4.Assets.Assets.GameMap;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;

namespace IW4.FastFiles.Loaders.Assets.GameMap;

public sealed class GGlassDataLoader
{
    public GGlassData? LoadFromPointer(
        FastFileCursor cursor,
        XPointer<GGlassData> pointer,
        DbLoadExecutionContext context,
        string memberName)
    {
        if (pointer.Raw == 0)
            return null;

        XBlockAddress glassDataAddress = PatchPresenceCell(
            pointer.Untyped,
            alignment: 4,
            context,
            memberName);
        byte[] rootBytes = context.Blocks.Load(
            cursor,
            GGlassData.SerializedSize,
            out XBlockAddress loadedAddress);
        if (loadedAddress != glassDataAddress)
        {
            throw new InvalidDataException(
                $"G_GlassData pointer patched to {glassDataAddress}, but root loaded at {loadedAddress}.");
        }

        var rootCursor = new FastFileCursor(rootBytes, glassDataAddress);
        XPointer<GGlassPiece[]> glassPiecesPointer = ReadPresencePointer<GGlassPiece[]>(rootCursor, context);
        int pieceCount = rootCursor.ReadInt32();
        ushort damageToWeaken = rootCursor.ReadUInt16();
        ushort damageToDestroy = rootCursor.ReadUInt16();
        int glassNameCount = rootCursor.ReadInt32();
        XPointer<GGlassName[]> glassNamesPointer = ReadPresencePointer<GGlassName[]>(rootCursor, context);
        byte[] pad14To7F = rootCursor.ReadBytes(0x6C);

        if (rootCursor.Offset != GGlassData.SerializedSize)
        {
            throw new InvalidDataException(
                $"G_GlassData consumed 0x{rootCursor.Offset:X} bytes instead of 0x{GGlassData.SerializedSize:X}.");
        }

        ValidateCount(pieceCount, "G_GlassData.pieceCount");
        ValidateCount(glassNameCount, "G_GlassData.glassNameCount");
        IReadOnlyList<GGlassPiece> glassPieces = ReadGlassPieces(
            cursor,
            glassPiecesPointer,
            pieceCount,
            context);
        IReadOnlyList<GGlassName> glassNames = ReadGlassNames(
            cursor,
            glassNamesPointer,
            glassNameCount,
            context);

        return new GGlassData
        {
            Offset = glassDataAddress.Offset,
            GlassPiecesPointer = glassPiecesPointer,
            GlassPieces = glassPieces,
            PieceCount = pieceCount,
            DamageToWeaken = damageToWeaken,
            DamageToDestroy = damageToDestroy,
            GlassNameCount = glassNameCount,
            GlassNamesPointer = glassNamesPointer,
            GlassNames = glassNames,
            Pad14To7F = pad14To7F
        };
    }

    private static IReadOnlyList<GGlassPiece> ReadGlassPieces(
        FastFileCursor cursor,
        XPointer<GGlassPiece[]> pointer,
        int count,
        DbLoadExecutionContext context)
    {
        byte[] bytes = LoadPresenceArray(
            cursor,
            pointer.Untyped,
            count,
            GGlassPiece.SerializedSize,
            alignment: 4,
            context,
            "G_GlassData.glassPieces",
            out XBlockAddress piecesAddress);
        var rowCursor = new FastFileCursor(bytes, piecesAddress);
        var pieces = new GGlassPiece[count];
        for (int index = 0; index < pieces.Length; index++)
        {
            XBlockAddress rowAddress = piecesAddress.Add(rowCursor.Offset);
            pieces[index] = new GGlassPiece
            {
                Offset = rowAddress.Offset,
                DamageTaken = rowCursor.ReadUInt16(),
                CollapseTime = rowCursor.ReadUInt16(),
                LastStateChangeTime = rowCursor.ReadInt32(),
                PackedImpactDir = rowCursor.ReadUInt16(),
                PackedImpactPos = rowCursor.ReadUInt16()
            };
        }

        return pieces;
    }

    private static IReadOnlyList<GGlassName> ReadGlassNames(
        FastFileCursor cursor,
        XPointer<GGlassName[]> pointer,
        int count,
        DbLoadExecutionContext context)
    {
        byte[] bytes = LoadPresenceArray(
            cursor,
            pointer.Untyped,
            count,
            GGlassName.SerializedSize,
            alignment: 4,
            context,
            "G_GlassData.glassNames",
            out XBlockAddress namesAddress);
        var rowCursor = new FastFileCursor(bytes, namesAddress);
        var names = new GGlassName[count];
        for (int index = 0; index < names.Length; index++)
        {
            XBlockAddress rowAddress = namesAddress.Add(rowCursor.Offset);
            XPointer<string> nameStrPointer = context.PointerReader.ReadPointer<string>(
                rowCursor,
                XPointerResolutionMode.Direct);
            ushort name = rowCursor.ReadUInt16();
            ushort pieceCount = rowCursor.ReadUInt16();
            XPointer<ushort[]> pieceIndicesPointer = ReadPresencePointer<ushort[]>(rowCursor, context);
            string? nameStr = context.PointerReader.LoadXString(cursor, nameStrPointer);
            IReadOnlyList<ushort> pieceIndices = ReadUInt16Array(
                cursor,
                pieceIndicesPointer,
                pieceCount,
                context,
                $"G_GlassData.glassNames[{index}].pieceIndices");

            names[index] = new GGlassName
            {
                Offset = rowAddress.Offset,
                NameStrPointer = nameStrPointer,
                NameStr = nameStr,
                Name = name,
                PieceCount = pieceCount,
                PieceIndicesPointer = pieceIndicesPointer,
                PieceIndices = pieceIndices
            };
        }

        return names;
    }

    private static IReadOnlyList<ushort> ReadUInt16Array(
        FastFileCursor cursor,
        XPointer<ushort[]> pointer,
        int count,
        DbLoadExecutionContext context,
        string memberName)
    {
        byte[] bytes = LoadPresenceArray(
            cursor,
            pointer.Untyped,
            count,
            sizeof(ushort),
            alignment: 2,
            context,
            memberName,
            out XBlockAddress address);
        var valueCursor = new FastFileCursor(bytes, address);
        var values = new ushort[count];
        for (int index = 0; index < values.Length; index++)
            values[index] = valueCursor.ReadUInt16();
        return values;
    }

    private static byte[] LoadPresenceArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        int stride,
        int alignment,
        DbLoadExecutionContext context,
        string memberName,
        out XBlockAddress targetAddress)
    {
        ValidateCount(count, memberName);
        if (pointer.Raw == 0)
        {
            if (count != 0)
                throw new InvalidDataException($"{memberName} is null with non-zero count {count}.");
            targetAddress = context.Blocks.CurrentAddress;
            return [];
        }

        targetAddress = PatchPresenceCell(pointer, alignment, context, memberName);
        byte[] bytes = context.Blocks.Load(
            cursor,
            checked(count * stride),
            out XBlockAddress loadedAddress);
        if (loadedAddress != targetAddress)
            throw new InvalidDataException($"{memberName} patched to {targetAddress}, but loaded at {loadedAddress}.");
        return bytes;
    }

    private static XPointer<T> ReadPresencePointer<T>(FastFileCursor cursor, DbLoadExecutionContext context)
    {
        return context.PointerReader.ReadDeferredPointer<T>(cursor, XPointerResolutionMode.Direct);
    }

    private static XBlockAddress PatchPresenceCell(
        XPointerReference pointer,
        int alignment,
        DbLoadExecutionContext context,
        string memberName)
    {
        if (pointer.Raw == 0)
            throw new InvalidDataException($"{memberName} is null and cannot be materialized.");
        if (pointer.CellAddress is not { } cellAddress)
            throw new InvalidDataException($"{memberName} pointer has no destination cell.");

        context.Blocks.AlignCurrent(alignment);
        XBlockAddress targetAddress = context.Blocks.CurrentAddress;
        context.Blocks.WriteInt32(cellAddress, XPointerCodec.Encode(targetAddress));
        return targetAddress;
    }

    private static void ValidateCount(int count, string memberName)
    {
        if (count < 0 || count > 0x100000)
            throw new InvalidDataException($"{memberName} has invalid count {count}.");
    }
}
