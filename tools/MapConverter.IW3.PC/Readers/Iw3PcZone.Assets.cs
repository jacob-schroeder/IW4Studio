using MapConverter.IW3.PC.Assets.TechniqueSet;
namespace MapConverter.IW3.PC.Readers;

public sealed partial class Iw3PcZone
{
    private sealed partial class Loader
    {
        // File sizes and offsets below describe the little-endian IW3 PC layout, independent of host word size.
        private static (string Name, int Size, int NameOffset) Layout(int type) => type switch
        {
            1 => ("physpreset", 44, 0), 2 => ("xanim", 88, 0), 3 => ("xmodel", 220, 0),
            4 => ("material", 80, 0), 5 => ("techniqueset", 148, 0), 6 => ("image", 36, 32),
            7 => ("sound", 12, 0), 8 => ("soundcurve", 72, 0), 9 => ("loadedsound", 44, 0),
            10 or 11 => ("clipmap", 284, 0), 12 => ("comworld", 16, 0), 13 => ("gameworldsp", 44, 0), 14 => ("gameworldmp", 4, 0),
            15 => ("mapents", 12, 0), 16 => ("gfxworld", 732, 0), 17 => ("lightdef", 16, 0),
            19 => ("font", 24, 0), 20 => ("menulist", 12, 0), 21 => ("menu", 284, 0),
            22 => ("localize", 8, 4), 23 => ("weapon", 2168, 0), 25 => ("fx", 32, 0),
            26 => ("impactfx", 8, 0), 31 => ("rawfile", 12, 0), 32 => ("stringtable", 16, 0),
            _ => throw new InvalidDataException($"Unsupported IW3 PC asset type {type}.")
        };

        private void ReadAsset(int type, int body)
        {
            if (type == 22) XString(body); // The localized value precedes its key.
            XString(body + Layout(type).NameOffset);
            switch (type)
            {
                case 1: XString(body + 28); break;
                case 2: ReadAnimation(body); break;
                case 3: ReadModel(body); break;
                case 4: ReadMaterial(body); break;
                case 5:
                    for (int i = 0; i < Iw3TechniqueSlots.Definitions.Length; ++i)
                        Data(body + 12 + i * 4, 1, 8, visit: ReadTechnique, shared: true);
                    break;
                case 6:
                    Data(body + 4, 1, 16, visit: def => Take(Temp, 1, Int(def + 12)), shared: true, block: Temp);
                    break;
                case 7: Data(body + 4, Int(body + 8), 92, visit: ReadSoundAlias, shared: true); break;
                case 9: Data(body + 40, Int(body + 12), 1, 1, shared: true, block: Temp); break;
                case 10 or 11: ReadClipMap(body); break;
                case 12: Data(body + 12, Int(body + 8), 68, visit: light => XString(light + 64)); break;
                case 13: ReadPathData(body + 4); break;
                case 15: Data(body + 4, Int(body + 8), 1, 1); break;
                case 16: ReadGfxWorld(body); break;
                case 17: Asset(body + 4, 6); break;
                case 19:
                    Asset(body + 12, 4);
                    Asset(body + 16, 4);
                    Data(body + 20, Int(body + 8), 24, shared: true);
                    break;
                case 20: Data(body + 8, Int(body + 4), 4, visit: menu => Asset(menu, 21)); break;
                case 21: ReadMenu(body); break;
                case 23: ReadWeapon(body); break;
                case 25: Data(body + 28, checked(Int(body + 16) + Int(body + 20) + Int(body + 24)), 252, visit: ReadFxElement); break;
                case 26: Data(body + 4, 12 * 33, 4, visit: field => Asset(field, 25)); break;
                case 31: Data(body + 8, checked(Int(body + 4) + 1), 1, 1); break;
                case 32: Data(body + 12, checked(Int(body + 4) * Int(body + 8)), 4, visit: XString); break;
            }
        }

        private void ReadMaterial(int body)
        {
            Asset(body + 64, 5);
            Data(body + 68, Byte(body + 58), 12, visit: texture =>
            {
                if (Byte(texture + 7) == 11)
                    Data(texture + 8, 1, 68, visit: water =>
                    {
                        int samples = checked(Int(water + 12) * Int(water + 16));
                        Data(water + 4, samples, 8);
                        Data(water + 8, samples, 4);
                        Asset(water + 64, 6);
                    }, shared: true);
                else Asset(texture + 8, 6);
            }, shared: true);
            Data(body + 72, Byte(body + 59), 32, 16, shared: true);
            Data(body + 76, Byte(body + 60), 8, shared: true);
        }

        private void ReadTechnique(int technique)
        {
            int count = Short(technique + 6);
            int passes = Take(Virtual, 1, checked(count * 20));
            for (int i = 0; i < count; ++i)
            {
                int pass = passes + i * 20;
                Data(pass, 1, 100, shared: true);
                Data(pass + 4, 1, 16, visit: ReadShader, shared: true);
                Data(pass + 8, 1, 16, visit: ReadShader, shared: true);
                Data(pass + 16, Byte(pass + 12) + Byte(pass + 13) + Byte(pass + 14), 8, visit: arg =>
                {
                    if (Short(arg) is 1 or 7) Data(arg + 4, 1, 16, shared: true);
                });
            }
            XString(technique);
        }

        private void ReadShader(int shader)
        {
            XString(shader);
            Data(shader + 8, Short(shader + 12), 4);
        }

        private void ReadSoundAlias(int alias)
        {
            for (int i = 0; i < 4; ++i) XString(alias + i * 4);
            Data(alias + 16, 1, 12, visit: file =>
            {
                if (Byte(file) == 1) Asset(file + 4, 9);
                else
                {
                    XString(file + 4);
                    XString(file + 8);
                }
            }, shared: true);
            Asset(alias + 72, 8);
            Data(alias + 88, 1, 408, visit: speaker => XString(speaker + 4), shared: true);
        }

        private void ReadFxElement(int row)
        {
            Data(row + 180, Byte(row + 178) + 1, 96);
            Data(row + 184, Byte(row + 179) + 1, 48);
            int type = Byte(row + 176), count = Byte(row + 177);
            void Visual(int field)
            {
                if (type <= 4) Asset(field, 4);
                else if (type == 5) Asset(field, 3);
                else if (type is 8 or 10) XString(field);
            }
            if (type == 9) Data(row + 188, count * 2, 4, visit: field => Asset(field, 4));
            else if (count > 1) Data(row + 188, count, 4, visit: Visual);
            else Visual(row + 188);
            XString(row + 216);
            XString(row + 220);
            XString(row + 224);
            Data(row + 244, 1, 28, visit: trail =>
            {
                Data(trail + 16, Int(trail + 12), 20);
                Data(trail + 24, Int(trail + 20), 2, 2);
            });
        }
    }
}
