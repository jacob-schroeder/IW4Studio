namespace MapConverter.Game.IW3.PS3.Extraction;

internal static partial class Iw3Ps3FastFileCatalog
{
    private sealed partial class Reader
    {
        // Source d3218: traverse the 0x878-byte COD4 weapon solely to reach
        // later stock shader definitions. No target weapon is emitted.
        private Pointer? WeaponPrefix(uint raw, Address cell) =>
            AssetPointer(raw, cell, "weapon", 0x878, root =>
            {
                Push(4);
                try
                {
                    int start = root.FileOffset;
                    Address address = root.Address;
                    ReadString(U32(start));
                    ReadString(U32(start + 4));
                    ReadString(U32(start + 8));
                    for (int index = 0; index < 16; index++)
                        Model(U32(start + 0xc + index * 4), address.Add(0xc + index * 4));
                    Model(U32(start + 0x4c), address.Add(0x4c));
                    for (int index = 0; index < 33; index++)
                        ReadString(U32(start + 0x50 + index * 4));
                    ReadString(U32(start + 0xd4));
                    Fx(U32(start + 0x14c), address.Add(0x14c));
                    Fx(U32(start + 0x150), address.Add(0x150));
                    for (int offset = 0x154; offset <= 0x204; offset += 4)
                        WeaponSoundNamePrefix(U32(start + offset), address.Add(offset));
                    uint soundArrayRaw = U32(start + 0x208);
                    Pointer? soundNames = ReadArray(soundArrayRaw, 4, 29 * 4,
                        "weapon-sound-name-pointers");
                    if (soundNames is not null && soundArrayRaw == uint.MaxValue)
                        for (int index = 0; index < 29; index++)
                        {
                            int row = soundNames.Region.FileOffset + index * 4;
                            WeaponSoundNamePrefix(U32(row),
                                soundNames.Region.Address.Add(index * 4));
                        }
                    for (int offset = 0x20c; offset <= 0x218; offset += 4)
                        Fx(U32(start + offset), address.Add(offset));
                    Material(U32(start + 0x21c), address.Add(0x21c));
                    Material(U32(start + 0x220), address.Add(0x220));
                    for (int index = 0; index < 16; index++)
                        Model(U32(start + 0x2bc + index * 4), address.Add(0x2bc + index * 4));
                    for (int offset = 0x2fc; offset <= 0x308; offset += 4)
                        Model(U32(start + offset), address.Add(offset));
                    Material(U32(start + 0x30c), address.Add(0x30c));
                    Material(U32(start + 0x314), address.Add(0x314));
                    foreach (int offset in new[] { 0x324, 0x32c, 0x340 })
                        ReadString(U32(start + offset));
                    foreach (int offset in new[] { 0x430, 0x434, 0x518, 0x524 })
                        Material(U32(start + offset), address.Add(offset));
                    ReadString(U32(start + 0x53c));
                    Model(U32(start + 0x584), address.Add(0x584));
                    Fx(U32(start + 0x58c), address.Add(0x58c));
                    Fx(U32(start + 0x594), address.Add(0x594));
                    WeaponSoundNamePrefix(U32(start + 0x598), address.Add(0x598));
                    WeaponSoundNamePrefix(U32(start + 0x59c), address.Add(0x59c));
                    Fx(U32(start + 0x6a8), address.Add(0x6a8));
                    Fx(U32(start + 0x6c4), address.Add(0x6c4));
                    WeaponSoundNamePrefix(U32(start + 0x6c8), address.Add(0x6c8));
                    ReadString(U32(start + 0x76c));
                    ReadArray(U32(start + 0x774), 4,
                        Size(Count(U32(start + 0x784)), 8), "weapon-accuracy-graph-0");
                    ReadArray(U32(start + 0x77c), 4,
                        Size(Count(U32(start + 0x784)), 8), "weapon-accuracy-graph-1");
                    ReadString(U32(start + 0x770));
                    ReadArray(U32(start + 0x778), 4,
                        Size(Count(U32(start + 0x788)), 8), "weapon-accuracy-graph-2");
                    ReadArray(U32(start + 0x780), 4,
                        Size(Count(U32(start + 0x788)), 8), "weapon-accuracy-graph-3");
                    foreach (int offset in new[] { 0x7dc, 0x7e0, 0x7f4, 0x868, 0x86c })
                        ReadString(U32(start + offset));
                    return Remember(new Pointer("weapon", root, []));
                }
                finally { Pop(); }
            });

        // Source c4498 -> 1ccb08 -> cd658: one owned XString cell, then
        // runtime lookup of an existing type-9 sound asset.
        private void WeaponSoundNamePrefix(uint raw, Address cell)
        {
            Pointer? wrapper = ReadArray(raw, 4, 4, "weapon-sound-name");
            SetAlias(cell, wrapper);
            if (wrapper is not null && raw == uint.MaxValue)
                ReadString(U32(wrapper.Region.FileOffset));
        }
    }
}
