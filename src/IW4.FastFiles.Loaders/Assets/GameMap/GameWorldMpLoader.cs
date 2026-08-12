using IW4.FastFiles.Loaders.Database;
using IW4.Assets.Assets.GameMap;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;

namespace IW4.FastFiles.Loaders.Assets.GameMap;

public sealed class GameWorldMpLoader
{
    private readonly GGlassDataLoader _glassDataLoader = new();

    public GameWorldMpAsset LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            throw new InvalidDataException("Top-level GameWorldMp pointer is null.");

        if (pointer.Type == PointerType.Offset)
        {
            GameWorldMpAsset canonical = context.ResolveCanonicalAsset<GameWorldMpAsset>(
                    pointer,
                    XAssetType.GameMapMp)
                ?? throw new InvalidDataException(
                    $"Top-level GameWorldMp pointer 0x{unchecked((uint)pointer.Raw):X8} " +
                    "does not resolve to a canonical GameMapMp asset.");
            context.PatchCanonicalAssetPointerCell(
                pointer,
                canonical,
                "Packed GameWorldMp pointer has no destination cell.",
                "Canonical GameWorldMp has no runtime address.");
            return canonical;
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
        {
            throw new InvalidDataException(
                $"Top-level GameWorldMp pointer 0x{pointer.Raw:X8} does not reference inline/insert payload data.");
        }

        ProviderRegistrationOccurrence providerRegistration = context.BeginProviderRegistration(pointer);

        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            XBlockAddress rootAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            GameWorldMpAsset gameWorld = ReadGameWorldMp(cursor, rootAddress, context);
            GameWorldMpAsset canonical = context.DB_AddXAsset(
                XAssetType.GameMapMp,
                gameWorld.Name,
                gameWorld,
                providerRegistration);

            return canonical;
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    private GameWorldMpAsset ReadGameWorldMp(
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
