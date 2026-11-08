namespace MapConverter.Game.IW3.PS3.Extraction;

internal static partial class Iw3Ps3FastFileCatalog
{
    private sealed partial class Reader
    {
        // Source d1c58/d1bc8: FxImpactTable has 12 fixed 0x84-byte entries.
        // This source-only walk reaches later stock shader definitions; it
        // does not create a target impact table.
        private Pointer? ImpactPrefix(uint raw, Address cell) =>
            AssetPointer(raw, cell, "impactfx", 8, root =>
            {
                Push(4);
                try
                {
                    int start = root.FileOffset;
                    ReadString(U32(start));
                    Pointer? entries = ReadArray(U32(start + 4), 4,
                        Size(12, 0x84), "impact-entries");
                    if (entries is not null)
                    {
                        for (int index = 0; index < 12; index++)
                        {
                            int row = entries.Region.FileOffset + index * 0x84;
                            Address address = entries.Region.Address.Add(index * 0x84);
                            for (int effect = 0; effect < 33; effect++)
                                Fx(U32(row + effect * 4), address.Add(effect * 4));
                        }
                    }
                    return Remember(new Pointer("impactfx", root, []));
                }
                finally { Pop(); }
            });
    }
}
