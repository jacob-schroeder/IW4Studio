using MapConverter.IW3.PC.Assets.TechniqueSet;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using MapConverter.IW3.PC.Readers;

namespace MapConverter.IW3.PC.Assets.Materials;

internal static class Iw3PcMaterialExporter
{
    private static readonly string[] CameraRegions = ["lit", "decal", "emissive", "none"];
    private static readonly string[] Semantics = ["2D", "function", "colorMap", "unused1", "unused2",
        "normalMap", "unused3", "unused4", "specularMap", "unused5", "unused6", "waterMap"];
    private static readonly string[] Filters = ["disabled", "nearest", "linear", "aniso2x", "aniso4x"];
    private static readonly string[] MipMaps = ["disabled", "nearest", "linear"];

    internal static string FileName(string name)
    {
        if (name.StartsWith('*'))
        {
            int suffix = name.IndexOf('(');
            name = "generated/" + (suffix < 0 ? name : name[..suffix]).Replace('*', '_');
        }
        return "materials/" + name + ".json";
    }

    internal static byte[] ReadJson(Iw3PcZone zone, Iw3PcAsset asset)
    {
        int body = asset.Offset;
        byte U8(int field) => zone.Bytes(field, 1)[0];
        uint U32(int field) => BinaryPrimitives.ReadUInt32LittleEndian(zone.Bytes(field, 4));
        float Single(int field) => BinaryPrimitives.ReadSingleLittleEndian(zone.Bytes(field, 4));
        float[] Vector(int first, int count) => Enumerable.Range(0, count).Select(i => Single(first + 4 * i)).ToArray();
        string Name(int pointer, int offset)
        {
            if (pointer == 0) return string.Empty;
            string value = zone.String(pointer + offset);
            return value.StartsWith(',') ? value[1..] : value;
        }
        string? Label(string[] values, int index) => index < values.Length ? values[index] : null;

        object? ReadWater(int water)
        {
            if (water == 0) return null;
            int m = BinaryPrimitives.ReadInt32LittleEndian(zone.Bytes(water + 12, 4));
            int n = BinaryPrimitives.ReadInt32LittleEndian(zone.Bytes(water + 16, 4));
            string? Samples(int field, int stride)
            {
                int ptr = zone.Pointer(field);
                if (ptr == 0) return null;
                int bytes = checked(m * n * stride);
                if (bytes < 0 || bytes > 256 * 1024 * 1024)
                    throw new InvalidDataException($"IW3 material '{asset.Name}' water sample table has invalid size {bytes}.");
                return Convert.ToBase64String(zone.Bytes(ptr, bytes));
            }
            return new
            {
                floatTime = Single(water), m, n, lx = Single(water + 20), lz = Single(water + 24),
                gravity = Single(water + 28), windvel = Single(water + 32), winddir = Vector(water + 36, 2),
                amplitude = Single(water + 44), codeConstant = Vector(water + 48, 4),
                h0 = Samples(water + 4, 8), wTerm = Samples(water + 8, 4)
            };
        }

        object Texture(int row)
        {
            int sampler = U8(row + 6), semantic = U8(row + 7);
            int value = zone.Pointer(row + 8);
            bool waterMap = semantic == 11;
            return new
            {
                nameHash = U32(row), nameStart = ((char)U8(row + 4)).ToString(), nameEnd = ((char)U8(row + 5)).ToString(),
                semantic = Label(Semantics, semantic),
                samplerState = new { filter = Label(Filters, sampler & 7),
                    mipMap = Label(MipMaps, (sampler >> 3) & 3),
                    clampU = (sampler & 32) != 0, clampV = (sampler & 64) != 0, clampW = (sampler & 128) != 0 },
                image = Name(waterMap && value != 0 ? zone.Pointer(value + 64) : value, 32),
                water = waterMap ? ReadWater(value) : null
            };
        }

        object Constant(int row)
        {
            var nameBytes = zone.Bytes(row + 4, 12);
            int length = nameBytes.IndexOf((byte)0);
            return new { nameHash = U32(row),
                nameFragment = Encoding.Latin1.GetString(length < 0 ? nameBytes : nameBytes[..length]),
                literal = Vector(row + 16, 4) };
        }

        object[] Table(int field, int count, int stride, Func<int, object> read)
        {
            int table = zone.Pointer(field);
            if (table == 0 && count > 0)
                throw new InvalidDataException($"Missing table at offset 0x{field:X} in IW3 material '{asset.Name}'.");
            return Enumerable.Range(0, count).Select(i => read(table + i * stride)).ToArray();
        }
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            _type = "material", _version = 1, _game = "iw3",
            gameFlags = Enumerable.Range(0, 8).Select(bit => 1 << bit).Where(flag => (U8(body + 4) & flag) != 0),
            sortKey = U8(body + 5), textureAtlas = new { rows = U8(body + 6), columns = U8(body + 7) },
            surfaceTypeBits = U32(body + 16), stateBitsEntry = Enumerable.Range(0, Iw3TechniqueSlots.Definitions.Length).Select(i => (int)(sbyte)U8(body + 24 + i)),
            stateFlags = U8(body + 61), cameraRegion = Label(CameraRegions, U8(body + 62)),
            techniqueSet = Name(zone.Pointer(body + 64), 0),
            textures = Table(body + 68, U8(body + 58), 12, Texture),
            constants = Table(body + 72, U8(body + 59), 32, Constant),
            stateBits = Table(body + 76, U8(body + 60), 8, row => ReadState(U32(row), U32(row + 4)))
        });
    }

    private static readonly string[] Blends = ["disabled", "zero", "one", "srccolor", "invsrccolor",
        "srcalpha", "invsrcalpha", "destalpha", "invdestalpha", "destcolor", "invdestcolor"];
    private static readonly string[] BlendOps = ["disabled", "add", "subtract", "revsubtract", "min", "max"];
    private static readonly string[] AlphaTests = ["", "gt0", "lt128", "ge128"];
    private static readonly string[] CullFaces = ["", "none", "back", "front"];
    private static readonly string[] DepthTests = ["always", "less", "equal", "less_equal"];
    private static readonly string[] PolygonOffsets = ["offset0", "offset1", "offset2", "offsetShadowmap"];
    private static readonly string[] StencilOps = ["keep", "zero", "replace", "incrsat", "decrsat", "invert", "incr", "decr"];
    private static readonly string[] StencilFunctions = ["never", "less", "equal", "lessequal", "greater", "notequal", "greaterequal", "always"];

    private static object ReadState(uint first, uint second)
    {
        string? Value(string[] values, uint word, int shift, uint mask)
        {
            uint index = (word >> shift) & mask;
            return index >= values.Length || values[index].Length == 0 ? null : values[index];
        }
        object? Stencil(int enable, int shift) => (second & (1u << enable)) == 0 ? null :
            new { pass = Value(StencilOps, second, shift, 7), fail = Value(StencilOps, second, shift + 3, 7),
                zfail = Value(StencilOps, second, shift + 6, 7), func = Value(StencilFunctions, second, shift + 9, 7) };
        return new
        {
            srcBlendRgb = Value(Blends, first, 0, 15), dstBlendRgb = Value(Blends, first, 4, 15),
            blendOpRgb = Value(BlendOps, first, 8, 7), alphaTest = (first & 0x800) != 0 ? "disabled" : Value(AlphaTests, first, 12, 3),
            cullFace = Value(CullFaces, first, 14, 3), srcBlendAlpha = Value(Blends, first, 16, 15),
            dstBlendAlpha = Value(Blends, first, 20, 15), blendOpAlpha = Value(BlendOps, first, 24, 7),
            colorWriteRgb = (first & (1u << 27)) != 0, colorWriteAlpha = (first & (1u << 28)) != 0,
            polymodeLine = (first & (1u << 31)) != 0, depthWrite = (second & 1) != 0,
            depthTest = (second & 2) != 0 ? "disabled" : Value(DepthTests, second, 2, 3),
            polygonOffset = Value(PolygonOffsets, second, 4, 3), stencilFront = Stencil(6, 8), stencilBack = Stencil(7, 20)
        };
    }
}
