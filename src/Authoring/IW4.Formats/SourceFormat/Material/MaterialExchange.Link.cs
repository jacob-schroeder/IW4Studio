using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using IW4.Game.Assets.Image;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.TechniqueSet;

namespace IW4.Formats.SourceFormat.Material;

public sealed partial class MaterialExchange
{
    /// <summary>Reads a version-two PS3 material and resolves its asset dependencies.</summary>
    public MaterialAsset Link(
        string sourceDirectory,
        string assetName,
        Func<string, MaterialTechniqueSetAsset> loadTechniqueSet,
        Func<string, GfxImageAsset> loadImage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentNullException.ThrowIfNull(loadTechniqueSet);
        ArgumentNullException.ThrowIfNull(loadImage);
        string name = SourceOutput.NormalizeOwnedAssetName(assetName, "Material");
        string path = Path.Combine(Path.GetFullPath(sourceDirectory), GetSourcePath(name));
        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = Object(document.RootElement, "Material");
        if (String(root, "_game", "Material") != "iw4" ||
            String(root, "_platform", "Material") != "ps3" ||
            String(root, "_type", "Material") != "material")
            throw new InvalidDataException($"Material '{name}' requires IW4 PS3 material JSON.");
        if (Int(root, "_version", "Material") != 2)
            throw new InvalidDataException($"Material '{name}' requires version 2; re-export the source material.");

        JsonElement native = Object(Property(root, "_native", "Material"), "Material._native");
        JsonElement states = JsonArray(root, "stateBits", "Material");
        JsonElement residual = JsonArray(native, "stateBitsResidual", "Material._native");
        if (states.GetArrayLength() != residual.GetArrayLength())
            throw new InvalidDataException("Material state-bit rows and native residual rows differ.");
        GfxStateBits[] stateBits = new GfxStateBits[states.GetArrayLength()];
        for (int index = 0; index < stateBits.Length; index++)
            stateBits[index] = ReadStateBits(states[index], residual[index], index);

        JsonElement entries = JsonArray(root, "stateBitsEntry", "Material");
        if (entries.GetArrayLength() != MaterialAsset.TechniqueSlotCount)
            throw new InvalidDataException($"Material requires {MaterialAsset.TechniqueSlotCount} state-bit entries.");
        MaterialStateBitsEntry[] stateEntries = entries.EnumerateArray()
            .Select((value, index) => new MaterialStateBitsEntry(
                unchecked((byte)SignedByte(value, $"Material.stateBitsEntry[{index}]"))))
            .ToArray();

        JsonElement nativeStrings = JsonArray(native, "xstrings", "Material._native");
        MaterialXStringEntry[] xstrings = nativeStrings.EnumerateArray()
            .Select((value, index) => new MaterialXStringEntry(index, default,
                value.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.String => value.GetString(),
                    _ => throw new InvalidDataException($"Material._native.xstrings[{index}] must be a string or null.")
                }))
            .ToArray();
        MaterialConstantDef[] constants = JsonArray(root, "constants", "Material")
            .EnumerateArray().Select((value, index) => ReadConstant(value, index)).ToArray();
        MaterialTextureDef[] textures = JsonArray(root, "textures", "Material")
            .EnumerateArray().Select((value, index) => ReadTexture(value, index, loadImage)).ToArray();

        JsonElement atlas = Object(Property(root, "textureAtlas", "Material"), "Material.textureAtlas");
        byte gameFlags = 0;
        foreach (JsonElement flag in JsonArray(root, "gameFlags", "Material").EnumerateArray())
        {
            string hex = JsonString(flag, "Material.gameFlags[]");
            if (!byte.TryParse(hex, System.Globalization.NumberStyles.AllowHexSpecifier,
                    System.Globalization.CultureInfo.InvariantCulture, out byte bit) ||
                bit == 0 || (bit & (bit - 1)) != 0 || (gameFlags & bit) != 0)
                throw new InvalidDataException($"Material has an invalid game flag '{hex}'.");
            gameFlags |= bit;
        }

        string techniqueSetName = SourceOutput.NormalizeReferencedAssetName(
            String(root, "techniqueSet", "Material"), "Material.techniqueSet");
        var asset = new MaterialAsset
        {
            Info = new MaterialInfo
            {
                Name = name,
                GameFlags = (MaterialGameFlags)gameFlags,
                SortKey = (MaterialSortKey)Byte(root, "sortKey", "Material"),
                TextureAtlasColumnCount = Byte(atlas, "columns", "Material.textureAtlas"),
                TextureAtlasRowCount = Byte(atlas, "rows", "Material.textureAtlas"),
                SurfaceTypeBits = (MaterialSurfaceTypeBits)UInt(root, "surfaceTypeBits", "Material"),
                HashIndex = UShort(native, "hashIndex", "Material._native"),
                Pad16 = UShort(native, "pad16", "Material._native")
            },
            StateBitsEntries = stateEntries,
            TextureCount = CheckedCount(textures.Length, "textures"),
            Textures = textures,
            ConstantCount = CheckedCount(constants.Length, "constants"),
            Constants = constants,
            StateBitsCount = CheckedCount(stateBits.Length, "stateBits"),
            StateBits = stateBits,
            StateFlags = (MaterialStateFlags)Byte(root, "stateFlags", "Material"),
            CameraRegion = CameraRegion(String(root, "cameraRegion", "Material")),
            XStringCount = CheckedCount(xstrings.Length, "xstrings"),
            XStrings = xstrings,
            Pad43 = Byte(native, "pad43", "Material._native"),
            Pad8E = UShort(native, "pad8E", "Material._native"),
            RuntimeTechniqueSlotStateBits = Bool(native, "runtimeTechniqueStatePresent", "Material._native")
                ? new ushort[MaterialAsset.TechniqueSlotCount] : [],
            TechniqueSet = loadTechniqueSet(techniqueSetName) ??
                throw new InvalidDataException($"Material '{name}' technique set '{techniqueSetName}' did not resolve.")
        };
        Validate(asset, name);
        return asset;
    }

    private static MaterialConstantDef ReadConstant(JsonElement value, int index)
    {
        string path = $"Material.constants[{index}]";
        value = Object(value, path);
        string name;
        uint hash;
        if (value.TryGetProperty("name", out JsonElement fullName))
        {
            name = JsonString(fullName, $"{path}.name");
            hash = HashString(name);
        }
        else
        {
            name = String(value, "nameFragment", path);
            hash = UInt(value, "nameHash", path);
        }
        if (name.Any(character => character > 0x7f) ||
            (!value.TryGetProperty("name", out _) && name.Length > 12))
            throw new InvalidDataException($"{path} name cannot fit the native 12-byte field.");
        byte[] fragment = Encoding.ASCII.GetBytes(name);
        byte[] nameBytes = new byte[12];
        fragment.AsSpan(0, Math.Min(fragment.Length, 12)).CopyTo(nameBytes);
        JsonElement literal = JsonArray(value, "literal", path);
        MaterialVec4 vector = Vec4(literal, $"{path}.literal");
        return new MaterialConstantDef { NameHash = hash, NameBytes = nameBytes, Literal = vector };
    }

    private static MaterialTextureDef ReadTexture(
        JsonElement value,
        int index,
        Func<string, GfxImageAsset> loadImage)
    {
        string path = $"Material.textures[{index}]";
        value = Object(value, path);
        uint hash;
        byte start;
        byte end;
        if (value.TryGetProperty("name", out JsonElement nameElement))
        {
            string name = JsonString(nameElement, $"{path}.name");
            if (name.Length == 0 || name[0] > 0x7f || name[^1] > 0x7f)
                throw new InvalidDataException($"{path}.name has invalid boundary bytes.");
            hash = HashString(name);
            start = (byte)name[0];
            end = (byte)name[^1];
        }
        else
        {
            hash = UInt(value, "nameHash", path);
            start = NameBoundary(String(value, "nameStart", path), $"{path}.nameStart");
            end = NameBoundary(String(value, "nameEnd", path), $"{path}.nameEnd");
        }
        JsonElement sampler = Object(Property(value, "samplerState", path), $"{path}.samplerState");
        MaterialSamplerState samplerState = String(sampler, "filter", path) switch
        {
            "disabled" => MaterialSamplerState.FilterDisabled,
            "nearest" => MaterialSamplerState.FilterNearest,
            "linear" => MaterialSamplerState.FilterLinear,
            "aniso2x" => MaterialSamplerState.FilterAnisotropic2X,
            "aniso4x" => MaterialSamplerState.FilterAnisotropic4X,
            _ => throw new InvalidDataException($"{path}.samplerState.filter is unsupported.")
        };
        samplerState |= String(sampler, "mipMap", path) switch
        {
            "disabled" => MaterialSamplerState.MipMapDisabled,
            "nearest" => MaterialSamplerState.MipMapNearest,
            "linear" => MaterialSamplerState.MipMapLinear,
            _ => throw new InvalidDataException($"{path}.samplerState.mipMap is unsupported.")
        };
        if (Bool(sampler, "clampU", path)) samplerState |= MaterialSamplerState.ClampU;
        if (Bool(sampler, "clampV", path)) samplerState |= MaterialSamplerState.ClampV;
        if (Bool(sampler, "clampW", path)) samplerState |= MaterialSamplerState.ClampW;
        TextureSemantic semantic = String(value, "semantic", path) switch
        {
            "2D" => TextureSemantic.TwoDimensional,
            "function" => TextureSemantic.Function,
            "colorMap" => TextureSemantic.ColorMap,
            "detailMap" => TextureSemantic.DetailMap,
            "unused2" => TextureSemantic.Unused2,
            "normalMap" => TextureSemantic.NormalMap,
            "unused3" => TextureSemantic.Unused3,
            "unused4" => TextureSemantic.Unused4,
            "specularMap" => TextureSemantic.SpecularMap,
            "unused5" => TextureSemantic.Unused5,
            "unused6" => TextureSemantic.Unused6,
            "waterMap" => TextureSemantic.WaterMap,
            _ => throw new InvalidDataException($"{path}.semantic is unsupported.")
        };
        string imageName = SourceOutput.NormalizeReferencedAssetName(String(value, "image", path), $"{path}.image");
        GfxImageAsset image = loadImage(imageName) ??
            throw new InvalidDataException($"{path}.image '{imageName}' did not resolve.");
        return new MaterialTextureDef
        {
            NameHash = hash,
            NameStart = start,
            NameEnd = end,
            SamplerState = samplerState,
            Semantic = semantic,
            Image = semantic == TextureSemantic.WaterMap ? null : image,
            Water = semantic == TextureSemantic.WaterMap
                ? ReadWater(Object(Property(value, "water", path), $"{path}.water"), image, $"{path}.water")
                : null
        };
    }

    private static MaterialWater ReadWater(JsonElement value, GfxImageAsset image, string path)
    {
        int m = Int(value, "m", path);
        int n = Int(value, "n", path);
        if (m < 0 || n < 0)
            throw new InvalidDataException($"{path} has negative dimensions.");
        int count = checked(m * n);
        byte[] h0 = Base64(value, "h0", path);
        byte[] wTerm = Base64(value, "wTerm", path);
        if (h0.Length != checked(count * 8) || wTerm.Length != checked(count * 4))
            throw new InvalidDataException($"{path} spectrum lengths do not match its dimensions.");
        float[] h0x = new float[count];
        float[] h0y = new float[count];
        float[] terms = new float[count];
        for (int index = 0; index < count; index++)
        {
            h0x[index] = ReadFloat(h0.AsSpan(index * 8, 4));
            h0y[index] = ReadFloat(h0.AsSpan(index * 8 + 4, 4));
            terms[index] = ReadFloat(wTerm.AsSpan(index * 4, 4));
        }
        JsonElement direction = JsonArray(value, "winddir", path);
        if (direction.GetArrayLength() != 2)
            throw new InvalidDataException($"{path}.winddir requires two values.");
        return new MaterialWater
        {
            Writable = new MaterialWaterWritable(unchecked((uint)BitConverter.SingleToInt32Bits(Float(value, "floatTime", path)))),
            M = m, N = n,
            Lx = Float(value, "lx", path), Lz = Float(value, "lz", path),
            Gravity = Float(value, "gravity", path),
            WindVelocity = Float(value, "windvel", path),
            WindDirection = new MaterialVec2(JsonFloat(direction[0], path), JsonFloat(direction[1], path)),
            Amplitude = Float(value, "amplitude", path),
            CodeConstant = Vec4(JsonArray(value, "codeConstant", path), $"{path}.codeConstant"),
            H0X = h0x, H0Y = h0y, WTerm = terms, Image = image
        };
    }

    private static GfxStateBits ReadStateBits(JsonElement value, JsonElement residual, int index)
    {
        string path = $"Material.stateBits[{index}]";
        value = Object(value, path);
        if (residual.ValueKind != JsonValueKind.Array || residual.GetArrayLength() != 2)
            throw new InvalidDataException($"Material._native.stateBitsResidual[{index}] requires two words.");
        uint residual0 = JsonUInt(residual[0], $"{path}.residual0");
        uint residual1 = JsonUInt(residual[1], $"{path}.residual1");
        uint word0 = 0;
        uint word1 = 0;
        word0 |= EnumField(value, "srcBlendRgb", path, BlendNames) << GfxStateBitsEncoding.SourceBlendRgbShift;
        word0 |= EnumField(value, "dstBlendRgb", path, BlendNames) << GfxStateBitsEncoding.DestinationBlendRgbShift;
        word0 |= EnumField(value, "blendOpRgb", path, BlendOperationNames) << GfxStateBitsEncoding.BlendOperationRgbShift;
        string alpha = String(value, "alphaTest", path);
        if (alpha == "disabled")
            word0 |= (uint)GfxStateBits0Flags.AlphaTestDisabled;
        else
            word0 |= NamedValue(alpha, AlphaNames, $"{path}.alphaTest") << GfxStateBitsEncoding.AlphaTestShift;
        word0 |= EnumField(value, "cullFace", path, CullNames) << GfxStateBitsEncoding.CullFaceShift;
        word0 |= EnumField(value, "srcBlendAlpha", path, BlendNames) << GfxStateBitsEncoding.SourceBlendAlphaShift;
        word0 |= EnumField(value, "dstBlendAlpha", path, BlendNames) << GfxStateBitsEncoding.DestinationBlendAlphaShift;
        word0 |= EnumField(value, "blendOpAlpha", path, BlendOperationNames) << GfxStateBitsEncoding.BlendOperationAlphaShift;
        if (Bool(value, "colorWriteRgb", path)) word0 |= (uint)GfxStateBits0Flags.ColorWriteRgb;
        if (Bool(value, "colorWriteAlpha", path)) word0 |= (uint)GfxStateBits0Flags.ColorWriteAlpha;
        if (Bool(value, "gammaWrite", path)) word0 |= (uint)GfxStateBits0Flags.GammaWrite;
        if (Bool(value, "polymodeLine", path)) word0 |= (uint)GfxStateBits0Flags.PolygonModeLine;

        if (Bool(value, "depthWrite", path)) word1 |= (uint)GfxStateBits1Flags.DepthWrite;
        string depth = String(value, "depthTest", path);
        if (depth == "disabled") word1 |= (uint)GfxStateBits1Flags.DepthTestDisabled;
        else word1 |= NamedValue(depth, DepthNames, $"{path}.depthTest") << GfxStateBitsEncoding.DepthTestShift;
        word1 |= EnumField(value, "polygonOffset", path, PolygonNames) << GfxStateBitsEncoding.PolygonOffsetShift;
        if (value.TryGetProperty("stencilFront", out JsonElement front))
        {
            word1 |= (uint)GfxStateBits1Flags.StencilEnabled;
            word1 |= ReadStencil(front, $"{path}.stencilFront", back: false);
        }
        if (value.TryGetProperty("stencilBack", out JsonElement back))
        {
            word1 |= (uint)GfxStateBits1Flags.StencilBackFaceIndependent;
            word1 |= ReadStencil(back, $"{path}.stencilBack", back: true);
        }
        uint allowed0 = alpha == "disabled" ? GfxStateBitsEncoding.AlphaTestMask : 0;
        uint allowed1 = depth == "disabled" ? GfxStateBitsEncoding.DepthTestMask : 0;
        if (!value.TryGetProperty("stencilFront", out _)) allowed1 |= 0x000fff00;
        if (!value.TryGetProperty("stencilBack", out _)) allowed1 |= 0xfff00000;
        const uint potentialResidual1 = GfxStateBitsEncoding.DepthTestMask | 0xffffff00;
        if ((residual0 & ~GfxStateBitsEncoding.AlphaTestMask) != 0 ||
            (residual1 & ~potentialResidual1) != 0)
            throw new InvalidDataException($"{path} has bits outside native residual fields.");
        return new GfxStateBits { LoadBits = [word0 | (residual0 & allowed0), word1 | (residual1 & allowed1)] };
    }

    private static uint ReadStencil(JsonElement value, string path, bool back)
    {
        value = Object(value, path);
        int pass = back ? GfxStateBitsEncoding.StencilBackPassShift : GfxStateBitsEncoding.StencilFrontPassShift;
        int fail = back ? GfxStateBitsEncoding.StencilBackFailShift : GfxStateBitsEncoding.StencilFrontFailShift;
        int zfail = back ? GfxStateBitsEncoding.StencilBackDepthFailShift : GfxStateBitsEncoding.StencilFrontDepthFailShift;
        int func = back ? GfxStateBitsEncoding.StencilBackFunctionShift : GfxStateBitsEncoding.StencilFrontFunctionShift;
        return (EnumField(value, "pass", path, StencilOperationNames) << pass) |
               (EnumField(value, "fail", path, StencilOperationNames) << fail) |
               (EnumField(value, "zfail", path, StencilOperationNames) << zfail) |
               (EnumField(value, "func", path, StencilFunctionNames) << func);
    }

    private static readonly string[] BlendNames =
        ["disabled", "zero", "one", "srccolor", "invsrccolor", "srcalpha", "invsrcalpha", "destalpha", "invdestalpha", "destcolor", "invdestcolor"];
    private static readonly string[] BlendOperationNames =
        ["disabled", "add", "subtract", "revsubtract", "min", "max"];
    private static readonly string[] AlphaNames = ["", "gt0", "lt128", "ge128"];
    private static readonly string[] CullNames = ["", "none", "back", "front"];
    private static readonly string[] DepthNames = ["always", "less", "equal", "less_equal"];
    private static readonly string[] PolygonNames = ["offset0", "offset1", "offset2", "inherit"];
    private static readonly string[] StencilOperationNames =
        ["keep", "zero", "replace", "incrsat", "decrsat", "invert", "incr", "decr"];
    private static readonly string[] StencilFunctionNames =
        ["never", "less", "equal", "lessequal", "greater", "notequal", "greaterequal", "always"];

    private static uint EnumField(JsonElement value, string name, string path, string[] names) =>
        NamedValue(String(value, name, path), names, $"{path}.{name}");

    private static uint NamedValue(string value, string[] names, string path)
    {
        int index = System.Array.IndexOf(names, value);
        if (index < 0 || value.Length == 0)
            throw new InvalidDataException($"{path} has unsupported value '{value}'.");
        return checked((uint)index);
    }

    private static GfxCameraRegionType CameraRegion(string value) => value switch
    {
        "litOpaque" => GfxCameraRegionType.LitOpaque,
        "litTrans" => GfxCameraRegionType.LitTrans,
        "emissive" => GfxCameraRegionType.Emissive,
        "depthHack" => GfxCameraRegionType.DepthHack,
        "none" => GfxCameraRegionType.None,
        _ => throw new InvalidDataException($"Material.cameraRegion has unsupported value '{value}'.")
    };

    private static MaterialVec4 Vec4(JsonElement value, string path)
    {
        if (value.GetArrayLength() != 4)
            throw new InvalidDataException($"{path} requires four values.");
        return new MaterialVec4(JsonFloat(value[0], path), JsonFloat(value[1], path),
            JsonFloat(value[2], path), JsonFloat(value[3], path));
    }

    private static float ReadFloat(ReadOnlySpan<byte> bytes) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes));

    private static byte[] Base64(JsonElement parent, string name, string path)
    {
        try { return Convert.FromBase64String(String(parent, name, path)); }
        catch (FormatException exception)
        { throw new InvalidDataException($"{path}.{name} is not base64 data.", exception); }
    }

    private static byte NameBoundary(string value, string path) =>
        value.Length == 1 && value[0] <= 0x7f
            ? (byte)value[0]
            : throw new InvalidDataException($"{path} requires one ASCII character.");

    private static byte CheckedCount(int count, string path) =>
        count <= byte.MaxValue ? (byte)count :
            throw new InvalidDataException($"Material.{path} exceeds the native byte count.");

    private static JsonElement Object(JsonElement value, string path) =>
        value.ValueKind == JsonValueKind.Object ? value :
            throw new InvalidDataException($"{path} must be an object.");

    private static JsonElement Property(JsonElement parent, string name, string path) =>
        parent.TryGetProperty(name, out JsonElement value) ? value :
            throw new InvalidDataException($"{path}.{name} is missing.");

    private static JsonElement JsonArray(JsonElement parent, string name, string path)
    {
        JsonElement value = Property(parent, name, path);
        return value.ValueKind == JsonValueKind.Array ? value :
            throw new InvalidDataException($"{path}.{name} must be an array.");
    }

    private static string JsonString(JsonElement value, string path) =>
        value.ValueKind == JsonValueKind.String && value.GetString() is { } text ? text :
            throw new InvalidDataException($"{path} must be a string.");

    private static string String(JsonElement parent, string name, string path) =>
        JsonString(Property(parent, name, path), $"{path}.{name}");

    private static int Int(JsonElement parent, string name, string path)
    {
        JsonElement value = Property(parent, name, path);
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result) ? result :
            throw new InvalidDataException($"{path}.{name} must be a 32-bit integer.");
    }

    private static uint JsonUInt(JsonElement value, string path) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out uint result) ? result :
            throw new InvalidDataException($"{path} must be an unsigned 32-bit integer.");

    private static uint UInt(JsonElement parent, string name, string path) =>
        JsonUInt(Property(parent, name, path), $"{path}.{name}");

    private static byte Byte(JsonElement parent, string name, string path)
    {
        uint value = UInt(parent, name, path);
        return value <= byte.MaxValue ? (byte)value :
            throw new InvalidDataException($"{path}.{name} must fit a byte.");
    }

    private static ushort UShort(JsonElement parent, string name, string path)
    {
        uint value = UInt(parent, name, path);
        return value <= ushort.MaxValue ? (ushort)value :
            throw new InvalidDataException($"{path}.{name} must fit a ushort.");
    }

    private static sbyte SignedByte(JsonElement value, string path) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result) &&
        result >= sbyte.MinValue && result <= sbyte.MaxValue ? (sbyte)result :
            throw new InvalidDataException($"{path} must fit a signed byte.");

    private static bool Bool(JsonElement parent, string name, string path)
    {
        JsonElement value = Property(parent, name, path);
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidDataException($"{path}.{name} must be a boolean.")
        };
    }

    private static float Float(JsonElement parent, string name, string path) =>
        JsonFloat(Property(parent, name, path), $"{path}.{name}");

    private static float JsonFloat(JsonElement value, string path) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out float result) && float.IsFinite(result)
            ? result
            : throw new InvalidDataException($"{path} must be a finite float.");
}
