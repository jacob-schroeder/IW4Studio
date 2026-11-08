namespace MapConverter.Game.IW3.PS3.Extraction;

internal static partial class Iw3Ps3FastFileCatalog
{
    private sealed partial class Reader
    {
        // Source-only menu traversal lets the stock shader prefix pass the
        // one-item ui/default.menu list. No IW4 menu asset is constructed.
        private Pointer? MenuListPrefix(uint raw, Address cell) =>
            AssetPointer(raw, cell, "menulist", 0x0c, root =>
            {
                Push(4);
                try
                {
                    int start = root.FileOffset;
                    string? name = ReadString(U32(start));
                    int count = Count(U32(start + 4));
                    uint arrayRaw = U32(start + 8);
                    Pointer? cells = ReadArray(arrayRaw, 4, Size(count, 4), "menu-pointers");
                    var dependencies = new HashSet<int>();
                    if (cells is not null && arrayRaw == uint.MaxValue)
                    {
                        for (int index = 0; index < count; index++)
                        {
                            int row = cells.Region.FileOffset + index * 4;
                            Add(dependencies, MenuDefinitionPrefix(U32(row),
                                cells.Region.Address.Add(index * 4)));
                        }
                    }
                    else if (count != 0)
                        throw Unsupported("menu list has no inline pointer cells");
                    return Named("menulist", name, root, dependencies);
                }
                finally { Pop(); }
            });

        private Pointer? MenuDefinitionPrefix(uint raw, Address cell) =>
            AssetPointer(raw, cell, "menu-definition", 0x134, root =>
            {
                Push(4);
                try
                {
                    int start = root.FileOffset;
                    ReadMenuWindowPrefix(start, root.Address);
                    ReadString(U32(start + 0xa8));
                    ReadString(U32(start + 0xdc));
                    ReadString(U32(start + 0xe0));
                    ReadString(U32(start + 0xe4));
                    MenuKeyHandlersPrefix(U32(start + 0xe8), root.Address.Add(0xe8));
                    MenuStatementPrefix(start + 0xec, root.Address.Add(0xec));
                    ReadString(U32(start + 0xf4));
                    ReadString(U32(start + 0xf8));
                    MenuStatementPrefix(start + 0x120, root.Address.Add(0x120));
                    MenuStatementPrefix(start + 0x128, root.Address.Add(0x128));
                    int count = Count(U32(start + 0xb0));
                    uint arrayRaw = U32(start + 0x130);
                    Pointer? cells = ReadArray(arrayRaw, 4, Size(count, 4), "menu-item-pointers");
                    if (cells is not null && arrayRaw == uint.MaxValue)
                    {
                        for (int index = 0; index < count; index++)
                        {
                            int row = cells.Region.FileOffset + index * 4;
                            MenuItemPrefix(U32(row), cells.Region.Address.Add(index * 4));
                        }
                    }
                    else if (count != 0)
                        throw Unsupported("menu definition has no inline item cells");
                    return Remember(new Pointer("menu-definition", root, []));
                }
                finally { Pop(); }
            });

        private Pointer? MenuItemPrefix(uint raw, Address cell)
        {
            Pointer? item = ReadArray(raw, 4, 0x1d4, "menu-item");
            SetAlias(cell, item);
            if (item is null || raw != uint.MaxValue) return item;
            Region root = item.Region;
            int start = root.FileOffset;
            ReadMenuWindowPrefix(start, root.Address);
            ReadString(U32(start + 0x134));
            foreach (int offset in new[] { 0x140, 0x144, 0x148, 0x14c, 0x150,
                0x154, 0x158, 0x15c, 0x160, 0x164, 0x16c })
                ReadString(U32(start + offset));
            MenuKeyHandlersPrefix(U32(start + 0x168), root.Address.Add(0x168));
            SoundPrefix(U32(start + 0x174), root.Address.Add(0x174));
            MenuTypedDataPrefix(start, root.Address);
            foreach (int offset in new[] { 0x194, 0x19c, 0x1a4, 0x1ac,
                0x1b4, 0x1bc, 0x1c4, 0x1cc })
                MenuStatementPrefix(start + offset, root.Address.Add(offset));
            return item;
        }

        private void ReadMenuWindowPrefix(int start, Address address)
        {
            ReadString(U32(start));
            ReadString(U32(start + 0x34));
            if (U32(start + 0xa4) != 0)
                Material(U32(start + 0xa4), address.Add(0xa4));
        }

        private void MenuKeyHandlersPrefix(uint raw, Address cell)
        {
            while (raw != 0)
            {
                Pointer? node = ReadArray(raw, 4, 12, "menu-key-handler");
                SetAlias(cell, node);
                if (node is null || raw != uint.MaxValue) return;
                int row = node.Region.FileOffset;
                ReadString(U32(row + 4));
                raw = U32(row + 8);
                cell = node.Region.Address.Add(8);
            }
        }

        private void MenuStatementPrefix(int start, Address address)
        {
            int count = Count(U32(start));
            uint raw = U32(start + 4);
            Pointer? cells = ReadArray(raw, 4, Size(count, 4), "menu-expression-pointers");
            if (cells is null)
            {
                if (count != 0) throw Invalid("menu expression has entries without a pointer array");
                return;
            }
            if (raw != uint.MaxValue) return;
            for (int index = 0; index < count; index++)
            {
                int row = cells.Region.FileOffset + index * 4;
                uint entryRaw = U32(row);
                Pointer? entry = ReadArray(entryRaw, 4, 12, "menu-expression-entry");
                SetAlias(cells.Region.Address.Add(index * 4), entry);
                if (entry is null || entryRaw != uint.MaxValue || U32(entry.Region.FileOffset) == 0)
                    continue;
                if (U32(entry.Region.FileOffset + 4) == 2)
                    ReadString(U32(entry.Region.FileOffset + 8));
            }
        }

        private void MenuTypedDataPrefix(int start, Address address)
        {
            uint raw = U32(start + 0x18c);
            int type = Count(U32(start + 0x108));
            if (type == 13)
            {
                ReadString(raw);
                return;
            }
            int size = type switch
            {
                6 => 0x168,
                4 or 9 or 16 or 18 or 11 or 14 or 10 or 0 or 17 => 0x20,
                12 => 0x188,
                _ => 0
            };
            if (size == 0) return;
            Pointer? data = ReadArray(raw, 4, size, "menu-typed-data");
            SetAlias(address.Add(0x18c), data);
            if (data is null || raw != uint.MaxValue) return;
            int row = data.Region.FileOffset;
            if (type == 6)
            {
                ReadString(U32(row + 0x134));
                if (U32(row + 0x164) != 0)
                    Material(U32(row + 0x164), data.Region.Address.Add(0x164));
            }
            else if (type == 12)
                for (int index = 0; index < 64; index++)
                    ReadString(U32(row + index * 4));
        }
    }
}
