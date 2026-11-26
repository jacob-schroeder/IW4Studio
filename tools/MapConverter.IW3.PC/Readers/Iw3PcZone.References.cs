using MapConverter.IW3.PC.Assets;

namespace MapConverter.IW3.PC.Readers;

public sealed partial class Iw3PcZone
{
    private sealed partial class Loader
    {
        private void CollectNamedReferences(Iw3PcAsset asset)
        {
            int body = asset.Offset;
            if (asset.Type == "sound")
            {
                int count = Int(body + 8), head = zone.Pointer(body + 4);
                if (count is < 0 or >= 65536 || count != 0 && head == 0)
                    throw new InvalidDataException($"Invalid IW3 PC sound alias list '{asset.Name}'.");
                for (int i = 0; i < count; ++i)
                {
                    int alias = head + i * 92;
                    AddName("sound", alias + 8);
                    AddName("sound", alias + 12);
                    int file = zone.Pointer(alias + 16);
                    if (file == 0 || Byte(file) != 2) continue;
                    string dir = zone.String(file + 4), name = zone.String(file + 8);
                    if (string.IsNullOrEmpty(name))
                        throw new InvalidDataException($"Missing streamed sound filename in '{asset.Name}'.");
                    asset.StreamedSounds.Add(("sound/" + (dir.Length == 0 ? "" : dir + "/") + name).Replace('\\', '/'));
                }
            }
            else if (asset.Type == "fx")
            {
                int count = checked(Int(body + 16) + Int(body + 20) + Int(body + 24)), rows = zone.Pointer(body + 28);
                if (count is < 0 or >= 65536 || count != 0 && rows == 0)
                    throw new InvalidDataException($"Invalid IW3 PC effect element table '{asset.Name}'.");
                for (int i = 0; i < count; ++i)
                {
                    int row = rows + i * 252;
                    AddName("fx", row + 216);
                    AddName("fx", row + 220);
                    AddName("fx", row + 224);
                    int type = Byte(row + 176), visuals = Byte(row + 177);
                    if (type is not (8 or 10)) continue;
                    int fields = visuals > 1 ? zone.Pointer(row + 188) : row + 188;
                    if (fields == 0 && visuals != 0)
                        throw new InvalidDataException($"Missing effect visuals in '{asset.Name}'.");
                    for (int j = 0; j < visuals; ++j) AddName(type == 8 ? "sound" : "fx", fields + 4 * j);
                }
            }
            else if (asset.Type == "weapon")
            {
                for (int i = 0; i < 33; ++i) AddName("xanim", body + 80 + i * 4);
            }

            void AddName(string type, int field)
            {
                string name = zone.String(field);
                if (name.Length != 0) asset.References.Add((type, name));
            }
        }
    }
}
