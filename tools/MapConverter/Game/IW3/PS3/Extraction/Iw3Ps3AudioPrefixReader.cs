namespace MapConverter.Game.IW3.PS3.Extraction;

internal static partial class Iw3Ps3FastFileCatalog
{
    private sealed partial class Reader
    {
        // Source-only traversal. COD4 PS3 0xccb48 loads a 12-byte sound-list
        // root, then 0xccac0 walks 0x5c-byte alias rows through 0xcc908.
        private Pointer? SoundPrefix(uint raw, Address cell) =>
            AssetPointer(raw, cell, "sound", 0x0c, root =>
            {
                Push(4);
                try
                {
                    int start = root.FileOffset;
                    string? name = ReadString(U32(start));
                    int count = Count(U32(start + 8));
                    uint rawAliases = U32(start + 4);
                    Pointer? aliases = ReadArray(rawAliases, 4,
                        Size(count, 0x5c), "sound-alias-rows");
                    var dependencies = new HashSet<int>();
                    if (aliases is not null && rawAliases == uint.MaxValue)
                    {
                        for (int index = 0; index < count; index++)
                        {
                            int row = aliases.Region.FileOffset + index * 0x5c;
                            Address address = aliases.Region.Address.Add(index * 0x5c);
                            for (int offset = 0; offset < 0x10; offset += 4)
                                ReadString(U32(row + offset));
                            Add(dependencies, SoundFilePrefix(U32(row + 0x10)));
                            Add(dependencies, SoundCurvePrefix(U32(row + 0x48),
                                address.Add(0x48)));
                            SpeakerMapPrefix(U32(row + 0x58));
                        }
                    }
                    else if (count != 0)
                        throw Unsupported("sound alias list has no inline alias rows");
                    return Named("sound", name, root, dependencies);
                }
                finally { Pop(); }
            });

        // 0xcc068 loads the 0x48-byte curve and its name XString. Some stock
        // curves point at an empty source string; a source-only traversal can
        // retain that pointer without inventing an asset identity.
        private Pointer? SoundCurvePrefix(uint raw, Address cell) =>
            AssetPointer(raw, cell, "soundcurve", 0x48, root =>
            {
                Push(4);
                try
                {
                    string? name = ReadString(U32(root.FileOffset));
                    return string.IsNullOrEmpty(name)
                        ? new Pointer("soundcurve", root, [])
                        : Named("soundcurve", name, root);
                }
                finally { Pop(); }
            });

        // 0xcc8c0: SoundFile is 0x10 bytes. Tag 1 owns a LoadedSound asset;
        // other tags use an embedded streamed-file union. No codec is read.
        private Pointer? SoundFilePrefix(uint raw)
        {
            Pointer? file = ReadArray(raw, 4, 0x10, "sound-file");
            if (file is null || raw != uint.MaxValue) return file;
            int start = file.Region.FileOffset;
            var dependencies = new HashSet<int>();
            if (_data[start] == 1)
                Add(dependencies, LoadedSoundPrefix(U32(start + 4),
                    file.Region.Address.Add(4)));
            else if (U32(start + 4) == 0)
            {
                ReadString(U32(start + 8));
                ReadString(U32(start + 0x0c));
            }
            return Remember(file with { Dependencies = dependencies.Order().ToArray() });
        }

        // 0xcc3d8/0xbe0b0: root 0x1c, name XString, 0x18-byte format header
        // at +4, and a length-prefixed payload aligned to 0x80 in block 4.
        private Pointer? LoadedSoundPrefix(uint raw, Address cell) =>
            AssetPointer(raw, cell, "loadedsound", 0x1c, root =>
            {
                Push(4);
                try
                {
                    int start = root.FileOffset;
                    string? name = ReadString(U32(start));
                    PresenceArray(U32(start + 0x18), 0x80,
                        Count(U32(start + 4)), "loaded-sound-data");
                    return Named("loadedsound", name, root);
                }
                finally { Pop(); }
            });

        // 0xcc908: SpeakerMap is inline 0x198 bytes; its sole child is the
        // name XString at +4.
        private Pointer? SpeakerMapPrefix(uint raw)
        {
            Pointer? map = ReadArray(raw, 4, 0x198, "speaker-map");
            if (map is not null && raw == uint.MaxValue)
                ReadString(U32(map.Region.FileOffset + 4));
            return map;
        }
    }
}
