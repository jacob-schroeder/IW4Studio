namespace MapConverter.Game.IW3.PS3.Extraction;

internal static partial class Iw3Ps3FastFileCatalog
{
    private sealed partial class Reader
    {
        // Source cad90/af030: 0x18-byte font, with 0x18-byte glyph rows.
        // Shader-prefix capture consumes its children without exporting a font.
        private Pointer? FontPrefix(uint raw, Address cell) =>
            AssetPointer(raw, cell, "font", 0x18, root =>
            {
                Push(4);
                try
                {
                    int start = root.FileOffset;
                    string? name = ReadString(U32(start));
                    var dependencies = new HashSet<int>();
                    Add(dependencies, Material(U32(start + 0xc), root.Address.Add(0xc)));
                    Add(dependencies, Material(U32(start + 0x10), root.Address.Add(0x10)));
                    ReadArray(U32(start + 0x14), 4,
                        Size(Count(U32(start + 8)), 0x18), "font-glyphs");
                    return Named("font", name, root, dependencies);
                }
                finally { Pop(); }
            });
    }
}
