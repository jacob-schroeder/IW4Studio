using System.Buffers.Binary;
using MapConverter.IW3.PC.Readers;

namespace MapConverter.IW3.PC.Assets;

/// <summary>Compares the source fields used to choose between duplicate asset providers.</summary>
internal static class Iw3PcAssetComparison
{
    internal static bool SameContents(Iw3PcZone left, Iw3PcAsset a, Iw3PcZone right, Iw3PcAsset b)
    {
        int x = a.Offset, y = b.Offset;
        bool Same(int p, int q, int size) => left.Bytes(p, size).SequenceEqual(right.Bytes(q, size));
        bool SameField(int offset, int size) => Same(x + offset, y + offset, size);

        bool Payload(int first, int second, int size)
        {
            if (size < 0 || (size > 0 && (first == 0 || second == 0)))
                throw new InvalidDataException($"Missing or invalid {a.Type} payload in '{a.Name}'.");
            return size == 0 || Same(first, second, size);
        }

        bool Binding(int first, int second, int nameOffset)
        {
            if (first == 0 || second == 0) return first == second;
            static string Key(string value) => (value.StartsWith(',') ? value[1..] : value).Replace('\\', '/').Trim();
            return StringComparer.OrdinalIgnoreCase.Equals(Key(left.String(first + nameOffset)), Key(right.String(second + nameOffset)));
        }

        switch (a.Type)
        {
            case "soundcurve":
                int knots = Int(left, x + 4);
                if (knots is < 0 or > 8 || Int(right, y + 4) is < 0 or > 8)
                    throw new InvalidDataException($"Invalid knot count in IW3 sound curve '{a.Name}'.");
                return SameField(4, 4 + knots * 8);
            case "loadedsound":
                return SameField(4, 4) && SameField(12, 24) &&
                    Payload(left.Pointer(x + 40), right.Pointer(y + 40), Int(left, x + 12));
            case "image":
                if (!SameField(0, 4) || !SameField(8, 5) || !SameField(20, 12)) return false;
                int imageA = left.Pointer(x + 4), imageB = right.Pointer(y + 4);
                if (imageA == 0 || imageB == 0) return imageA == imageB;
                return Same(imageA, imageB, 16) && Payload(imageA + 16, imageB + 16, Int(left, imageA + 12));
            case "material":
                if (!RecordedMaterialBindings(left, a) || !RecordedMaterialBindings(right, b))
                    return false;
                if (!SameField(4, 18) || !SameField(24, 39) || !Binding(left.Pointer(x + 64), right.Pointer(y + 64), 0))
                    return false;
                int texturesA = left.Pointer(x + 68), texturesB = right.Pointer(y + 68);
                if ((texturesA == 0) != (texturesB == 0)) return false;
                int textureCount = left.Bytes(x + 58, 1)[0];
                if (texturesA == 0 && textureCount > 0)
                    throw new InvalidDataException($"Missing texture table in IW3 material '{a.Name}'.");
                for (int i = 0; i < textureCount; ++i)
                {
                    int first = texturesA + i * 12, second = texturesB + i * 12;
                    if (!Same(first, second, 8)) return false;
                    int dataA = left.Pointer(first + 8), dataB = right.Pointer(second + 8);
                    if (left.Bytes(first + 7, 1)[0] == 11)
                    {
                        if (!Water(dataA, dataB)) return false;
                    }
                    else if (!Binding(dataA, dataB, 32)) return false;
                }
                return Table(x + 72, y + 72, left.Bytes(x + 59, 1)[0] * 32) &&
                    Table(x + 76, y + 76, left.Bytes(x + 60, 1)[0] * 8);
            default: return false;
        }

        bool Table(int first, int second, int size)
        {
            int dataA = left.Pointer(first), dataB = right.Pointer(second);
            return (dataA == 0) == (dataB == 0) && Payload(dataA, dataB, size);
        }

        bool Water(int first, int second)
        {
            if (first == 0 || second == 0) return first == second;
            if (Int(left, first + 12) < 0 || Int(left, first + 16) < 0 ||
                Int(right, second + 12) < 0 || Int(right, second + 16) < 0)
                throw new InvalidDataException($"Negative IW3 water dimensions in material '{a.Name}'.");
            if (!Same(first, second, 4) || !Same(first + 12, second + 12, 52) ||
                !Binding(left.Pointer(first + 64), right.Pointer(second + 64), 32)) return false;
            int samples = checked(Int(left, first + 12) * Int(left, first + 16));
            // Either water simulation table can be absent even when its dimensions are populated.
            foreach (var (offset, stride) in new[] { (4, 8), (8, 4) })
            {
                int dataA = left.Pointer(first + offset), dataB = right.Pointer(second + offset);
                if ((dataA == 0) != (dataB == 0) || dataA != 0 && !Payload(dataA, dataB, checked(samples * stride)))
                    return false;
            }
            return true;
        }
    }

    private static bool RecordedMaterialBindings(Iw3PcZone zone, Iw3PcAsset material)
    {
        bool Has(int pointer, string type) => pointer == 0 || material.Dependencies.Any(id =>
            id >= 0 && id < zone.Assets.Count && zone.Assets[id].Type == type && zone.Assets[id].Offset == pointer);
        int root = material.Offset;
        if (!Has(zone.Pointer(root + 64), "techniqueset")) return false;
        int count = zone.Bytes(root + 58, 1)[0], textures = zone.Pointer(root + 68);
        if (count != 0 && textures == 0)
            throw new InvalidDataException($"Missing IW3 material texture table in '{material.Name}'.");
        for (int i = 0; i < count; ++i)
        {
            int texture = textures + i * 12;
            int value = zone.Pointer(texture + 8);
            if (zone.Bytes(texture + 7, 1)[0] == 11 && value != 0)
                value = zone.Pointer(value + 64);
            if (!Has(value, "image")) return false;
        }
        return true;
    }

    private static int Int(Iw3PcZone zone, int field) => BinaryPrimitives.ReadInt32LittleEndian(zone.Bytes(field, 4));
}
