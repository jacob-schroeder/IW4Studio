using System.Buffers.Binary;
using System.Text;
using IW4.Assets.Assets.ComWorld;
using IW4.Assets.Math;

namespace IW4Map;

internal static class D3dbspPrimaryLightCodec
{
    private const int DiskPrimaryLightSize = 128;
    private const int DefinitionNameOffset = 64;
    private const int DefinitionNameSize = 64;

    public static ComWorldAsset DecodeComWorld(string name, ReadOnlySpan<byte> data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        IReadOnlyList<ComPrimaryLight> lights = Decode(data);
        if (lights.Count <= 1)
        {
            throw new InvalidDataException(
                "A v22 ComWorld must contain the none sentinel and at least one map light.");
        }
        _ = GetLastSunPrimaryLightIndex(lights);

        return new ComWorldAsset
        {
            Name = name,
            IsInUse = 1,
            PrimaryLightCount = lights.Count,
            PrimaryLights = lights
        };
    }

    public static int GetLastSunPrimaryLightIndex(IReadOnlyList<ComPrimaryLight> lights)
    {
        ArgumentNullException.ThrowIfNull(lights);
        if (lights.Count == 0 || lights[0] is null || lights[0].Type != GfxLightType.None)
            throw new InvalidDataException("A v22 primary-light table must begin with the none sentinel.");
        for (int index = 1; index < lights.Count; index++)
        {
            if (lights[index] is null)
                throw new InvalidDataException($"Primary light row {index} is null.");
        }

        int firstNonSunIndex = 1;
        while (firstNonSunIndex < lights.Count &&
               lights[firstNonSunIndex].Type == GfxLightType.Directional)
        {
            firstNonSunIndex++;
        }

        for (int index = firstNonSunIndex; index < lights.Count; index++)
        {
            if (lights[index].Type == GfxLightType.Directional)
            {
                throw new InvalidDataException(
                    "Directional primary lights must form one contiguous table prefix after the none sentinel.");
            }
        }

        return firstNonSunIndex - 1;
    }

    public static IReadOnlyList<ComPrimaryLight> Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length % DiskPrimaryLightSize != 0)
        {
            throw new InvalidDataException(
                $"PRIMARY_LIGHTS length {data.Length} is not divisible by {DiskPrimaryLightSize}.");
        }

        var lights = new ComPrimaryLight[data.Length / DiskPrimaryLightSize];
        for (int index = 0; index < lights.Length; index++)
        {
            ReadOnlySpan<byte> row = data.Slice(index * DiskPrimaryLightSize, DiskPrimaryLightSize);
            GfxLightType type = (GfxLightType)row[0];
            float cosHalfFovOuter = ReadSingle(row, 44);
            float cosHalfFovInner = ReadSingle(row, 48);
            int exponent = BinaryPrimitives.ReadInt32LittleEndian(row[52..]);
            if ((uint)exponent > byte.MaxValue)
            {
                throw new InvalidDataException(
                    $"PRIMARY_LIGHTS row {index} has exponent {exponent}, outside the runtime byte range.");
            }

            float rotationLimit = ReadSingle(row, 56);
            string? definitionName = null;
            float cosHalfFovExpanded;
            if (type is not (GfxLightType.None or GfxLightType.Directional))
            {
                definitionName = ReadFixedString(
                    row.Slice(DefinitionNameOffset, DefinitionNameSize),
                    index);
                if (cosHalfFovOuter >= cosHalfFovInner)
                    cosHalfFovInner = (float)((double)cosHalfFovOuter * 0.75 + 0.25);

                cosHalfFovExpanded = rotationLimit switch
                {
                    1.0f => cosHalfFovOuter,
                    _ when rotationLimit > -cosHalfFovOuter =>
                        CosOfSumOfArcCos(cosHalfFovOuter, rotationLimit),
                    _ => -1.0f
                };
            }
            else
            {
                cosHalfFovExpanded = cosHalfFovOuter;
            }

            lights[index] = new ComPrimaryLight
            {
                Type = type,
                CanUseShadowMapRaw = row[1],
                Exponent = (byte)exponent,
                Unused = 0,
                Color = ReadVec3(row, 4),
                Dir = ReadVec3(row, 16),
                Origin = ReadVec3(row, 28),
                Radius = ReadSingle(row, 40),
                CosHalfFovOuter = cosHalfFovOuter,
                CosHalfFovInner = cosHalfFovInner,
                CosHalfFovExpanded = cosHalfFovExpanded,
                RotationLimit = rotationLimit,
                TranslationLimit = ReadSingle(row, 60),
                DefName = definitionName
            };
        }

        return Array.AsReadOnly(lights);
    }

    public static byte[] Encode(IReadOnlyList<ComPrimaryLight> lights)
    {
        ArgumentNullException.ThrowIfNull(lights);
        var data = new byte[checked(lights.Count * DiskPrimaryLightSize)];
        for (int index = 0; index < lights.Count; index++)
        {
            ComPrimaryLight light = lights[index] ??
                throw new InvalidDataException($"Primary light row {index} is null.");
            Span<byte> row = data.AsSpan(index * DiskPrimaryLightSize, DiskPrimaryLightSize);
            row[0] = (byte)light.Type;
            row[1] = light.CanUseShadowMapRaw;
            WriteVec3(row, 4, light.Color);
            WriteVec3(row, 16, light.Dir);
            WriteVec3(row, 28, light.Origin);
            WriteSingle(row, 40, light.Radius);
            WriteSingle(row, 44, light.CosHalfFovOuter);
            WriteSingle(row, 48, light.CosHalfFovInner);
            BinaryPrimitives.WriteInt32LittleEndian(row[52..], light.Exponent);
            WriteSingle(row, 56, light.RotationLimit);
            WriteSingle(row, 60, light.TranslationLimit);

            if (light.Type is not (GfxLightType.None or GfxLightType.Directional))
            {
                WriteFixedString(
                    row.Slice(DefinitionNameOffset, DefinitionNameSize),
                    light.DefName,
                    index);
            }
        }

        return data;
    }

    private static Vec3 ReadVec3(ReadOnlySpan<byte> row, int offset) => new()
    {
        X = ReadSingle(row, offset),
        Y = ReadSingle(row, offset + 4),
        Z = ReadSingle(row, offset + 8)
    };

    private static void WriteVec3(Span<byte> row, int offset, Vec3 value)
    {
        WriteSingle(row, offset, value.X);
        WriteSingle(row, offset + 4, value.Y);
        WriteSingle(row, offset + 8, value.Z);
    }

    private static float ReadSingle(ReadOnlySpan<byte> row, int offset) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(row[offset..]));

    private static void WriteSingle(Span<byte> row, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(row[offset..], BitConverter.SingleToInt32Bits(value));

    private static string ReadFixedString(ReadOnlySpan<byte> bytes, int index)
    {
        int terminator = bytes.IndexOf((byte)0);
        if (terminator < 0)
        {
            throw new InvalidDataException(
                $"PRIMARY_LIGHTS row {index} definition name is not null terminated.");
        }

        return Encoding.Latin1.GetString(bytes[..terminator]);
    }

    private static void WriteFixedString(
        Span<byte> destination,
        string? value,
        int index)
    {
        value ??= string.Empty;
        int byteCount = Encoding.Latin1.GetByteCount(value);
        if (byteCount >= destination.Length || value.Any(character => character > byte.MaxValue))
        {
            throw new InvalidDataException(
                $"Primary light row {index} definition name must fit in 63 Latin-1 bytes.");
        }

        Encoding.Latin1.GetBytes(value, destination);
    }

    private static float CosOfSumOfArcCos(float cos0, float cos1)
    {
        // Preserve linker_pc's explicit float spills around x87 intermediates.
        double cosProduct = (double)cos0 * cos1;
        float sinSq1 = (float)(1.0 - (double)cos1 * cos1);
        float sinSq0 = (float)(1.0 - (double)cos0 * cos0);
        float sinProduct = (float)((double)sinSq1 * sinSq0);
        float sin = MathF.Sqrt(sinProduct);
        return (float)(cosProduct - sin);
    }
}
