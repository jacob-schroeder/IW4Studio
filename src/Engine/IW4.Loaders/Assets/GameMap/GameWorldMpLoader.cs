using IW4.Loaders.Database;
using IW4.Game.Assets.GameMap;
using IW4.Game.Pointers;
using IW4.Game.Zone;
using IW4.Runtime.Database;
using IW4.Game.IO;

namespace IW4.Loaders.Assets.GameMap;

public sealed class GameWorldMpLoader : XAssetLoader<GameWorldMpAsset>
{
    private readonly GGlassDataLoader _glassDataLoader = new();

    protected override bool ValidatePackedPointerRange => false;

    public GameWorldMpLoader() : base(XAssetType.GameMapMp, GameWorldMpAsset.SerializedSize, "GameWorldMp")
    {
    }

    protected override GameWorldMpAsset ReadBody(
        FastFileCursor cursor,
        XBlockAddress expectedRootAddress,
        DbLoadExecutionContext context)
    {
        int sourceOffset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(
            cursor,
            GameWorldMpAsset.SerializedSize,
            out XBlockAddress rootAddress);
        if (rootAddress != expectedRootAddress)
        {
            throw new InvalidDataException(
                $"GameWorldMp pointer patched to {expectedRootAddress}, but root loaded at {rootAddress}.");
        }

        var rootCursor = new FastFileCursor(rootBytes, rootAddress);
        XPointer<string> namePointer = context.PointerReader.ReadPointer<string>(
            rootCursor,
            XPointerResolutionMode.Direct);
        XPointer<GGlassData> glassDataPointer = ReadPresencePointer<GGlassData>(rootCursor, context);
        if (rootCursor.Offset != GameWorldMpAsset.SerializedSize)
        {
            throw new InvalidDataException(
                $"GameWorldMp consumed 0x{rootCursor.Offset:X} bytes instead of 0x{GameWorldMpAsset.SerializedSize:X}.");
        }

        string? name;
        GGlassData? glassData;
        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            name = context.PointerReader.LoadXString(cursor, namePointer);
            glassData = _glassDataLoader.LoadFromPointer(
                cursor,
                glassDataPointer,
                context,
                "GameWorldMp.glassData");
        }
        finally
        {
            context.Blocks.Pop();
        }

        return new GameWorldMpAsset
        {
            Offset = sourceOffset,
            RuntimeAddress = rootAddress,
            NamePointer = namePointer,
            Name = name,
            GlassDataPointer = glassDataPointer,
            GlassData = glassData
        };
    }

    private static XPointer<T> ReadPresencePointer<T>(FastFileCursor cursor, DbLoadExecutionContext context)
    {
        return context.PointerReader.ReadDeferredPointer<T>(cursor, XPointerResolutionMode.Direct);
    }

    }
