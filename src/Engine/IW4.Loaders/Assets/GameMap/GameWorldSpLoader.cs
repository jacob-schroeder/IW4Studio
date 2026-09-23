using IW4.Loaders.Database;
using IW4.Game.Assets.GameMap;
using IW4.Game.Pointers;
using IW4.Game.Zone;
using IW4.Runtime.Database;
using IW4.Game.IO;

namespace IW4.Loaders.Assets.GameMap;

public sealed class GameWorldSpLoader : XAssetLoader<GameWorldSpAsset>
{
    private readonly PathDataLoader _pathDataLoader = new();
    private readonly VehicleTrackLoader _vehicleTrackLoader = new();
    private readonly GGlassDataLoader _glassDataLoader = new();

    public GameWorldSpLoader() : base(XAssetType.GameMapSp, GameWorldSpAsset.SerializedSize, "GameWorldSp")
    {
    }

    // The 0x38-byte root embeds PathData at +0x04, VehicleTrack at +0x2C,
    // and G_GlassData* at +0x34.
    protected override GameWorldSpAsset ReadBody(
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
        PathData path = _pathDataLoader.ReadHeader(rootCursor, context);
        VehicleTrack vehicleTrack = _vehicleTrackLoader.ReadHeader(rootCursor, context);
        XPointer<GGlassData> glassDataPointer = ReadPresencePointer<GGlassData>(rootCursor, context);
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

    private static XPointer<T> ReadPresencePointer<T>(FastFileCursor cursor, DbLoadExecutionContext context)
    {
        return context.PointerReader.ReadDeferredPointer<T>(cursor, XPointerResolutionMode.Direct);
    }

    }
