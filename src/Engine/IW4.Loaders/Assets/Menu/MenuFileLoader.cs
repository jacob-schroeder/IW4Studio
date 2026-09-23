using IW4.Loaders.Database;
using IW4.Game.Assets.Menu;
using IW4.Game.Pointers;
using IW4.Game.Zone;
using IW4.Runtime.Database;
using IW4.Game.IO;

namespace IW4.Loaders.Assets.Menu;

public sealed class MenuFileLoader : XAssetLoader<MenuFileAsset>
{
    private const int MenuFileSize = MenuFileAsset.SerializedSize;

    public MenuFileLoader()
        : base(XAssetType.MenuFile, MenuFileSize, "MenuFile")
    {
    }

    protected override MenuFileAsset RegisterAsset(
        MenuFileAsset asset,
        ProviderRegistrationOccurrence providerRegistration,
        DbLoadExecutionContext context)
    {
        return context.DB_AddXAsset(asset, providerRegistration);
    }

    protected override MenuFileAsset ReadBody(
        FastFileCursor cursor,
        XBlockAddress targetAddress,
        DbLoadExecutionContext context)
    {
        int offset = cursor.Offset;
        ReadOnlyMemory<byte> rootBytes = context.Blocks.LoadMemory(cursor, MenuFileSize, out XBlockAddress rootAddress);
        if (rootAddress != targetAddress)
            throw new InvalidDataException($"MenuFile pointer patched to {targetAddress}, but root loaded at {rootAddress}.");
        var rootCursor = new FastFileCursor(rootBytes, rootAddress);

        XPointer<string> namePointer = MenuDefLoader.ReadXStringPointer(rootCursor, context);
        int menuCount = rootCursor.ReadInt32();
        XPointer<XPointer<MenuDefAsset>[]> menusPointer = MenuDefLoader.ReadCountedPointer<XPointer<MenuDefAsset>[]>(
            rootCursor,
            context,
            XPointerResolutionMode.Direct,
            menuCount,
            "MenuFile.menus");

        if (rootCursor.Offset != MenuFileSize)
            throw new InvalidDataException($"MenuFile consumed 0x{rootCursor.Offset:X} bytes instead of 0x{MenuFileSize:X}.");


        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            string? name = MenuDefLoader.ReadXString(cursor, namePointer, context);
            IReadOnlyList<MenuDefReference> menus = ReadMenuDefPointerArray(
                cursor,
                menusPointer.Untyped,
                menuCount,
                context);

            return new MenuFileAsset
            {
                Offset = offset,
                RuntimeAddress = rootAddress,
                NamePointer = namePointer,
                Name = name,
                MenuCount = menuCount,
                MenusPointer = menusPointer,
                Menus = menus
            };
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    internal static IReadOnlyList<MenuDefReference> ReadMenuDefPointerArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative MenuFile menu count {count}.");

        if (pointer.Type == PointerType.Null)
        {
            if (count == 0)
                return [];
            throw new InvalidDataException(
                $"MenuDef*[] has count {count}, but its pointer is null.");
        }

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<XPointer<MenuDefAsset>[]>(pointer, checked(count * sizeof(int)), "MenuDef*[]");
            return context.ResolveMaterializedDirect<MenuDefReference[]>(
                pointer,
                "MenuDef*[]");
        }

        if (!context.PointerReader.HasInlinePayload(pointer))
            throw MenuDefLoader.UnsupportedDirectTablePointer(pointer, "MenuDef*[]");

        MenuDefLoader.AlignStream(cursor, context, 4);
        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        ReadOnlyMemory<byte> pointerBytes = context.Blocks.LoadMemory(cursor, checked(count * sizeof(int)), out XBlockAddress pointerTableAddress);
        var pointerCursor = new FastFileCursor(pointerBytes, pointerTableAddress);
        var menus = new MenuDefReference[count];
        context.RegisterMaterialized(
            pointerTableAddress,
            menus,
            "MenuDef*[]");

        for (int i = 0; i < count; i++)
        {
            // Packed type-0x19 references point to a previously materialized
            // Menu pointer cell, not directly to a Menu root.
            XPointer<MenuDefAsset> typedMenuPointer = context.PointerReader.ReadPointer<MenuDefAsset>(
                pointerCursor,
                XPointerResolutionMode.AliasCell,
                XPointerNullability.Required);
            XPointerReference menuPointer = typedMenuPointer.Untyped;
            MenuDefAsset? menu = ReadMenuDefPointer(
                cursor,
                menuPointer,
                context,
                out MenuDefAsset? sourceMenu);
            menus[i] = new MenuDefReference(
                i,
                typedMenuPointer,
                menu)
            {
                SourceMenu = sourceMenu
            };
        }

        return menus;
    }

    private static MenuDefAsset? ReadMenuDefPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context,
        out MenuDefAsset? sourceMenu)
    {
        var loader = new MenuDefLoader();
        MenuDefAsset? canonical = loader.LoadFromPointer(cursor, pointer, context);
        sourceMenu = loader.SourceMenu;
        return canonical;
    }
}
