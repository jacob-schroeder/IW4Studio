using IW4.Assets.Assets.GameMap;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Strings;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Model;

internal sealed class GameWorldMpLinkRecipe : AssetLinkRecipe
{
    private GameWorldMpLinkRecipe(
        AssetKey key,
        string originalSerializedName,
        GameWorldMpAsset definition,
        LinkAssetFreezeScope freeze)
        : base(
            key,
            originalSerializedName,
            freeze.FreezeProviderName(originalSerializedName, 0, "Asset.Name"))
    {
        LinkStorageSymbol glass = GameWorldGlassLinkStorage.Create(
            definition.GlassData!,
            freeze);
        var writer = new LinkTemplateWriter(GameWorldMpAsset.SerializedSize);
        writer.Skip(sizeof(int));
        writer.Skip(sizeof(int));
        Root = LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            writer.Complete(),
            alignment: 4,
            root => [
                NameOperation(root, 0),
                PresenceOperation(root, 0x04, glass, "GameWorldMp.GlassData")
            ]);
    }

    internal override LinkStorageSymbol Root { get; }

    public static AssetLinkRecipe Freeze(
        AssetKey key,
        string originalSerializedName,
        GameWorldMpAsset definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (originalSerializedName.StartsWith(','))
        {
            if (definition.GlassDataPointer.Raw != 0 ||
                definition.GlassData is not null)
            {
                throw new InvalidDataException(
                    "A comma-prefixed GameMapMp provider must have a zeroed reference body.");
            }
            return ExternalAssetLinkRecipe.Create(
                key,
                XAssetType.GameMapMp,
                originalSerializedName,
                freeze);
        }

        if (definition.GlassData is null)
            throw new InvalidDataException("An owned GameMapMp provider requires G_GlassData.");
        GameWorldGlassLinkStorage.Validate(definition.GlassData, "GameWorldMp.GlassData");
        return new GameWorldMpLinkRecipe(key, originalSerializedName, definition, freeze);
    }
}

/// <summary>Shared native G_GlassData graph used by SP and MP game worlds.</summary>
internal static class GameWorldGlassLinkStorage
{
    private const int MaximumCount = 0x100000;

    public static LinkStorageSymbol Create(
        GGlassData glass,
        LinkAssetFreezeScope freeze)
    {
        LinkStorageSymbol? pieces = CreatePieces(
            glass.GlassPieces,
            glass.GlassPiecesPointer.Type);
        LinkStorageSymbol? names = CreateNames(
            glass.GlassNames,
            glass.GlassNamesPointer.Type,
            freeze);
        var writer = new LinkTemplateWriter(GGlassData.SerializedSize);
        writer.Skip(sizeof(int));
        writer.WriteInt32(glass.PieceCount);
        writer.WriteUInt16(glass.DamageToWeaken);
        writer.WriteUInt16(glass.DamageToDestroy);
        writer.WriteInt32(glass.GlassNameCount);
        writer.Skip(sizeof(int));
        if (glass.Pad14To7F.Count == 0)
            writer.Skip(0x6c);
        else
            writer.WriteBytes(glass.Pad14To7F.ToArray());

        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.LARGE,
            writer.Complete(),
            alignment: 4,
            root => CreateRootOperations(root, pieces, names));
    }

    public static void Validate(GGlassData glass, string fieldPath)
    {
        IReadOnlyList<GGlassPiece> pieces = glass.GlassPieces ??
            throw new InvalidDataException($"{fieldPath}.GlassPieces cannot be null.");
        IReadOnlyList<GGlassName> names = glass.GlassNames ??
            throw new InvalidDataException($"{fieldPath}.GlassNames cannot be null.");
        if (glass.PieceCount < 0 ||
            glass.PieceCount > MaximumCount ||
            glass.PieceCount != pieces.Count)
        {
            throw new InvalidDataException(
                $"{fieldPath}.PieceCount must equal its bounded semantic piece count.");
        }
        if (glass.GlassNameCount < 0 ||
            glass.GlassNameCount > MaximumCount ||
            glass.GlassNameCount != names.Count)
        {
            throw new InvalidDataException(
                $"{fieldPath}.GlassNameCount must equal its bounded semantic name count.");
        }
        if (glass.Pad14To7F is null || glass.Pad14To7F.Count is not (0 or 0x6c))
            throw new InvalidDataException($"{fieldPath}.Pad14To7F must contain zero or 0x6C bytes.");
        for (int index = 0; index < pieces.Count; index++)
        {
            if (pieces[index] is null)
                throw new InvalidDataException($"{fieldPath}.GlassPieces[{index}] cannot be null.");
        }
        for (int index = 0; index < names.Count; index++)
        {
            GGlassName name = names[index] ?? throw new InvalidDataException(
                $"{fieldPath}.GlassNames[{index}] cannot be null.");
            if (name.Name is null)
                throw new InvalidDataException($"{fieldPath}.GlassNames[{index}].Name cannot be null.");
            IReadOnlyList<ushort> indices = name.PieceIndices ??
                throw new InvalidDataException(
                    $"{fieldPath}.GlassNames[{index}].PieceIndices cannot be null.");
            if (name.PieceCount != indices.Count)
            {
                throw new InvalidDataException(
                    $"{fieldPath}.GlassNames[{index}].PieceCount must equal its index count.");
            }
            if (indices.Any(piece => piece >= pieces.Count))
            {
                throw new InvalidDataException(
                    $"{fieldPath}.GlassNames[{index}] references a piece outside GlassPieces.");
            }
        }
    }

    private static IEnumerable<LinkOperation> CreateRootOperations(
        LinkStorageSymbol root,
        LinkStorageSymbol? pieces,
        LinkStorageSymbol? names)
    {
        if (pieces is not null)
            yield return Presence(root, 0x00, pieces, "G_GlassData.GlassPieces");
        if (names is not null)
            yield return Presence(root, 0x10, names, "G_GlassData.GlassNames");
    }

    private static LinkStorageSymbol? CreatePieces(
        IReadOnlyList<GGlassPiece> values,
        PointerType pointerType)
    {
        if (values.Count == 0 && pointerType == PointerType.Null)
            return null;
        var writer = new LinkTemplateWriter(checked(values.Count * GGlassPiece.SerializedSize));
        foreach (GGlassPiece piece in values)
        {
            writer.WriteUInt16(piece.DamageTaken);
            writer.WriteUInt16(piece.CollapseTime);
            writer.WriteInt32(piece.LastStateChangeTime);
            writer.WriteUInt16(piece.PackedImpactDir);
            writer.WriteUInt16(piece.PackedImpactPos);
        }
        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.LARGE,
            writer.Complete(),
            alignment: 4);
    }

    private static LinkStorageSymbol? CreateNames(
        IReadOnlyList<GGlassName> values,
        PointerType pointerType,
        LinkAssetFreezeScope freeze)
    {
        if (values.Count == 0 && pointerType == PointerType.Null)
            return null;
        var strings = new LinkStorageSymbol?[values.Count];
        var indices = new LinkStorageSymbol?[values.Count];
        var writer = new LinkTemplateWriter(checked(values.Count * GGlassName.SerializedSize));
        for (int index = 0; index < values.Count; index++)
        {
            GGlassName value = values[index];
            strings[index] = freeze.FreezeOptionalXString(
                value.NameStr,
                value.NameStrPointer.Untyped,
                $"G_GlassData.GlassNames[{index}].NameStr");
            indices[index] = CreateUInt16s(
                value.PieceIndices,
                value.PieceIndicesPointer.Type);
            writer.Skip(sizeof(int));
            writer.Skip(sizeof(ushort));
            writer.WriteUInt16(value.PieceCount);
            writer.Skip(sizeof(int));
        }

        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.LARGE,
            writer.Complete(),
            alignment: 4,
            table => CreateNameOperations(table, values, strings, indices));
    }

    private static IEnumerable<LinkOperation> CreateNameOperations(
        LinkStorageSymbol table,
        IReadOnlyList<GGlassName> values,
        IReadOnlyList<LinkStorageSymbol?> strings,
        IReadOnlyList<LinkStorageSymbol?> indices)
    {
        for (int index = 0; index < values.Count; index++)
        {
            int row = checked(index * GGlassName.SerializedSize);
            if (strings[index] is { } text)
            {
                yield return new XStringLinkOperation(
                    new LinkStorageCell(table, row),
                    LinkStorageView.Whole(text),
                    CanMaterializeRoot: true,
                    $"G_GlassData.GlassNames[{index}].NameStr");
            }
            ScriptStringReference script = values[index].Name;
            yield return new ScriptStringLinkOperation(
                new LinkStorageCell(table, checked(row + 0x04)),
                script,
                $"G_GlassData.GlassNames[{index}].Name");
            if (indices[index] is { } pieceIndices)
            {
                yield return Presence(
                    table,
                    checked(row + 0x08),
                    pieceIndices,
                    $"G_GlassData.GlassNames[{index}].PieceIndices");
            }
        }
    }

    private static LinkStorageSymbol? CreateUInt16s(
        IReadOnlyList<ushort> values,
        PointerType pointerType)
    {
        if (values.Count == 0 && pointerType == PointerType.Null)
            return null;
        var writer = new LinkTemplateWriter(checked(values.Count * sizeof(ushort)));
        foreach (ushort value in values)
            writer.WriteUInt16(value);
        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.LARGE,
            writer.Complete(),
            alignment: 2);
    }

    private static PresenceStorageLinkOperation Presence(
        LinkStorageSymbol owner,
        int pointerOffset,
        LinkStorageSymbol target,
        string fieldPath) =>
        new(
            new LinkStorageCell(owner, pointerOffset),
            LinkStorageView.Whole(target),
            fieldPath);
}
