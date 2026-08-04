using IW4.FastFiles.Loaders.Database;
using System.Buffers.Binary;
using IW4.FastFiles.Loaders.Assets.Material;
using IW4.FastFiles.Loaders.Assets.Sound;
using IW4.Assets.Assets.Material;
using SoundAliasListAssetModel = IW4.Assets.Assets.Sound.SoundAliasListAsset;
using IW4.Assets.Assets.Menu;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;

namespace IW4.FastFiles.Loaders.Assets.Menu;

public sealed class MenuFileLoader
{
    private const int MenuFileSize = MenuFileAsset.SerializedSize;
    private const int MenuDefSize = MenuDefAsset.SerializedSize;
    private const int ItemDefSize = ItemDefAsset.SerializedSize;
    private static readonly MaterialLoader MaterialLoader = new();
    private static readonly SoundAliasListLoader SoundAliasListLoader = new();

    // MenuFile children and top-level Menu assets share this pointer path.
    public MenuDefAsset LoadMenuFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        MenuDefLoadResult result = ReadMenuDefPointer(cursor, pointer, context);
        return result.CanonicalMenu ?? result.IncomingDefinition
            ?? throw new InvalidDataException("Top-level Menu pointer resolved to null.");
    }

    public MenuFileAsset LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            throw new InvalidDataException("Top-level MenuFile pointer is null.");

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<MenuFileAsset>(
                pointer,
                MenuFileAsset.SerializedSize,
                "MenuFile");
            MenuFileAsset canonical = context.ResolveMenuFile(pointer)
                ?? throw new InvalidDataException(
                    $"Top-level MenuFile pointer 0x{unchecked((uint)pointer.Raw):X8} does not resolve to a canonical MenuFile asset.");
            XBlockAddress pointerCellAddress = pointer.CellAddress
                ?? throw new InvalidDataException("Packed MenuFile pointer has no destination cell.");
            int canonicalRaw = canonical.RuntimeAddress?.RawValue
                ?? throw new InvalidDataException("Canonical MenuFile has no runtime address.");
            context.Blocks.WriteInt32(pointerCellAddress, canonicalRaw);
            return canonical;
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
            throw new InvalidDataException(
                $"Top-level MenuFile pointer 0x{unchecked((uint)pointer.Raw):X8} has unsupported type {pointer.Type}.");

        XBlockAddress? insertCell = pointer.Type == PointerType.Insert
            ? context.Blocks.AllocateInsertPointerCell()
            : null;

        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            XBlockAddress rootAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            MenuFileAsset menuFile = ReadMenuFile(cursor, rootAddress, context);
            XBlockAddress pointerCellAddress = pointer.CellAddress
                ?? throw new InvalidDataException("Inline MenuFile pointer has no destination cell.");
            MenuFileAsset canonical = context.DB_AddXAsset(menuFile, pointerCellAddress);

            if (insertCell is { } cell)
            {
                int canonicalRaw = canonical.RuntimeAddress?.RawValue
                    ?? throw new InvalidDataException("Canonical MenuFile has no runtime address.");
                context.Blocks.WriteInt32(cell, canonicalRaw);
            }

            return canonical;
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    private static MenuFileAsset ReadMenuFile(
        FastFileCursor cursor,
        XBlockAddress targetAddress,
        DbLoadExecutionContext context)
    {
        int offset = cursor.Offset;
        ReadOnlyMemory<byte> rootBytes = context.Blocks.LoadMemory(cursor, MenuFileSize, out XBlockAddress rootAddress);
        if (rootAddress != targetAddress)
            throw new InvalidDataException($"MenuFile pointer patched to {targetAddress}, but root loaded at {rootAddress}.");
        var rootCursor = new FastFileCursor(rootBytes, rootAddress);

        XPointer<string> namePointer = ReadXStringPointer(rootCursor, context);
        int menuCount = rootCursor.ReadInt32();
        XPointer<XPointer<MenuDefAsset>[]> menusPointer = ReadCountedPointer<XPointer<MenuDefAsset>[]>(
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
            string? name = ReadXString(cursor, namePointer, context);
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
            throw UnsupportedDirectTablePointer(pointer, "MenuDef*[]");

        AlignStream(cursor, context, 4);
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
                XPointerOffsetMode.AliasCell,
                XPointerNullability.Required);
            XPointerReference menuPointer = typedMenuPointer.Untyped;
            MenuDefLoadResult menu = ReadMenuDefPointer(cursor, menuPointer, context);
            menus[i] = new MenuDefReference(
                i,
                typedMenuPointer,
                menu.IncomingDefinition,
                menu.CanonicalMenu);
        }

        return menus;
    }

    private static MenuDefLoadResult ReadMenuDefPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            return new MenuDefLoadResult(null, null);

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<MenuDefAsset>(pointer, MenuDefSize, "MenuDef");
            MenuDefAsset canonical = context.ResolveMenuDef(pointer)
                ?? throw new InvalidDataException(
                    $"MenuDef pointer 0x{unchecked((uint)pointer.Raw):X8} does not resolve to a canonical type-0x19 asset.");
            XBlockAddress pointerCellAddress = pointer.CellAddress
                ?? throw new InvalidDataException("Packed MenuDef pointer has no destination cell.");
            int canonicalRaw = canonical.RuntimeAddress?.RawValue
                ?? throw new InvalidDataException("Canonical MenuDef has no runtime address.");
            context.Blocks.WriteInt32(pointerCellAddress, canonicalRaw);
            return new MenuDefLoadResult(null, canonical);
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
        {
            throw new InvalidDataException(
                $"MenuDef pointer 0x{unchecked((uint)pointer.Raw):X8} has unsupported type {pointer.Type}.");
        }

        XBlockAddress? insertCell = pointer.Type == PointerType.Insert
            ? context.Blocks.AllocateInsertPointerCell()
            : null;

        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            XBlockAddress targetAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            MenuDefAsset menu = ReadMenuDefRoot(cursor, targetAddress, context);
            context.Blocks.Push(XFileBlockType.LARGE);
            try
            {
                ReadMenuDefChildren(cursor, menu, context);
            }
            finally
            {
                context.Blocks.Pop();
            }

            XBlockAddress pointerCellAddress = pointer.CellAddress
                ?? throw new InvalidDataException("Inline MenuDef pointer has no destination cell.");
            MenuDefAsset canonical = context.DB_AddXAsset(menu, pointerCellAddress);

            if (insertCell is { } cell)
            {
                int canonicalRaw = canonical.RuntimeAddress?.RawValue
                    ?? throw new InvalidDataException("Canonical MenuDef has no runtime address.");
                context.Blocks.WriteInt32(cell, canonicalRaw);
            }

            return new MenuDefLoadResult(menu, canonical);
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    private sealed record MenuDefLoadResult(
        MenuDefAsset? IncomingDefinition,
        MenuDefAsset? CanonicalMenu);

    private static MenuDefAsset ReadMenuDefRoot(
        FastFileCursor cursor,
        XBlockAddress targetAddress,
        DbLoadExecutionContext context)
    {
        int offset = cursor.Offset;
        ReadOnlyMemory<byte> rootBytes = context.Blocks.LoadMemory(cursor, MenuDefSize, out XBlockAddress rootAddress);
        if (rootAddress != targetAddress)
            throw new InvalidDataException($"MenuDef pointer patched to {targetAddress}, but root loaded at {rootAddress}.");
        var rootCursor = new FastFileCursor(rootBytes, rootAddress);

        WindowDef window = ReadWindow(rootCursor, context);
        XPointer<string> fontPointer = ReadXStringPointer(rootCursor, context);
        int fullscreen = rootCursor.ReadInt32();
        int itemCount = rootCursor.ReadInt32();

        var menu = new MenuDefAsset
        {
            Offset = offset,
            RuntimeAddress = rootAddress,
            Window = window,
            FontPointer = fontPointer,
            Fullscreen = fullscreen,
            ItemCount = itemCount,
            FontIndex = rootCursor.ReadInt32(),
            CursorItems = ReadInt32Array(rootCursor, 4),
            FadeCycle = rootCursor.ReadInt32(),
            FadeClamp = ReadSingle(rootCursor),
            FadeAmount = ReadSingle(rootCursor),
            FadeInAmount = ReadSingle(rootCursor),
            BlurRadius = ReadSingle(rootCursor),
            OnOpen = ReadNullablePointer<MenuEventHandlerSet>(rootCursor, context, XPointerResolutionMode.Direct),
            OnCloseRequest = ReadNullablePointer<MenuEventHandlerSet>(rootCursor, context, XPointerResolutionMode.Direct),
            OnClose = ReadNullablePointer<MenuEventHandlerSet>(rootCursor, context, XPointerResolutionMode.Direct),
            OnEsc = ReadNullablePointer<MenuEventHandlerSet>(rootCursor, context, XPointerResolutionMode.Direct),
            ExecKeys = ReadNullablePointer<ItemKeyHandler>(rootCursor, context, XPointerResolutionMode.Direct),
            VisibleExpression = ReadNullablePointer<Statement>(rootCursor, context, XPointerResolutionMode.Direct),
            AllowedBinding = ReadXStringPointer(rootCursor, context),
            SoundName = ReadXStringPointer(rootCursor, context),
            ImageTrack = rootCursor.ReadInt32(),
            FocusColor = ReadVec4(rootCursor),
            RectXExpression = ReadNullablePointer<Statement>(rootCursor, context, XPointerResolutionMode.Direct),
            RectYExpression = ReadNullablePointer<Statement>(rootCursor, context, XPointerResolutionMode.Direct),
            RectWExpression = ReadNullablePointer<Statement>(rootCursor, context, XPointerResolutionMode.Direct),
            RectHExpression = ReadNullablePointer<Statement>(rootCursor, context, XPointerResolutionMode.Direct),
            ItemsPointer = ReadCountedPointer<XPointer<ItemDefAsset>[]>(
                rootCursor,
                context,
                XPointerResolutionMode.Direct,
                itemCount,
                "MenuDef.items"),
            ScaleTransitions = ReadMenuTransitions(rootCursor, 4),
            AlphaTransitions = ReadMenuTransitions(rootCursor, 4),
            XTransitions = ReadMenuTransitions(rootCursor, 4),
            YTransitions = ReadMenuTransitions(rootCursor, 4),
            ExpressionData = ReadNullablePointer<ExpressionSupportingData>(rootCursor, context, XPointerResolutionMode.Direct)
        };

        if (rootCursor.Offset != MenuDefSize)
            throw new InvalidDataException($"MenuDef consumed 0x{rootCursor.Offset:X} bytes instead of 0x{MenuDefSize:X}.");


        return menu;
    }

    private static void ReadMenuDefChildren(
        FastFileCursor cursor,
        MenuDefAsset menu,
        DbLoadExecutionContext context)
    {
        menu.ExpressionDataValue = ReadExpressionSupportingDataPointer(cursor, menu.ExpressionData.Untyped, context);
        ReadWindowChildren(cursor, menu.Window, context);
        menu.Font = ReadXString(cursor, menu.FontPointer, context);

        menu.OnOpenSet = ReadMenuEventHandlerSetPointer(cursor, menu.OnOpen.Untyped, context);
        menu.OnCloseSet = ReadMenuEventHandlerSetPointer(cursor, menu.OnClose.Untyped, context);
        menu.OnCloseRequestSet = ReadMenuEventHandlerSetPointer(cursor, menu.OnCloseRequest.Untyped, context);
        menu.OnEscSet = ReadMenuEventHandlerSetPointer(cursor, menu.OnEsc.Untyped, context);

        menu.ExecKeyHandler = ReadItemKeyHandlerPointer(cursor, menu.ExecKeys.Untyped, context);
        menu.VisibleStatement = ReadStatementPointer(cursor, menu.VisibleExpression.Untyped, context);
        menu.AllowedBindingString = ReadXString(cursor, menu.AllowedBinding, context);
        menu.SoundNameString = ReadXString(cursor, menu.SoundName, context);
        menu.RectXStatement = ReadStatementPointer(cursor, menu.RectXExpression.Untyped, context);
        menu.RectYStatement = ReadStatementPointer(cursor, menu.RectYExpression.Untyped, context);
        menu.RectWStatement = ReadStatementPointer(cursor, menu.RectWExpression.Untyped, context);
        menu.RectHStatement = ReadStatementPointer(cursor, menu.RectHExpression.Untyped, context);

        if (menu is { ItemCount: >= 0 })
        {
            IReadOnlyList<ItemDefReference> items = ReadItemDefPointerArray(
                cursor,
                menu.ItemsPointer.Untyped,
                menu.ItemCount,
                context);

            menu.Items = items;
        }
    }

    private static string? ReadXString(
        FastFileCursor cursor,
        XPointer<string> pointer,
        DbLoadExecutionContext context)
    {
        return context.PointerReader.LoadXString(cursor, pointer);
    }

    private static void ReadWindowChildren(
        FastFileCursor cursor,
        WindowDef window,
        DbLoadExecutionContext context)
    {
        window.Name = ReadXString(cursor, window.NamePointer, context);
        window.Group = ReadXString(cursor, window.GroupPointer, context);
        window.BackgroundMaterial = ReadMaterialPointer(cursor, window.Background.Untyped, context);
        window.BackgroundMaterialName = window.BackgroundMaterial?.Info.Name;
    }

    private static MaterialAsset? ReadMaterialPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        return MaterialLoader.LoadFromPointer(cursor, pointer, context);
    }

    private static IReadOnlyList<ItemDefReference> ReadItemDefPointerArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative ItemDef count {count}.");

        if (pointer.Type == PointerType.Null)
        {
            if (count == 0)
                return [];
            throw new InvalidDataException(
                $"ItemDef*[] has count {count}, but its pointer is null.");
        }

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<XPointer<ItemDefAsset>[]>(pointer, checked(count * sizeof(int)), "ItemDef*[]");
            return context.ResolveMaterializedDirect<ItemDefReference[]>(
                pointer,
                "ItemDef*[]");
        }

        if (!context.PointerReader.HasInlinePayload(pointer))
            throw UnsupportedDirectTablePointer(pointer, "ItemDef*[]");

        AlignStream(cursor, context, 4);
        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        ReadOnlyMemory<byte> pointerBytes = context.Blocks.LoadMemory(cursor, checked(count * sizeof(int)), out XBlockAddress tableAddress);
        var pointerCursor = new FastFileCursor(pointerBytes, tableAddress);
        var items = new ItemDefReference[count];
        context.RegisterMaterialized(tableAddress, items, "ItemDef*[]");
        ItemDefAsset? previousItem = null;
        int previousEndOffset = cursor.Offset;
        var recentItems = new Queue<(int Index, ItemDefAsset Item, int EndOffset)>();

        for (int i = 0; i < items.Length; i++)
        {
            XPointer<ItemDefAsset> typedItemPointer = context.PointerReader.ReadPointer<ItemDefAsset>(
                pointerCursor,
                XPointerOffsetMode.Direct,
                XPointerNullability.Required);
            XPointerReference itemPointer = typedItemPointer.Untyped;
            ItemDefAsset? item;
            try
            {
                item = ReadItemDefPointer(cursor, itemPointer, context);
            }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or OverflowException)
            {
                throw new InvalidDataException(
                    $"ItemDef[{i}] pointer 0x{itemPointer.Raw:X8} failed at cursor 0x{cursor.Offset:X}. " +
                    $"Previous item was {(previousItem is null ? "<none>" : $"source 0x{previousItem.Offset:X}..0x{previousEndOffset:X} type={previousItem.Type} dataType=0x{previousItem.DataType:X8} typeData=0x{GetItemTypeDataRaw(previousItem.TypeData):X8}")}. " +
                    $"Recent items: {string.Join("; ", recentItems.Select(recent =>
                        $"[{recent.Index}] 0x{recent.Item.Offset:X}..0x{recent.EndOffset:X} type={recent.Item.Type} " +
                        $"data=0x{recent.Item.DataType:X8} typeData=0x{GetItemTypeDataRaw(recent.Item.TypeData):X8}"))}.",
                    ex);
            }

            items[i] = new ItemDefReference(i, typedItemPointer, item);
            if (item is not null)
            {
                previousItem = item;
                previousEndOffset = cursor.Offset;
                recentItems.Enqueue((i, item, previousEndOffset));
                while (recentItems.Count > 8)
                    recentItems.Dequeue();
            }
        }

        return items;
    }

    internal static ItemDefAsset? ReadItemDefPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (ResolveMenuObjectWithoutSource(
                pointer,
                context,
                ItemDefSize,
                "ItemDef",
                out ItemDefAsset? existing))
            return existing;

        AlignStream(cursor, context, 4);
        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        ItemDefAsset item = ReadItemDefRoot(cursor, context);
        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            ReadItemDefChildren(cursor, item, context);
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or OverflowException or InvalidOperationException)
        {
            throw new InvalidDataException(
                $"ItemDef root at source 0x{item.Offset:X} parsed before child failure at cursor 0x{cursor.Offset:X}: " +
                $"type={item.Type} dataType=0x{item.DataType:X8} text=0x{item.Text.Raw:X8} textSaveGameInfo=0x{item.TextSaveGameInfo:X8} " +
                $"runtimeParent=0x{item.RuntimeParentPointer:X8} mouseEnterText=0x{item.MouseEnterText.Raw:X8} " +
                $"typeData=0x{GetItemTypeDataRaw(item.TypeData):X8} floatCount=0x{item.FloatExpressionCount:X8} " +
                $"floatExpressions=0x{item.FloatExpressions.Raw:X8} visible=0x{item.VisibleExpression.Raw:X8} " +
                $"disabled=0x{item.DisabledExpression.Raw:X8} textExpr=0x{item.TextExpression.Raw:X8} " +
                $"materialExpr=0x{item.MaterialExpression.Raw:X8} background=0x{item.Window.Background.Raw:X8}.",
                ex);
        }
        finally
        {
            context.Blocks.Pop();
        }

        return item;
    }

    private static ItemDefAsset ReadItemDefRoot(
        FastFileCursor cursor,
        DbLoadExecutionContext context)
    {
        int offset = cursor.Offset;
        ReadOnlyMemory<byte> rootBytes = context.Blocks.LoadMemory(cursor, ItemDefSize, out XBlockAddress rootAddress);
        var rootCursor = new FastFileCursor(rootBytes, rootAddress);

        var item = new ItemDefAsset
        {
            Offset = offset,
            RuntimeAddress = rootAddress,
            Window = ReadWindow(rootCursor, context),
            TextRect = ReadRectangles(rootCursor, 4),
            Type = (ItemDefType)rootCursor.ReadInt32(),
            DataType = rootCursor.ReadInt32(),
            Align = rootCursor.ReadInt32(),
            FontEnum = rootCursor.ReadInt32(),
            TextAlignMode = rootCursor.ReadInt32(),
            TextAlignX = ReadSingle(rootCursor),
            TextAlignY = ReadSingle(rootCursor),
            TextScale = ReadSingle(rootCursor),
            TextStyle = rootCursor.ReadInt32(),
            GameMsgWindowIndex = rootCursor.ReadInt32(),
            GameMsgWindowMode = rootCursor.ReadInt32(),
            Text = ReadXStringPointer(rootCursor, context),
            TextSaveGameInfo = rootCursor.ReadInt32(),
            RuntimeParentPointer = rootCursor.ReadInt32(),
            MouseEnterText = ReadNullablePointer<MenuEventHandlerSet>(rootCursor, context, XPointerResolutionMode.Direct),
            MouseExitText = ReadNullablePointer<MenuEventHandlerSet>(rootCursor, context, XPointerResolutionMode.Direct),
            MouseEnter = ReadNullablePointer<MenuEventHandlerSet>(rootCursor, context, XPointerResolutionMode.Direct),
            MouseExit = ReadNullablePointer<MenuEventHandlerSet>(rootCursor, context, XPointerResolutionMode.Direct),
            Action = ReadNullablePointer<MenuEventHandlerSet>(rootCursor, context, XPointerResolutionMode.Direct),
            Accept = ReadNullablePointer<MenuEventHandlerSet>(rootCursor, context, XPointerResolutionMode.Direct),
            OnFocus = ReadNullablePointer<MenuEventHandlerSet>(rootCursor, context, XPointerResolutionMode.Direct),
            LeaveFocus = ReadNullablePointer<MenuEventHandlerSet>(rootCursor, context, XPointerResolutionMode.Direct),
            Dvar = ReadXStringPointer(rootCursor, context),
            DvarTest = ReadXStringPointer(rootCursor, context),
            OnKey = ReadNullablePointer<ItemKeyHandler>(rootCursor, context, XPointerResolutionMode.Direct),
            EnableDvar = ReadXStringPointer(rootCursor, context),
            DvarFlags = rootCursor.ReadInt32(),
            FocusSound = ReadNullablePointer<SoundAliasListAssetModel>(rootCursor, context, XPointerResolutionMode.AliasCell),
            Special = ReadSingle(rootCursor),
            CursorPos = ReadInt32Array(rootCursor, 4),
            TypeData = ReadItemDefData(rootCursor, itemType: (ItemDefType)BinaryPrimitives.ReadInt32BigEndian(rootBytes.Span.Slice(0x100, sizeof(int)))),
            ImageTrack = rootCursor.ReadInt32(),
            FloatExpressionCount = rootCursor.ReadInt32(),
            FloatExpressions = ReadPointer<ItemFloatExpression[]>(rootCursor, context, XPointerResolutionMode.Direct),
            VisibleExpression = ReadNullablePointer<Statement>(rootCursor, context, XPointerResolutionMode.Direct),
            DisabledExpression = ReadNullablePointer<Statement>(rootCursor, context, XPointerResolutionMode.Direct),
            TextExpression = ReadNullablePointer<Statement>(rootCursor, context, XPointerResolutionMode.Direct),
            MaterialExpression = ReadNullablePointer<Statement>(rootCursor, context, XPointerResolutionMode.Direct),
            GlowColor = ReadVec4(rootCursor),
            DecayActive = rootCursor.ReadByte(),
            DecayActivePad0 = rootCursor.ReadByte(),
            DecayActivePad1 = rootCursor.ReadByte(),
            DecayActivePad2 = rootCursor.ReadByte(),
            FxBirthTime = rootCursor.ReadInt32(),
            FxLetterTime = rootCursor.ReadInt32(),
            FxDecayStartTime = rootCursor.ReadInt32(),
            FxDecayDuration = rootCursor.ReadInt32(),
            LastSoundPlayedTime = rootCursor.ReadInt32()
        };

        if (rootCursor.Offset != ItemDefSize)
            throw new InvalidDataException($"ItemDef consumed 0x{rootCursor.Offset:X} bytes instead of 0x{ItemDefSize:X}.");

        context.RegisterMenuObject(rootAddress, item);

        if (item.FloatExpressionCount is < 0 or > 0x1000)
        {
            throw new InvalidDataException(
                $"ItemDef at source 0x{item.Offset:X} has invalid floatExpressionCount 0x{item.FloatExpressionCount:X8}; " +
                $"type={item.Type} dataType=0x{item.DataType:X8} typeData=0x{GetItemTypeDataRaw(item.TypeData):X8} " +
                $"floatExpressions=0x{item.FloatExpressions.Raw:X8} visible=0x{item.VisibleExpression.Raw:X8}.");
        }


        return item;
    }

    private static void ReadItemDefChildren(
        FastFileCursor cursor,
        ItemDefAsset item,
        DbLoadExecutionContext context)
    {
        ReadWindowChildren(cursor, item.Window, context);
        item.TextString = ReadXString(cursor, item.Text, context);
        item.MouseEnterTextSet = ReadMenuEventHandlerSetPointer(cursor, item.MouseEnterText.Untyped, context);
        item.MouseExitTextSet = ReadMenuEventHandlerSetPointer(cursor, item.MouseExitText.Untyped, context);
        item.MouseEnterSet = ReadMenuEventHandlerSetPointer(cursor, item.MouseEnter.Untyped, context);
        item.MouseExitSet = ReadMenuEventHandlerSetPointer(cursor, item.MouseExit.Untyped, context);
        item.ActionSet = ReadMenuEventHandlerSetPointer(cursor, item.Action.Untyped, context);
        item.AcceptSet = ReadMenuEventHandlerSetPointer(cursor, item.Accept.Untyped, context);
        item.OnFocusSet = ReadMenuEventHandlerSetPointer(cursor, item.OnFocus.Untyped, context);
        item.LeaveFocusSet = ReadMenuEventHandlerSetPointer(cursor, item.LeaveFocus.Untyped, context);
        item.DvarString = ReadXString(cursor, item.Dvar, context);
        item.DvarTestString = ReadXString(cursor, item.DvarTest, context);
        item.OnKeyHandler = ReadItemKeyHandlerPointer(cursor, item.OnKey.Untyped, context);
        item.EnableDvarString = ReadXString(cursor, item.EnableDvar, context);
        item.FocusSoundAsset = SoundAliasListLoader.LoadFromPointer(cursor, item.FocusSound.Untyped, context);
        item.FocusSoundName = item.FocusSoundAsset?.AliasName;
        ReadItemTypeData(cursor, item, context);

        IReadOnlyList<ItemFloatExpression> floatExpressions = ReadItemFloatExpressions(
            cursor,
            item.FloatExpressions.Untyped,
            item.FloatExpressionCount,
            context);
        item.LoadedFloatExpressions = floatExpressions;

        item.VisibleStatement = ReadStatementPointer(cursor, item.VisibleExpression.Untyped, context);
        item.DisabledStatement = ReadStatementPointer(cursor, item.DisabledExpression.Untyped, context);
        item.TextStatement = ReadStatementPointer(cursor, item.TextExpression.Untyped, context);
        item.MaterialStatement = ReadStatementPointer(cursor, item.MaterialExpression.Untyped, context);
    }

    private static IReadOnlyList<RectangleDef> ReadRectangles(FastFileCursor cursor, int count)
    {
        var rectangles = new RectangleDef[count];
        for (int i = 0; i < rectangles.Length; i++)
            rectangles[i] = ReadRectangle(cursor);

        return rectangles;
    }

    private static WindowDef ReadWindow(
        FastFileCursor cursor,
        DbLoadExecutionContext context)
    {
        int start = cursor.Offset;
        var window = new WindowDef
        {
            NamePointer = ReadXStringPointer(cursor, context),
            Rect = ReadRectangle(cursor),
            RectClient = ReadRectangle(cursor),
            GroupPointer = ReadXStringPointer(cursor, context),
            Style = (WindowStyle)cursor.ReadInt32(),
            Border = (WindowBorder)cursor.ReadInt32(),
            OwnerDraw = (WindowOwnerDraw)cursor.ReadInt32(),
            OwnerDrawFlags = cursor.ReadInt32(),
            BorderSize = ReadSingle(cursor),
            StaticFlags = (WindowStaticFlags)cursor.ReadInt32(),
            DynamicFlags = ReadWindowDynamicFlags(cursor),
            NextTime = cursor.ReadInt32(),
            ForeColor = ReadVec4(cursor),
            BackColor = ReadVec4(cursor),
            BorderColor = ReadVec4(cursor),
            OutlineColor = ReadVec4(cursor),
            DisableColor = ReadVec4(cursor),
            Background = ReadNullablePointer<MaterialAsset>(
                cursor,
                context,
                XPointerResolutionMode.AliasCell)
        };

        int consumed = cursor.Offset - start;
        if (consumed != WindowDef.SerializedSize)
            throw new InvalidDataException($"WindowDef consumed 0x{consumed:X} bytes instead of 0x{WindowDef.SerializedSize:X}.");

        return window;
    }

    private static RectangleDef ReadRectangle(FastFileCursor cursor)
    {
        int start = cursor.Offset;
        var rectangle = new RectangleDef
        {
            X = ReadSingle(cursor),
            Y = ReadSingle(cursor),
            W = ReadSingle(cursor),
            H = ReadSingle(cursor),
            HorzAlign = (HorizontalAlign)cursor.ReadByte(),
            VertAlign = (VerticalAlign)cursor.ReadByte(),
            Pad12 = cursor.ReadUInt16()
        };

        int consumed = cursor.Offset - start;
        if (consumed != RectangleDef.SerializedSize)
            throw new InvalidDataException($"RectangleDef consumed 0x{consumed:X} bytes instead of 0x{RectangleDef.SerializedSize:X}.");

        return rectangle;
    }

    private static IReadOnlyList<WindowDynamicFlags> ReadWindowDynamicFlags(FastFileCursor cursor)
    {
        WindowDynamicFlags[] values = new WindowDynamicFlags[4];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = (WindowDynamicFlags)cursor.ReadInt32();
        }

        return values;
    }

    private static IReadOnlyList<MenuTransition> ReadMenuTransitions(FastFileCursor cursor, int count)
    {
        var transitions = new MenuTransition[count];
        for (int i = 0; i < transitions.Length; i++)
        {
            transitions[i] = new MenuTransition
            {
                TransitionType = (MenuTransitionType)cursor.ReadInt32(),
                TargetField = cursor.ReadInt32(),
                StartTime = cursor.ReadInt32(),
                StartValue = ReadSingle(cursor),
                EndValue = ReadSingle(cursor),
                Time = ReadSingle(cursor),
                EndTriggerType = (MenuTransitionEndTrigger)cursor.ReadInt32()
            };
        }

        return transitions;
    }

    private static MenuEventHandlerSet? ReadMenuEventHandlerSetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (ResolveMenuObjectWithoutSource(
                pointer,
                context,
                MenuEventHandlerSet.SerializedSize,
                "MenuEventHandlerSet",
                out MenuEventHandlerSet? existing))
            return existing;

        AlignStream(cursor, context, 4);
        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        ReadOnlyMemory<byte> rootBytes = context.Blocks.LoadMemory(cursor, MenuEventHandlerSet.SerializedSize, out XBlockAddress rootAddress);
        var rootCursor = new FastFileCursor(rootBytes, rootAddress);

        var set = new MenuEventHandlerSet
        {
            EventHandlerCount = rootCursor.ReadInt32(),
            EventHandlers = ReadPointer<XPointer<MenuEventHandler>[]>(rootCursor, context, XPointerResolutionMode.Direct)
        };

        if (rootCursor.Offset != MenuEventHandlerSet.SerializedSize)
            throw new InvalidDataException($"MenuEventHandlerSet consumed 0x{rootCursor.Offset:X} bytes instead of 0x{MenuEventHandlerSet.SerializedSize:X}.");

        context.RegisterMenuObject(rootAddress, set);


        set.Handlers = ReadMenuEventHandlerPointerArray(cursor, set.EventHandlers.Untyped, set.EventHandlerCount, context);
        return set;
    }

    private static IReadOnlyList<MenuEventHandlerReference> ReadMenuEventHandlerPointerArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative MenuEventHandler count {count}.");

        if (pointer.Type == PointerType.Null)
        {
            if (count == 0)
                return [];
            throw new InvalidDataException(
                $"MenuEventHandler*[] has count {count}, but its pointer is null.");
        }

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<XPointer<MenuEventHandler>[]>(pointer, checked(count * sizeof(int)), "MenuEventHandler*[]");
            return context.ResolveMaterializedDirect<MenuEventHandlerReference[]>(
                pointer,
                "MenuEventHandler*[]");
        }

        if (!context.PointerReader.HasInlinePayload(pointer))
            throw UnsupportedDirectTablePointer(pointer, "MenuEventHandler*[]");

        AlignStream(cursor, context, 4);
        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        ReadOnlyMemory<byte> pointerBytes = context.Blocks.LoadMemory(cursor, checked(count * sizeof(int)), out XBlockAddress tableAddress);
        var pointerCursor = new FastFileCursor(pointerBytes, tableAddress);
        var handlers = new MenuEventHandlerReference[count];
        context.RegisterMaterialized(
            tableAddress,
            handlers,
            "MenuEventHandler*[]");

        for (int i = 0; i < count; i++)
        {
            XPointerReference handlerPointer = context.PointerReader.ReadCell(pointerCursor, XPointerOffsetMode.Direct);
            MenuEventHandler? handler = ReadMenuEventHandlerPointer(cursor, handlerPointer, context);
            handlers[i] = new MenuEventHandlerReference(i, handlerPointer.AsPointer<MenuEventHandler>(), handler);
        }

        return handlers;
    }

    private static MenuEventHandler? ReadMenuEventHandlerPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (ResolveMenuObjectWithoutSource(
                pointer,
                context,
                MenuEventHandler.SerializedSize,
                "MenuEventHandler",
                out MenuEventHandler? existing))
            return existing;

        AlignStream(cursor, context, 4);
        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        ReadOnlyMemory<byte> rootBytes = context.Blocks.LoadMemory(cursor, MenuEventHandler.SerializedSize, out XBlockAddress rootAddress);
        var rootCursor = new FastFileCursor(rootBytes, rootAddress);
        XPointerReference eventDataPointer = context.PointerReader.ReadCell(rootCursor, XPointerOffsetMode.Direct);
        var eventType = (MenuEventHandlerType)rootCursor.ReadByte();

        var handler = new MenuEventHandler
        {
            EventData = ReadEventDataValue(eventDataPointer, eventType),
            EventType = eventType,
            Pad05 = rootCursor.ReadByte(),
            Pad06 = rootCursor.ReadByte(),
            Pad07 = rootCursor.ReadByte()
        };

        if (rootCursor.Offset != MenuEventHandler.SerializedSize)
            throw new InvalidDataException($"MenuEventHandler consumed 0x{rootCursor.Offset:X} bytes instead of 0x{MenuEventHandler.SerializedSize:X}.");

        context.RegisterMenuObject(rootAddress, handler);


        ReadEventData(cursor, handler, rootAddress, context);
        return handler;
    }

    private static void ReadEventData(
        FastFileCursor cursor,
        MenuEventHandler handler,
        XBlockAddress rootAddress,
        DbLoadExecutionContext context)
    {
        XBlockAddress dataCellAddress = rootAddress.Add(0x00);

        switch (handler.EventType)
        {
            case MenuEventHandlerType.UnconditionalScript:
                if (handler.EventData.UnconditionalScript is { } script)
                    handler.UnconditionalScript = context.PointerReader.LoadXString(cursor, dataCellAddress, script.Script);
                break;

            case MenuEventHandlerType.ConditionalScript:
                if (handler.EventData.ConditionalScript is not { } conditional)
                    break;

                if (context.PointerReader.HasInlinePayload(conditional.ConditionalScriptPointer.Untyped))
                    context.PointerReader.PatchInlinePointerCell(dataCellAddress, conditional.ConditionalScriptPointer.Raw, alignment: 4);

                handler.ConditionalScript = ReadConditionalScriptPointer(cursor, conditional.ConditionalScriptPointer.Untyped, context);
                break;

            case MenuEventHandlerType.ElseScript:
                if (handler.EventData.ElseScript is not { } elseScript)
                    break;

                if (context.PointerReader.HasInlinePayload(elseScript.EventHandlerSetPointer.Untyped))
                    context.PointerReader.PatchInlinePointerCell(dataCellAddress, elseScript.EventHandlerSetPointer.Raw, alignment: 4);

                handler.ElseScriptSet = ReadMenuEventHandlerSetPointer(cursor, elseScript.EventHandlerSetPointer.Untyped, context);
                break;

            case MenuEventHandlerType.SetLocalVarBool:
            case MenuEventHandlerType.SetLocalVarInt:
            case MenuEventHandlerType.SetLocalVarFloat:
            case MenuEventHandlerType.SetLocalVarString:
                if (handler.EventData.SetLocalVarData is not { } setLocal)
                    break;

                if (context.PointerReader.HasInlinePayload(setLocal.SetLocalVarDataPointer.Untyped))
                    context.PointerReader.PatchInlinePointerCell(dataCellAddress, setLocal.SetLocalVarDataPointer.Raw, alignment: 4);

                handler.SetLocalVarData = ReadSetLocalVarDataPointer(cursor, setLocal.SetLocalVarDataPointer.Untyped, context);
                break;
        }
    }

    private static EventData ReadEventDataValue(
        XPointerReference pointer,
        MenuEventHandlerType eventType)
    {
        EventDataValue value = eventType switch
        {
            MenuEventHandlerType.UnconditionalScript => new UnconditionalScriptEventData
            {
                Script = pointer.AsPointer<string>()
            },
            MenuEventHandlerType.ConditionalScript => new ConditionalScriptEventData
            {
                ConditionalScriptPointer = pointer.AsPointer<ConditionalScript>()
            },
            MenuEventHandlerType.ElseScript => new ElseScriptEventData
            {
                EventHandlerSetPointer = pointer.AsPointer<MenuEventHandlerSet>()
            },
            MenuEventHandlerType.SetLocalVarBool
                or MenuEventHandlerType.SetLocalVarInt
                or MenuEventHandlerType.SetLocalVarFloat
                or MenuEventHandlerType.SetLocalVarString => new SetLocalVarEventData
                {
                    SetLocalVarDataPointer = pointer.AsPointer<SetLocalVarData>()
                },
            _ => new IgnoredEventData { Reserved = pointer.Raw }
        };

        return new EventData { Value = value };
    }

    private static ConditionalScript? ReadConditionalScriptPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (ResolveMenuObjectWithoutSource(
                pointer,
                context,
                ConditionalScript.SerializedSize,
                "ConditionalScript",
                out ConditionalScript? existing))
            return existing;

        AlignStream(cursor, context, 4);
        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        ReadOnlyMemory<byte> rootBytes = context.Blocks.LoadMemory(cursor, ConditionalScript.SerializedSize, out XBlockAddress rootAddress);
        var rootCursor = new FastFileCursor(rootBytes, rootAddress);

        var script = new ConditionalScript
        {
            EventHandlerSet = ReadPointer<MenuEventHandlerSet>(rootCursor, context, XPointerResolutionMode.Direct),
            EventExpression = ReadPointer<Statement>(rootCursor, context, XPointerResolutionMode.Direct)
        };

        if (rootCursor.Offset != ConditionalScript.SerializedSize)
            throw new InvalidDataException($"ConditionalScript consumed 0x{rootCursor.Offset:X} bytes instead of 0x{ConditionalScript.SerializedSize:X}.");

        context.RegisterMenuObject(rootAddress, script);

        script.EventStatement = ReadStatementPointer(cursor, script.EventExpression.Untyped, context);

        script.EventHandlers = ReadMenuEventHandlerSetPointer(cursor, script.EventHandlerSet.Untyped, context);
        return script;
    }

    private static SetLocalVarData? ReadSetLocalVarDataPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (ResolveMenuObjectWithoutSource(
                pointer,
                context,
                SetLocalVarData.SerializedSize,
                "SetLocalVarData",
                out SetLocalVarData? existing))
            return existing;

        AlignStream(cursor, context, 4);
        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        ReadOnlyMemory<byte> rootBytes = context.Blocks.LoadMemory(cursor, SetLocalVarData.SerializedSize, out XBlockAddress rootAddress);
        var rootCursor = new FastFileCursor(rootBytes, rootAddress);

        var data = new SetLocalVarData
        {
            LocalVarName = ReadXStringPointer(rootCursor, context),
            Expression = ReadPointer<Statement>(rootCursor, context, XPointerResolutionMode.Direct)
        };

        if (rootCursor.Offset != SetLocalVarData.SerializedSize)
            throw new InvalidDataException($"SetLocalVarData consumed 0x{rootCursor.Offset:X} bytes instead of 0x{SetLocalVarData.SerializedSize:X}.");

        context.RegisterMenuObject(rootAddress, data);

        data.LocalVarNameString = context.PointerReader.LoadXString(cursor, data.LocalVarName);
        data.ExpressionStatement = ReadStatementPointer(cursor, data.Expression.Untyped, context);
        return data;
    }

    private static ItemKeyHandler? ReadItemKeyHandlerPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (ResolveMenuObjectWithoutSource(
                pointer,
                context,
                ItemKeyHandler.SerializedSize,
                "ItemKeyHandler",
                out ItemKeyHandler? existing))
            return existing;

        AlignStream(cursor, context, 4);
        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        ReadOnlyMemory<byte> rootBytes = context.Blocks.LoadMemory(cursor, ItemKeyHandler.SerializedSize, out XBlockAddress rootAddress);
        var rootCursor = new FastFileCursor(rootBytes, rootAddress);

        var handler = new ItemKeyHandler
        {
            Key = rootCursor.ReadInt32(),
            Action = ReadPointer<MenuEventHandlerSet>(rootCursor, context, XPointerResolutionMode.Direct),
            Next = ReadNullablePointer<ItemKeyHandler>(rootCursor, context, XPointerResolutionMode.Direct)
        };

        if (rootCursor.Offset != ItemKeyHandler.SerializedSize)
            throw new InvalidDataException($"ItemKeyHandler consumed 0x{rootCursor.Offset:X} bytes instead of 0x{ItemKeyHandler.SerializedSize:X}.");

        context.RegisterMenuObject(rootAddress, handler);

        handler.ActionSet = ReadMenuEventHandlerSetPointer(cursor, handler.Action.Untyped, context);
        handler.NextHandler = ReadItemKeyHandlerPointer(cursor, handler.Next.Untyped, context);
        return handler;
    }

    private static Statement? ReadStatementPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (ResolveMenuObjectWithoutSource(
                pointer,
                context,
                Statement.SerializedSize,
                "Statement",
                out Statement? existing))
            return existing;

        AlignStream(cursor, context, 4);
        int offset = cursor.Offset;
        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        ReadOnlyMemory<byte> rootBytes = context.Blocks.LoadMemory(cursor, Statement.SerializedSize, out XBlockAddress rootAddress);
        var rootCursor = new FastFileCursor(rootBytes, rootAddress);

        var statement = new Statement
        {
            DestinationAddress = rootAddress,
            NumEntries = rootCursor.ReadInt32(),
            Entries = ReadPointer<ExpressionEntry[]>(rootCursor, context, XPointerResolutionMode.Direct),
            SupportingData = ReadNullablePointer<ExpressionSupportingData>(rootCursor, context, XPointerResolutionMode.Direct),
            LastExecuteTime = rootCursor.ReadInt32(),
            LastResult = ReadOperand(rootCursor.ReadInt32(), rootCursor.ReadInt32())
        };

        if (rootCursor.Offset != Statement.SerializedSize)
            throw new InvalidDataException($"Statement consumed 0x{rootCursor.Offset:X} bytes instead of 0x{Statement.SerializedSize:X}.");

        context.RegisterMenuObject(rootAddress, statement);


        if (statement.NumEntries is < 0 or > 0x10000)
        {
            throw new InvalidDataException(
                $"Statement at source 0x{offset:X} from pointer 0x{pointer.Raw:X8} has invalid numEntries 0x{statement.NumEntries:X8}; " +
                $"entries=0x{statement.Entries.Raw:X8}, supportingData=0x{statement.SupportingData.Raw:X8}.");
        }

        statement.LoadedEntries = ReadExpressionEntries(cursor, statement.Entries.Untyped, statement.NumEntries, context);

        statement.SupportingDataValue = ReadExpressionSupportingDataPointer(cursor, statement.SupportingData.Untyped, context);
        return statement;
    }

    private static IReadOnlyList<ExpressionEntry> ReadExpressionEntries(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative ExpressionEntry count {count}.");

        if (pointer.Type == PointerType.Null)
        {
            if (count == 0)
                return [];
            throw new InvalidDataException(
                $"ExpressionEntry[] has count {count}, but its pointer is null.");
        }

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<ExpressionEntry[]>(pointer, checked(count * ExpressionEntry.SerializedSize), "ExpressionEntry[]");
            return context.ResolveMaterializedDirect<ExpressionEntry[]>(pointer, "ExpressionEntry[]");
        }

        if (!context.PointerReader.HasInlinePayload(pointer))
            throw UnsupportedDirectTablePointer(pointer, "ExpressionEntry[]");

        AlignStream(cursor, context, 4);
        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        ReadOnlyMemory<byte> entryBytes = context.Blocks.LoadMemory(cursor, checked(count * ExpressionEntry.SerializedSize), out XBlockAddress tableAddress);
        var entryCursor = new FastFileCursor(entryBytes, tableAddress);
        var entries = new ExpressionEntry[count];
        context.RegisterMaterialized(tableAddress, entries, "ExpressionEntry[]");

        for (int i = 0; i < entries.Length; i++)
        {
            int rowStart = entryCursor.Offset;
            var kind = (ExpressionEntryKind)entryCursor.ReadInt32();
            int discriminatorOrOperation = entryCursor.ReadInt32();
            int encodedValueOrTail = entryCursor.ReadInt32();

            if (entryCursor.Offset - rowStart != ExpressionEntry.SerializedSize)
                throw new InvalidDataException($"ExpressionEntry consumed 0x{entryCursor.Offset - rowStart:X} bytes instead of 0x{ExpressionEntry.SerializedSize:X}.");

            var entry = kind switch
            {
                ExpressionEntryKind.Operator => new ExpressionEntry
                {
                    Kind = kind,
                    OperationCode = discriminatorOrOperation,
                    OperatorTail = encodedValueOrTail
                },
                ExpressionEntryKind.Operand => new ExpressionEntry
                {
                    Kind = kind,
                    Operand = ReadOperand(discriminatorOrOperation, encodedValueOrTail)
                },
                _ => new ExpressionEntry
                {
                    Kind = kind,
                    // Preserve both words for a later diagnostic without
                    // accidentally treating an unknown union arm as an
                    // ExpDataType/operand child.
                    OperationCode = discriminatorOrOperation,
                    OperatorTail = encodedValueOrTail
                }
            };
            entries[i] = entry;

            if (kind != ExpressionEntryKind.Operand)
                continue;

            ReadOperandChildren(cursor, entry, tableAddress.Add(rowStart + 0x08), context);
        }

        return entries;
    }

    private static Operand ReadOperand(int dataTypeRaw, int encodedValue)
    {
        var dataType = (ExpDataType)dataTypeRaw;
        return new Operand
        {
            DataType = dataType,
            Value = OperandValueFactory.FromEncoded(dataType, encodedValue)
        };
    }

    private static XPointerReference ReadRawCell(
        FastFileCursor cursor,
        XPointerOffsetMode offsetMode)
    {
        int cellOffset = cursor.Offset;
        return XPointerReference.FromRaw(
            cursor.ReadInt32(),
            offsetMode,
            cursor.AddressAt(cellOffset));
    }

    private static ItemDefData ReadItemDefData(
        FastFileCursor cursor,
        ItemDefType itemType)
    {
        XPointerReference pointer = ReadRawCell(cursor, XPointerOffsetMode.Direct);
        ItemDefDataValue value = itemType switch
        {
            ItemDefType.Text
                or ItemDefType.EditField
                or ItemDefType.NumericField
                or ItemDefType.Slider
                or ItemDefType.YesNo
                or ItemDefType.Bind
                or ItemDefType.Validation
                or ItemDefType.DecimalField
                or ItemDefType.UpDown
                or ItemDefType.EmailField
                or ItemDefType.PassWordField => new EditFieldItemDefData
                {
                    EditFieldPointer = pointer.AsPointer<EditFieldDef>()
                },
            ItemDefType.ListBox => new ListBoxItemDefData
            {
                ListBoxPointer = pointer.AsPointer<ListBoxDef>()
            },
            ItemDefType.Multi => new MultiItemDefData
            {
                MultiPointer = pointer.AsPointer<MultiDef>()
            },
            ItemDefType.DvarEnum => new DvarEnumItemDefData
            {
                DvarEnumNamePointer = pointer.AsPointer<string>()
            },
            ItemDefType.NewsTicker => new NewsTickerItemDefData
            {
                NewsTickerPointer = pointer.AsPointer<NewsTickerDef>()
            },
            ItemDefType.TextScroll => new TextScrollItemDefData
            {
                TextScrollPointer = pointer.AsPointer<TextScrollDef>()
            },
            _ => new NoItemDefData { Reserved = pointer.Raw }
        };

        return new ItemDefData { Value = value };
    }

    private static int GetItemTypeDataRaw(ItemDefData typeData)
    {
        return typeData.Value switch
        {
            EditFieldItemDefData editField => editField.EditFieldPointer.Raw,
            ListBoxItemDefData listBox => listBox.ListBoxPointer.Raw,
            MultiItemDefData multi => multi.MultiPointer.Raw,
            DvarEnumItemDefData dvarEnum => dvarEnum.DvarEnumNamePointer.Raw,
            NewsTickerItemDefData newsTicker => newsTicker.NewsTickerPointer.Raw,
            TextScrollItemDefData textScroll => textScroll.TextScrollPointer.Raw,
            NoItemDefData none => none.Reserved,
            _ => 0
        };
    }

    private static void ReadOperandChildren(
        FastFileCursor cursor,
        ExpressionEntry entry,
        XBlockAddress pointerCellAddress,
        DbLoadExecutionContext context)
    {
        Operand operand = entry.Operand;
        switch (operand.DataType)
        {
            case ExpDataType.VAL_STRING:
                if (operand.Value is StringOperandValue stringValue)
                {
                    entry.StringValue = context.PointerReader.LoadXString(
                        cursor,
                        context.PointerReader.FromRaw<string>(
                            stringValue.StringPointer.Raw,
                            XPointerResolutionMode.Direct,
                            pointerCellAddress));
                }
                break;

            case ExpDataType.VAL_FUNCTION:
                if (operand.Value is FunctionOperandValue functionValue)
                {
                    entry.FunctionStatement = ReadStatementPointer(
                        cursor,
                        context.PointerReader.FromRaw<Statement>(
                            functionValue.StatementPointer.Raw,
                            XPointerResolutionMode.Direct,
                            pointerCellAddress).Untyped,
                        context);
                }
                break;
        }
    }

    private static ExpressionSupportingData? ReadExpressionSupportingDataPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (ResolveMenuObjectWithoutSource(
                pointer,
                context,
                ExpressionSupportingData.SerializedSize,
                "ExpressionSupportingData",
                out ExpressionSupportingData? existing))
            return existing;

        AlignStream(cursor, context, 4);
        int offset = cursor.Offset;
        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        ReadOnlyMemory<byte> rootBytes = context.Blocks.LoadMemory(cursor, ExpressionSupportingData.SerializedSize, out XBlockAddress rootAddress);
        var rootCursor = new FastFileCursor(rootBytes, rootAddress);

        var data = new ExpressionSupportingData
        {
            UiFunctions = ReadUiFunctionList(rootCursor, context),
            StaticDvarList = ReadStaticDvarList(rootCursor, context),
            UiStrings = ReadStringList(rootCursor, context)
        };

        if (rootCursor.Offset != ExpressionSupportingData.SerializedSize)
            throw new InvalidDataException($"ExpressionSupportingData consumed 0x{rootCursor.Offset:X} bytes instead of 0x{ExpressionSupportingData.SerializedSize:X}.");

        context.RegisterMenuObject(rootAddress, data);


        data.UiFunctions.LoadedFunctions = ReadUiFunctionListChildren(cursor, data.UiFunctions, context);
        data.StaticDvarList.LoadedStaticDvars = ReadStaticDvarListChildren(cursor, data.StaticDvarList, context);
        data.UiStrings.LoadedStrings = ReadStringListChildren(cursor, data.UiStrings, context);
        return data;
    }

    private static UIFunctionList ReadUiFunctionList(FastFileCursor cursor, DbLoadExecutionContext context)
    {
        return new UIFunctionList
        {
            TotalFunctions = cursor.ReadInt32(),
            Functions = ReadPointer<XPointer<Statement>[]>(cursor, context, XPointerResolutionMode.Direct)
        };
    }

    private static StaticDvarList ReadStaticDvarList(FastFileCursor cursor, DbLoadExecutionContext context)
    {
        return new StaticDvarList
        {
            NumStaticDvars = cursor.ReadInt32(),
            StaticDvars = ReadPointer<XPointer<StaticDvar>[]>(cursor, context, XPointerResolutionMode.Direct)
        };
    }

    private static StringList ReadStringList(FastFileCursor cursor, DbLoadExecutionContext context)
    {
        return new StringList
        {
            TotalStrings = cursor.ReadInt32(),
            Strings = ReadPointer<XPointer<string>[]>(cursor, context, XPointerResolutionMode.Direct)
        };
    }

    private static IReadOnlyList<StatementReference> ReadUiFunctionListChildren(
        FastFileCursor cursor,
        UIFunctionList list,
        DbLoadExecutionContext context)
    {
        if (context.PointerReader.HasInlinePayload(list.Functions.Untyped))
            context.PointerReader.PatchInlinePointerCell(list.Functions, alignment: 4);

        return ReadPointerArray(cursor, list.Functions.Untyped, list.TotalFunctions, context, "UIFunctionList.functions", (index, pointer) =>
            new StatementReference(index, pointer.AsPointer<Statement>(), ReadStatementPointer(cursor, pointer, context)), inlineAlignment: 4);
    }

    private static IReadOnlyList<StaticDvarReference> ReadStaticDvarListChildren(
        FastFileCursor cursor,
        StaticDvarList list,
        DbLoadExecutionContext context)
    {
        if (context.PointerReader.HasInlinePayload(list.StaticDvars.Untyped))
            context.PointerReader.PatchInlinePointerCell(list.StaticDvars, alignment: 4);

        return ReadPointerArray(cursor, list.StaticDvars.Untyped, list.NumStaticDvars, context, "StaticDvarList.staticDvars", (index, pointer) =>
            new StaticDvarReference(index, pointer.AsPointer<StaticDvar>(), ReadStaticDvarPointer(cursor, pointer, context)), inlineAlignment: 4);
    }

    private static IReadOnlyList<XStringReference> ReadStringListChildren(
        FastFileCursor cursor,
        StringList list,
        DbLoadExecutionContext context)
    {
        if (context.PointerReader.HasInlinePayload(list.Strings.Untyped))
            context.PointerReader.PatchInlinePointerCell(list.Strings, alignment: 4);

        return ReadPointerArray(cursor, list.Strings.Untyped, list.TotalStrings, context, "StringList.strings", (index, pointer) =>
            new XStringReference(index, pointer.AsPointer<string>(), ReadXString(cursor, pointer.AsPointer<string>(), context)), inlineAlignment: 0);
    }

    private static StaticDvar? ReadStaticDvarPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (ResolveMenuObjectWithoutSource(
                pointer,
                context,
                StaticDvar.SerializedSize,
                "StaticDvar",
                out StaticDvar? existing))
            return existing;

        AlignStream(cursor, context, 4);
        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        ReadOnlyMemory<byte> rootBytes = context.Blocks.LoadMemory(cursor, StaticDvar.SerializedSize, out XBlockAddress rootAddress);
        var rootCursor = new FastFileCursor(rootBytes, rootAddress);

        var dvar = new StaticDvar
        {
            DestinationAddress = rootAddress,
            Dvar = ReadNullablePointer<DvarRuntimeHandle>(rootCursor, context, XPointerResolutionMode.Direct),
            DvarName = ReadXStringPointer(rootCursor, context)
        };

        if (rootCursor.Offset != StaticDvar.SerializedSize)
            throw new InvalidDataException($"StaticDvar consumed 0x{rootCursor.Offset:X} bytes instead of 0x{StaticDvar.SerializedSize:X}.");

        context.RegisterMenuObject(rootAddress, dvar);

        dvar.DvarNameString = context.PointerReader.LoadXString(cursor, dvar.DvarName);
        return dvar;
    }

    internal static IReadOnlyList<T> ReadPointerArray<T>(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        string name,
        Func<int, XPointerReference, T> readElement,
        int inlineAlignment)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative pointer-array count {count}.");

        if (pointer.Type == PointerType.Null)
        {
            if (count == 0)
                return [];
            throw new InvalidDataException(
                $"{name} has count {count}, but its pointer is null.");
        }

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange(pointer, checked(count * sizeof(int)), name);
            return context.ResolveMaterializedDirect<T[]>(pointer, name);
        }

        if (!context.PointerReader.HasInlinePayload(pointer))
            throw UnsupportedDirectTablePointer(pointer, name);

        AlignStream(cursor, context, 4);
        int tableOffset = cursor.Offset;
        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        ReadOnlyMemory<byte> pointerBytes = context.Blocks.LoadMemory(cursor, checked(count * sizeof(int)), out XBlockAddress tableAddress);
        var pointerCursor = new FastFileCursor(pointerBytes, tableAddress);
        var values = new T[count];
        context.RegisterMaterialized(tableAddress, values, name);

        for (int i = 0; i < count; i++)
        {
            XPointerReference elementPointer = context.PointerReader.ReadCell(pointerCursor, XPointerOffsetMode.Direct);
            try
            {
                if (context.PointerReader.HasInlinePayload(elementPointer))
                    context.PointerReader.PatchInlinePointerCell(elementPointer, inlineAlignment);

                values[i] = readElement(i, elementPointer);
            }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or OverflowException)
            {
                throw new InvalidDataException(
                    $"{name}[{i}] pointer 0x{elementPointer.Raw:X8} from table source 0x{tableOffset:X} failed at cursor 0x{cursor.Offset:X}.",
                    ex);
            }
        }

        return values;
    }

    private static InvalidDataException UnsupportedDirectTablePointer(
        XPointerReference pointer,
        string name) =>
        new(
            $"{name} pointer 0x{unchecked((uint)pointer.Raw):X8} has " +
            $"unsupported direct-pointer source form {pointer.Type}.");

    private static void ReadItemTypeData(
        FastFileCursor cursor,
        ItemDefAsset item,
        DbLoadExecutionContext context)
    {
        switch (item.Type)
        {
            case ItemDefType.Text:
            case ItemDefType.EditField:
            case ItemDefType.NumericField:
            case ItemDefType.Slider:
            case ItemDefType.YesNo:
            case ItemDefType.Bind:
            case ItemDefType.Validation:
            case ItemDefType.DecimalField:
            case ItemDefType.UpDown:
            case ItemDefType.EmailField:
            case ItemDefType.PassWordField:
                if (item.TypeData.EditField is { } editField)
                    item.EditField = ReadEditFieldDefPointer(cursor, editField.EditFieldPointer.Untyped, context);
                break;

            case ItemDefType.ListBox:
                if (item.TypeData.ListBox is { } listBox)
                    item.ListBox = ReadListBoxDefPointer(cursor, listBox.ListBoxPointer.Untyped, context);
                break;

            case ItemDefType.Multi:
                if (item.TypeData.Multi is { } multi)
                    item.Multi = ReadMultiDefPointer(cursor, multi.MultiPointer.Untyped, context);
                break;

            case ItemDefType.DvarEnum:
                if (item.TypeData.DvarEnum is { } dvarEnum)
                    item.DvarEnumName = ReadXString(cursor, dvarEnum.DvarEnumNamePointer, context);
                break;

            case ItemDefType.NewsTicker:
                if (item.TypeData.NewsTicker is { } newsTicker)
                    item.NewsTicker = ReadNewsTickerDefPointer(cursor, newsTicker.NewsTickerPointer.Untyped, context);
                break;

            case ItemDefType.TextScroll:
                if (item.TypeData.TextScroll is { } textScroll)
                    item.TextScroll = ReadTextScrollDefPointer(cursor, textScroll.TextScrollPointer.Untyped, context);
                break;
        }
    }

    private static EditFieldDef? ReadEditFieldDefPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (ResolveMenuObjectWithoutSource(
                pointer,
                context,
                EditFieldDef.SerializedSize,
                "EditFieldDef",
                out EditFieldDef? existing))
            return existing;

        AlignStream(cursor, context, 4);
        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        ReadOnlyMemory<byte> rootBytes = context.Blocks.LoadMemory(cursor, EditFieldDef.SerializedSize, out XBlockAddress rootAddress);
        var rootCursor = new FastFileCursor(rootBytes, rootAddress);

        var edit = new EditFieldDef
        {
            MinVal = ReadSingle(rootCursor),
            MaxVal = ReadSingle(rootCursor),
            DefVal = ReadSingle(rootCursor),
            Range = ReadSingle(rootCursor),
            MaxChars = rootCursor.ReadInt32(),
            MaxCharsGotoNext = rootCursor.ReadInt32(),
            MaxPaintChars = rootCursor.ReadInt32(),
            PaintOffset = rootCursor.ReadInt32()
        };

        if (rootCursor.Offset != EditFieldDef.SerializedSize)
            throw new InvalidDataException($"EditFieldDef consumed 0x{rootCursor.Offset:X} bytes instead of 0x{EditFieldDef.SerializedSize:X}.");

        context.RegisterMenuObject(rootAddress, edit);

        return edit;
    }

    private static ListBoxDef? ReadListBoxDefPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (ResolveMenuObjectWithoutSource(
                pointer,
                context,
                ListBoxDef.SerializedSize,
                "ListBoxDef",
                out ListBoxDef? existing))
            return existing;

        AlignStream(cursor, context, 4);
        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        ReadOnlyMemory<byte> rootBytes = context.Blocks.LoadMemory(cursor, ListBoxDef.SerializedSize, out XBlockAddress rootAddress);
        var rootCursor = new FastFileCursor(rootBytes, rootAddress);

        var listBox = new ListBoxDef
        {
            StartPos = ReadInt32Array(rootCursor, 4),
            EndPos = ReadInt32Array(rootCursor, 4),
            DrawPadding = rootCursor.ReadInt32(),
            ElementWidth = ReadSingle(rootCursor),
            ElementHeight = ReadSingle(rootCursor),
            ElementStyle = rootCursor.ReadInt32(),
            NumColumns = rootCursor.ReadInt32(),
            ColumnInfo = ReadColumnInfoArray(rootCursor, 16),
            DoubleClick = ReadNullablePointer<MenuEventHandlerSet>(rootCursor, context, XPointerResolutionMode.Direct),
            NotSelectable = rootCursor.ReadInt32(),
            NoScrollbars = rootCursor.ReadInt32(),
            UsePaging = rootCursor.ReadInt32(),
            SelectBorder = ReadVec4(rootCursor),
            SelectIcon = ReadNullablePointer<MaterialAsset>(rootCursor, context, XPointerResolutionMode.AliasCell)
        };

        if (rootCursor.Offset != ListBoxDef.SerializedSize)
            throw new InvalidDataException($"ListBoxDef consumed 0x{rootCursor.Offset:X} bytes instead of 0x{ListBoxDef.SerializedSize:X}.");

        context.RegisterMenuObject(rootAddress, listBox);


        listBox.DoubleClickSet = ReadMenuEventHandlerSetPointer(cursor, listBox.DoubleClick.Untyped, context);
        listBox.SelectIconMaterial = ReadMaterialPointer(cursor, listBox.SelectIcon.Untyped, context);
        listBox.SelectIconMaterialName = listBox.SelectIconMaterial?.Info.Name;
        return listBox;
    }

    private static IReadOnlyList<ColumnInfo> ReadColumnInfoArray(FastFileCursor cursor, int count)
    {
        var columns = new ColumnInfo[count];
        for (int i = 0; i < columns.Length; i++)
        {
            columns[i] = new ColumnInfo
            {
                Pos = cursor.ReadInt32(),
                Width = cursor.ReadInt32(),
                MaxChars = cursor.ReadInt32(),
                Alignment = cursor.ReadInt32()
            };
        }

        return columns;
    }

    private static MultiDef? ReadMultiDefPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (ResolveMenuObjectWithoutSource(
                pointer,
                context,
                MultiDef.SerializedSize,
                "MultiDef",
                out MultiDef? existing))
            return existing;

        AlignStream(cursor, context, 4);
        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        ReadOnlyMemory<byte> rootBytes = context.Blocks.LoadMemory(cursor, MultiDef.SerializedSize, out XBlockAddress rootAddress);
        var rootCursor = new FastFileCursor(rootBytes, rootAddress);

        var multi = new MultiDef
        {
            DvarList = ReadXStringPointerArray(rootCursor, MultiDef.EntryCapacity, context),
            DvarStr = ReadXStringPointerArray(rootCursor, MultiDef.EntryCapacity, context),
            DvarValue = ReadFloatArray(rootCursor, MultiDef.EntryCapacity),
            Count = rootCursor.ReadInt32(),
            StrDef = rootCursor.ReadInt32()
        };

        if (rootCursor.Offset != MultiDef.SerializedSize)
            throw new InvalidDataException($"MultiDef consumed 0x{rootCursor.Offset:X} bytes instead of 0x{MultiDef.SerializedSize:X}.");

        context.RegisterMenuObject(rootAddress, multi);


        var dvarListStrings = new string?[multi.DvarList.Count];
        for (int i = 0; i < multi.DvarList.Count; i++)
            dvarListStrings[i] = context.PointerReader.LoadXString(cursor, multi.DvarList[i]);
        multi.DvarListStrings = dvarListStrings;

        var dvarStrStrings = new string?[multi.DvarStr.Count];
        for (int i = 0; i < multi.DvarStr.Count; i++)
            dvarStrStrings[i] = context.PointerReader.LoadXString(cursor, multi.DvarStr[i]);
        multi.DvarStrStrings = dvarStrStrings;

        return multi;
    }

    private static IReadOnlyList<XPointer<string>> ReadXStringPointerArray(
        FastFileCursor cursor,
        int count,
        DbLoadExecutionContext context)
    {
        var pointers = new XPointer<string>[count];
        for (int i = 0; i < pointers.Length; i++)
            pointers[i] = ReadXStringPointer(cursor, context);

        return pointers;
    }

    private static IReadOnlyList<float> ReadFloatArray(FastFileCursor cursor, int count)
    {
        var values = new float[count];
        for (int i = 0; i < values.Length; i++)
            values[i] = ReadSingle(cursor);

        return values;
    }

    private static NewsTickerDef? ReadNewsTickerDefPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (ResolveMenuObjectWithoutSource(
                pointer,
                context,
                NewsTickerDef.SerializedSize,
                "NewsTickerDef",
                out NewsTickerDef? existing))
            return existing;

        AlignStream(cursor, context, 4);
        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        ReadOnlyMemory<byte> rootBytes = context.Blocks.LoadMemory(cursor, NewsTickerDef.SerializedSize, out XBlockAddress rootAddress);
        var rootCursor = new FastFileCursor(rootBytes, rootAddress);

        var newsTicker = new NewsTickerDef
        {
            FeedId = rootCursor.ReadInt32(),
            Speed = rootCursor.ReadInt32(),
            Spacing = rootCursor.ReadInt32(),
            LastTime = rootCursor.ReadInt32(),
            Start = rootCursor.ReadInt32(),
            End = rootCursor.ReadInt32(),
            X = ReadSingle(rootCursor)
        };

        if (rootCursor.Offset != NewsTickerDef.SerializedSize)
            throw new InvalidDataException($"NewsTickerDef consumed 0x{rootCursor.Offset:X} bytes instead of 0x{NewsTickerDef.SerializedSize:X}.");

        context.RegisterMenuObject(rootAddress, newsTicker);


        return newsTicker;
    }

    private static TextScrollDef? ReadTextScrollDefPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (ResolveMenuObjectWithoutSource(
                pointer,
                context,
                TextScrollDef.SerializedSize,
                "TextScrollDef",
                out TextScrollDef? existing))
            return existing;

        AlignStream(cursor, context, 4);
        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        ReadOnlyMemory<byte> rootBytes = context.Blocks.LoadMemory(cursor, TextScrollDef.SerializedSize, out XBlockAddress rootAddress);
        var rootCursor = new FastFileCursor(rootBytes, rootAddress);

        var textScroll = new TextScrollDef
        {
            DestinationAddress = rootAddress,
            StartTime = rootCursor.ReadInt32()
        };

        if (rootCursor.Offset != TextScrollDef.SerializedSize)
            throw new InvalidDataException($"TextScrollDef consumed 0x{rootCursor.Offset:X} bytes instead of 0x{TextScrollDef.SerializedSize:X}.");

        context.RegisterMenuObject(rootAddress, textScroll);


        return textScroll;
    }

    private static IReadOnlyList<ItemFloatExpression> ReadItemFloatExpressions(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative ItemFloatExpression count {count}.");

        if (pointer.Type == PointerType.Null)
        {
            if (count == 0)
                return [];
            throw new InvalidDataException(
                $"ItemFloatExpression[] has count {count}, but its pointer is null.");
        }

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<ItemFloatExpression[]>(pointer, checked(count * ItemFloatExpression.SerializedSize), "ItemFloatExpression[]");
            return context.ResolveMaterializedDirect<ItemFloatExpression[]>(pointer, "ItemFloatExpression[]");
        }

        if (!context.PointerReader.HasInlinePayload(pointer))
            throw UnsupportedDirectTablePointer(
                pointer,
                "ItemFloatExpression[]");

        AlignStream(cursor, context, 4);
        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        ReadOnlyMemory<byte> rootBytes = context.Blocks.LoadMemory(cursor, checked(count * ItemFloatExpression.SerializedSize), out XBlockAddress tableAddress);
        var rootCursor = new FastFileCursor(rootBytes, tableAddress);
        var expressions = new ItemFloatExpression[count];
        context.RegisterMaterialized(tableAddress, expressions, "ItemFloatExpression[]");

        for (int i = 0; i < expressions.Length; i++)
        {
            int rowStart = rootCursor.Offset;
            var target = (ItemFloatExpressionTarget)rootCursor.ReadInt32();
            XPointer<Statement> expressionPointer = ReadPointer<Statement>(rootCursor, context, XPointerResolutionMode.Direct);
            expressions[i] = new ItemFloatExpression
            {
                Target = target,
                Expression = expressionPointer
            };

            if (rootCursor.Offset - rowStart != ItemFloatExpression.SerializedSize)
                throw new InvalidDataException($"ItemFloatExpression consumed 0x{rootCursor.Offset - rowStart:X} bytes instead of 0x{ItemFloatExpression.SerializedSize:X}.");

            expressions[i].Statement = ReadStatementPointer(cursor, expressionPointer.Untyped, context);
        }

        return expressions;
    }

    private static IReadOnlyList<int> ReadInt32Array(FastFileCursor cursor, int count)
    {
        var values = new int[count];
        for (int i = 0; i < values.Length; i++)
            values[i] = cursor.ReadInt32();

        return values;
    }

    internal static XPointer<string> ReadXStringPointer(
        FastFileCursor cursor,
        DbLoadExecutionContext context)
    {
        // Menu roots are copied before their children are walked. A packed
        // XString may therefore name LARGE-stream bytes whose materialization
        // is completed later in the native child order. Preserve the raw cell
        // here and validate it when ReadXString resolves the child.
        return context.PointerReader.ReadDeferredPointer<string>(
            cursor,
            XPointerResolutionMode.Direct);
    }

    private static XPointer<T> ReadPointer<T>(
        FastFileCursor cursor,
        DbLoadExecutionContext context,
        XPointerResolutionMode resolutionMode)
    {
        return context.PointerReader.ReadPointer<T>(cursor, resolutionMode);
    }

    private static XPointer<T> ReadNullablePointer<T>(
        FastFileCursor cursor,
        DbLoadExecutionContext context,
        XPointerResolutionMode resolutionMode)
    {
        return context.PointerReader.ReadPointer<T>(cursor, resolutionMode, XPointerNullability.Nullable);
    }

    private static XPointer<T> ReadCountedPointer<T>(
        FastFileCursor cursor,
        DbLoadExecutionContext context,
        XPointerResolutionMode resolutionMode,
        int count,
        string fieldName)
    {
        if (count < 0)
            throw new InvalidDataException($"{fieldName} has invalid negative count {count}.");

        XPointerNullability nullability = count == 0
            ? XPointerNullability.Nullable
            : XPointerNullability.Required;
        return context.PointerReader.ReadPointer<T>(cursor, resolutionMode, nullability);
    }

    private static Vec4 ReadVec4(FastFileCursor cursor)
    {
        return new Vec4
        {
            A = ReadSingle(cursor),
            R = ReadSingle(cursor),
            G = ReadSingle(cursor),
            B = ReadSingle(cursor)
        };
    }

    private static float ReadSingle(FastFileCursor cursor)
    {
        return BitConverter.Int32BitsToSingle(cursor.ReadInt32());
    }

    private static bool ResolveMenuObjectWithoutSource<T>(
        XPointerReference pointer,
        DbLoadExecutionContext context,
        int serializedSize,
        string targetName,
        out T? value)
        where T : class
    {
        if (pointer.Type == PointerType.Null)
        {
            value = null;
            return true;
        }

        if (context.PointerReader.HasInlinePayload(pointer))
        {
            value = null;
            return false;
        }

        if (pointer.Type != PointerType.Offset)
        {
            throw new InvalidDataException(
                $"{targetName} pointer 0x{unchecked((uint)pointer.Raw):X8} " +
                $"has unsupported source form {pointer.Type}.");
        }

        context.PointerReader.ValidateOffsetPointerRange<T>(
            pointer,
            serializedSize,
            targetName);
        if (pointer.PackedAddress is { } address &&
            context.TryGetMenuObject(address, out value))
        {
            return true;
        }

        throw new InvalidDataException(
            $"Packed {targetName} pointer " +
            $"0x{unchecked((uint)pointer.Raw):X8} has no earlier " +
            "materialized Menu graph owner.");
    }

    private static void AlignStream(
        FastFileCursor cursor,
        DbLoadExecutionContext context,
        int alignment)
    {
        context.Blocks.AlignCurrent(alignment);
    }
}
