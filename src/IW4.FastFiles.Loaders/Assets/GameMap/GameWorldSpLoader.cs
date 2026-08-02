using IW4.FastFiles.Loaders.Database;
using IW4.Assets.Assets.GameMap;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;

namespace IW4.FastFiles.Loaders.Assets.GameMap;

public sealed class GameWorldSpLoader
{
    private readonly PathDataLoader _pathDataLoader = new();
    private readonly VehicleTrackLoader _vehicleTrackLoader = new();
    private readonly GGlassDataLoader _glassDataLoader = new();

    public GameWorldSpAsset LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            throw new InvalidDataException("Top-level GameWorldSp pointer is null.");

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<GameWorldSpAsset>(
                pointer,
                GameWorldSpAsset.SerializedSize,
                "GameWorldSp");
            GameWorldSpAsset canonical = context.ResolveCanonicalAsset<GameWorldSpAsset>(
                    pointer,
                    XAssetType.GameMapSp)
                ?? throw new InvalidDataException(
                    $"Top-level GameWorldSp pointer 0x{unchecked((uint)pointer.Raw):X8} " +
                    "does not resolve to a canonical GameMapSp asset.");
            PatchCanonicalPointerCell(pointer, canonical, context);
            return canonical;
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
        {
            throw new InvalidDataException(
                $"GameWorldSp pointer 0x{unchecked((uint)pointer.Raw):X8} has unsupported type {pointer.Type}.");
        }

        XBlockAddress? insertCell = pointer.Type == PointerType.Insert
            ? context.Blocks.AllocateInsertPointerCell()
            : null;

        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            XBlockAddress rootAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            GameWorldSpAsset gameWorld = ReadGameWorldSp(cursor, rootAddress, context);
            XBlockAddress pointerCellAddress = pointer.CellAddress
                ?? throw new InvalidDataException("Inline GameWorldSp pointer has no destination cell.");
            GameWorldSpAsset canonical = context.DB_AddXAsset(
                XAssetType.GameMapSp,
                gameWorld.Name,
                gameWorld,
                pointerCellAddress);

            if (insertCell is { } cell)
            {
                int canonicalRaw = canonical.RuntimeAddress?.RawValue
                    ?? throw new InvalidDataException("Canonical GameWorldSp has no runtime address.");
                context.Blocks.WriteInt32(cell, canonicalRaw);
            }

            return canonical;
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    // The 0x38-byte root embeds PathData at +0x04, VehicleTrack at +0x2C,
    // and G_GlassData* at +0x34.
    private GameWorldSpAsset ReadGameWorldSp(
        FastFileCursor cursor,
        XBlockAddress expectedRootAddress,
        DbLoadExecutionContext context)
    {
        int sourceOffset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(
            cursor,
            GameWorldSpAsset.SerializedSize,
            out XBlockAddress rootAddress);
        if (rootAddress != expectedRootAddress)
        {
            throw new InvalidDataException(
                $"GameWorldSp pointer patched to {expectedRootAddress}, but root loaded at {rootAddress}.");
        }

        var rootCursor = new FastFileCursor(rootBytes, rootAddress);
        XPointer<string> namePointer = context.PointerReader.ReadPointer<string>(
            rootCursor,
            XPointerResolutionMode.Direct);
        PathData path = _pathDataLoader.ReadHeader(rootCursor);
        VehicleTrack vehicleTrack = _vehicleTrackLoader.ReadHeader(rootCursor);
        XPointer<GGlassData> glassDataPointer = ReadPresencePointer<GGlassData>(rootCursor);
        if (rootCursor.Offset != GameWorldSpAsset.SerializedSize)
        {
            throw new InvalidDataException(
                $"GameWorldSp consumed 0x{rootCursor.Offset:X} bytes instead of 0x{GameWorldSpAsset.SerializedSize:X}.");
        }

        string? name;
        GGlassData? glassData;
        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            name = context.PointerReader.LoadXString(cursor, namePointer);
            path = _pathDataLoader.LoadPayloads(cursor, path, context);
            vehicleTrack = _vehicleTrackLoader.LoadPayloads(cursor, vehicleTrack, context);
            glassData = _glassDataLoader.LoadFromPointer(
                cursor,
                glassDataPointer,
                context,
                "GameWorldSp.glassData");
        }
        finally
        {
            context.Blocks.Pop();
        }

        return new GameWorldSpAsset
        {
            Offset = sourceOffset,
            RuntimeAddress = rootAddress,
            NamePointer = namePointer,
            Name = name,
            Path = path,
            VehicleTrack = vehicleTrack,
            GlassDataPointer = glassDataPointer,
            GlassData = glassData
        };
    }

    private static XPointer<T> ReadPresencePointer<T>(FastFileCursor cursor)
    {
        int cellOffset = cursor.Offset;
        return new XPointer<T>(
            cursor.ReadInt32(),
            XPointerResolutionMode.Direct,
            cursor.AddressAt(cellOffset));
    }

    private static void PatchCanonicalPointerCell(
        XPointerReference pointer,
        GameWorldSpAsset canonical,
        DbLoadExecutionContext context)
    {
        XBlockAddress pointerCellAddress = pointer.CellAddress
            ?? throw new InvalidDataException("Packed GameWorldSp pointer has no destination cell.");
        int canonicalRaw = canonical.RuntimeAddress?.RawValue
            ?? throw new InvalidDataException("Canonical GameWorldSp has no runtime address.");
        context.Blocks.WriteInt32(pointerCellAddress, canonicalRaw);
    }
}
