using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;

namespace IW4.AssetExchange.XModel;

/// <summary>Compiles imported GLB material facts against a compatible native IW4 material template.</summary>
public static class XModelImportedMaterialCompiler
{
    public static string ImportedMaterialName(
        string? modelName,
        XModelExportMaterial source,
        MaterialAsset template)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(template);
        XModelImportMaterial imported = source.ImportMaterial ??
            throw new ArgumentException("The source has no GLB material facts.", nameof(source));
        using SHA256 hash = SHA256.Create();
        var payload = new List<byte>();
        WriteString(payload, modelName);
        WriteString(payload, source.Name);
        WriteString(payload, template.Info.Name);
        foreach (float value in new[]
                 {
                     imported.BaseColorFactor.X,
                     imported.BaseColorFactor.Y,
                     imported.BaseColorFactor.Z,
                     imported.BaseColorFactor.W
                 })
        {
            WriteSingle(payload, value);
        }
        payload.Add((byte)imported.AlphaMode);
        if (imported.BaseColorImage is { } image)
        {
            WriteUInt32(payload, checked((uint)image.Width));
            WriteUInt32(payload, checked((uint)image.Height));
            payload.AddRange(image.RgbaBytes);
        }
        if (imported.NormalImage is { } normalImage)
        {
            WriteUInt32(payload, checked((uint)normalImage.Width));
            WriteUInt32(payload, checked((uint)normalImage.Height));
            payload.AddRange(normalImage.RgbaBytes);
            WriteSingle(payload, imported.NormalScale);
        }
        payload.Add(imported.DoubleSided ? (byte)1 : (byte)0);
        string digest = Convert.ToHexString(hash.ComputeHash(payload.ToArray()))
            .ToLowerInvariant()[..16];
        string model = SafeNamePart(modelName, "xmodel", 40);
        string material = SafeNamePart(source.Name, "material", 40);
        return $"{model}_studio_{material}_{digest}";
    }

    public static bool IsCompatibleImportTemplate(
        XModelExportMaterial source,
        MaterialAsset template,
        out string? blocker)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(template);
        blocker = null;
        if (source.ImportMaterial is not { } imported)
        {
            blocker = "The source has no GLB material facts.";
            return false;
        }
        if (template.Textures.Count(row => row.Semantic == TextureSemantic.ColorMap) != 1)
        {
            blocker = "The IW4 template must have exactly one ColorMap texture row.";
            return false;
        }
        if (template.Textures.Any(row => row.Semantic == TextureSemantic.WaterMap))
        {
            blocker = "Water-material templates cannot be used for imported XModel materials.";
            return false;
        }
        if (imported.NormalImage is not null)
        {
            if (template.Textures.Count(row => row.Semantic == TextureSemantic.NormalMap) != 1)
            {
                blocker = "A GLB normal map requires exactly one IW4 NormalMap texture row.";
                return false;
            }
            if (template.Textures.Any(row => row.Semantic is not (
                    TextureSemantic.ColorMap or TextureSemantic.NormalMap)))
            {
                blocker = "A GLB normal map requires a clean ColorMap + NormalMap IW4 template.";
                return false;
            }
        }
        if (!TryResolveTemplateAlphaMode(
                template,
                out XModelImportAlphaMode templateAlpha,
                out float? templateAlphaCutoff))
        {
            blocker = "The IW4 template has no single proven camera-color alpha behavior.";
            return false;
        }
        if (templateAlpha != imported.AlphaMode)
        {
            blocker = $"GLB alpha mode {imported.AlphaMode.ToString().ToUpperInvariant()} is incompatible with the template's {templateAlpha.ToString().ToUpperInvariant()} state.";
            return false;
        }
        if (imported.AlphaMode == XModelImportAlphaMode.Mask &&
            (templateAlphaCutoff is not float cutoff ||
             MathF.Abs(cutoff - imported.AlphaCutoff) > 0.000001f))
        {
            blocker = $"GLB alpha cutoff {imported.AlphaCutoff:G9} is incompatible with the template's proven alpha-test threshold.";
            return false;
        }
        return true;
    }

    public static bool TryCompile(
        string? modelName,
        XModelExportMaterial source,
        MaterialAsset template,
        out MaterialAsset? material,
        out GfxImageAsset? colorImage,
        out GfxImageAsset? normalImage,
        out string? blocker)
    {
        material = null;
        colorImage = null;
        normalImage = null;
        blocker = null;
        XModelImportMaterial imported = source.ImportMaterial!;
        if (!IsCompatibleImportTemplate(source, template, out blocker))
            return false;
        MaterialTextureDef colorRow = template.Textures.Single(row =>
            row.Semantic == TextureSemantic.ColorMap);
        MaterialTextureDef? normalRow = imported.NormalImage is null
            ? null
            : template.Textures.Single(row =>
                row.Semantic == TextureSemantic.NormalMap);

        try
        {
            string materialName = ImportedMaterialName(modelName, source, template);
            colorImage = CreateColorImage(materialName + "_color", imported);
            normalImage = imported.NormalImage is null
                ? null
                : CreateNormalImage(materialName + "_normal", imported);
            material = CloneMaterialTemplate(
                template,
                materialName,
                colorRow,
                colorImage,
                normalRow,
                normalImage,
                imported.DoubleSided);
            return true;
        }
        catch (Exception exception) when (exception is
            InvalidDataException or OverflowException or ArgumentException)
        {
            blocker = exception.Message;
            material = null;
            colorImage = null;
            normalImage = null;
            return false;
        }
    }

    private static bool TryResolveTemplateAlphaMode(
        MaterialAsset template,
        out XModelImportAlphaMode alphaMode,
        out float? alphaCutoff)
    {
        alphaMode = default;
        alphaCutoff = null;
        if (template.StateBitsEntries.Count != MaterialAsset.TechniqueSlotCount ||
            template.TechniqueSet?.TechniqueSlots.Count != MaterialAsset.TechniqueSlotCount)
        {
            return false;
        }
        var modes = new HashSet<XModelImportAlphaMode>();
        var alphaCutoffs = new HashSet<float>();
        MaterialTechniqueSlot[] populated = template.TechniqueSet.TechniqueSlots
            .Where(slot => slot.Technique is not null)
            .OrderBy(slot => slot.Index)
            .ToArray();
        MaterialTechniqueSlot? selected = populated.FirstOrDefault(slot =>
                slot.Index == (int)MaterialTechniqueType.Lit) ??
            populated.FirstOrDefault(slot =>
                slot.Index == (int)MaterialTechniqueType.Emissive) ??
            populated.FirstOrDefault();
        if (selected?.Technique is not { } technique ||
            (uint)selected.Index >= MaterialAsset.TechniqueSlotCount ||
            technique.Passes.Count == 0 ||
            technique.PassCount != technique.Passes.Count)
        {
            return false;
        }
        int firstState = template.StateBitsEntries[selected.Index].StateBitsIndex;
        for (int pass = 0; pass < technique.Passes.Count; pass++)
        {
            int stateIndex = firstState + pass;
            if ((uint)stateIndex >= (uint)template.StateBits.Count ||
                template.StateBits[stateIndex].LoadBits.Count != 2)
            {
                return false;
            }
            uint word = template.StateBits[stateIndex].LoadBits[0];
            bool blend = (word & GfxStateBitsEncoding.BlendOperationRgbMask) != 0;
            bool alphaTest = (word & (uint)GfxStateBits0Flags.AlphaTestDisabled) == 0;
            if (!blend && alphaTest)
            {
                var test = (GfxAlphaTest)((word & GfxStateBitsEncoding.AlphaTestMask) >>
                    GfxStateBitsEncoding.AlphaTestShift);
                if (test == GfxAlphaTest.GreaterThanZero)
                    alphaCutoffs.Add(0f);
                else if (test == GfxAlphaTest.GreaterThanOrEqualTo128)
                    alphaCutoffs.Add(0.5f);
                else
                    return false;
            }
            modes.Add(blend
                ? XModelImportAlphaMode.Blend
                : alphaTest
                    ? XModelImportAlphaMode.Mask
                    : XModelImportAlphaMode.Opaque);
        }
        if (modes.Count != 1)
            return false;
        alphaMode = modes.Single();
        if (alphaMode == XModelImportAlphaMode.Mask)
        {
            if (alphaCutoffs.Count != 1)
                return false;
            alphaCutoff = alphaCutoffs.Single();
        }
        return true;
    }

    private static MaterialAsset CloneMaterialTemplate(
        MaterialAsset template,
        string name,
        MaterialTextureDef colorRow,
        GfxImageAsset colorImage,
        MaterialTextureDef? normalRow,
        GfxImageAsset? normalImage,
        bool doubleSided) => new()
    {
        Info = new MaterialInfo
        {
            Name = name,
            GameFlags = template.Info.GameFlags,
            SortKey = template.Info.SortKey,
            TextureAtlasRowCount = template.Info.TextureAtlasRowCount,
            TextureAtlasColumnCount = template.Info.TextureAtlasColumnCount,
            DrawSurf = template.Info.DrawSurf,
            SurfaceTypeBits = template.Info.SurfaceTypeBits,
            HashIndex = template.Info.HashIndex,
            Pad16 = template.Info.Pad16
        },
        StateBitsEntries = template.StateBitsEntries.ToArray(),
        TextureCount = template.TextureCount,
        ConstantCount = template.ConstantCount,
        StateBitsCount = template.StateBitsCount,
        StateFlags = doubleSided
            ? template.StateFlags & ~(
                MaterialStateFlags.CullBack |
                MaterialStateFlags.CullFront |
                MaterialStateFlags.CullBackShadow |
                MaterialStateFlags.CullFrontShadow)
            : template.StateFlags,
        CameraRegion = template.CameraRegion,
        XStringCount = template.XStringCount,
        Pad43 = template.Pad43,
        InlineTechniqueSlotStateBits = template.InlineTechniqueSlotStateBits.ToArray(),
        Pad8E = template.Pad8E,
        RuntimeTechniqueSlotStateBits = template.RuntimeTechniqueSlotStateBits.ToArray(),
        TechniqueSet = template.TechniqueSet,
        Textures = template.Textures.Select(row => new MaterialTextureDef
        {
            NameHash = row.NameHash,
            NameStart = row.NameStart,
            NameEnd = row.NameEnd,
            SamplerState = row.SamplerState,
            Semantic = row.Semantic,
            Image = ReferenceEquals(row, colorRow)
                ? colorImage
                : ReferenceEquals(row, normalRow)
                    ? normalImage
                    : row.Image,
            Water = row.Water
        }).ToArray(),
        Constants = template.Constants.Select(row => new MaterialConstantDef
        {
            NameHash = row.NameHash,
            NameBytes = row.NameBytes.ToArray(),
            Literal = row.Literal
        }).ToArray(),
        StateBits = template.StateBits.Select(row =>
            CloneStateBits(row, doubleSided)).ToArray(),
        XStrings = template.XStrings.Select(row => new MaterialXStringEntry(
            row.Index,
            default,
            row.Value)).ToArray()
    };

    private static GfxStateBits CloneStateBits(
        GfxStateBits source,
        bool doubleSided)
    {
        uint[] loadBits = source.LoadBits.ToArray();
        if (doubleSided && loadBits.Length > 0)
        {
            loadBits[0] =
                (loadBits[0] & ~GfxStateBitsEncoding.CullFaceMask) |
                ((uint)GfxCullFace.None << GfxStateBitsEncoding.CullFaceShift);
        }
        return new GfxStateBits
        {
            LoadBits = loadBits,
            CommandWordCount = source.CommandWordCount
        };
    }

    private static GfxImageAsset CreateColorImage(
        string name,
        XModelImportMaterial material)
    {
        XModelImportImage? source = material.BaseColorImage;
        int width = source?.Width ?? 4;
        int height = source?.Height ?? 4;
        if (width is <= 0 or > ushort.MaxValue || height is <= 0 or > ushort.MaxValue)
            throw new InvalidDataException("Imported base-color image dimensions exceed IW4 limits.");
        int pixelBytes = checked(width * height * 4);
        if (source is not null && source.RgbaBytes.Count != pixelBytes)
            throw new InvalidDataException("Imported base-color image pixels do not match its dimensions.");
        int payloadBytes = checked((pixelBytes + 0x7f) & ~0x7f);
        var payload = new byte[payloadBytes];
        for (int pixel = 0; pixel < width * height; pixel++)
        {
            int sourceOffset = pixel * 4;
            float red = source is null ? 1f : SrgbToLinear(source.RgbaBytes[sourceOffset] / 255f);
            float green = source is null ? 1f : SrgbToLinear(source.RgbaBytes[sourceOffset + 1] / 255f);
            float blue = source is null ? 1f : SrgbToLinear(source.RgbaBytes[sourceOffset + 2] / 255f);
            float alpha = material.AlphaMode == XModelImportAlphaMode.Opaque
                ? 1f
                : (source is null ? 1f : source.RgbaBytes[sourceOffset + 3] / 255f) *
                  material.BaseColorFactor.W;
            int destination = sourceOffset;
            payload[destination] = ToByte(alpha);
            payload[destination + 1] = ToByte(LinearToSrgb(red * material.BaseColorFactor.X));
            payload[destination + 2] = ToByte(LinearToSrgb(green * material.BaseColorFactor.Y));
            payload[destination + 3] = ToByte(LinearToSrgb(blue * material.BaseColorFactor.Z));
        }
        return new GfxImageAsset
        {
            Format = (byte)((byte)GfxImageBaseFormat.A8R8G8B8 | (byte)GfxImageFormatFlags.Linear),
            LevelCount = 1,
            DimensionCount = GfxImageDimension.TwoDimensional,
            TextureControl1 = 0x0001aae4,
            Width = checked((ushort)width),
            Height = checked((ushort)height),
            Depth = 1,
            MemoryLocation = GfxImageMemoryLocation.Local,
            MapType = MapType.TwoDimensional,
            TextureSemantic = TextureSemantic.ColorMap,
            Category = ImageCategory.LoadFromFile,
            UseSrgbReads = 1,
            CardMemory = checked((uint)payload.Length),
            BaseWidth = checked((ushort)width),
            BaseHeight = checked((ushort)height),
            BaseDepth = 1,
            BaseLevelCount = 1,
            Cached = GfxImageCached.Auto,
            PayloadByteCount = payload.Length,
            PayloadBytes = payload,
            Name = name
        };
    }

    private static GfxImageAsset CreateNormalImage(
        string name,
        XModelImportMaterial material)
    {
        XModelImportImage source = material.NormalImage ??
            throw new ArgumentException("The imported material has no normal image.", nameof(material));
        int width = source.Width;
        int height = source.Height;
        if (width is <= 0 or > ushort.MaxValue || height is <= 0 or > ushort.MaxValue)
            throw new InvalidDataException("Imported normal image dimensions exceed IW4 limits.");
        int pixelBytes = checked(width * height * 4);
        if (source.RgbaBytes.Count != pixelBytes)
            throw new InvalidDataException("Imported normal image pixels do not match its dimensions.");
        int payloadBytes = checked((pixelBytes + 0x7f) & ~0x7f);
        var payload = new byte[payloadBytes];
        for (int pixel = 0; pixel < width * height; pixel++)
        {
            int offset = pixel * 4;
            var normal = new Vector3(
                (source.RgbaBytes[offset] / 255f * 2f - 1f) * material.NormalScale,
                (source.RgbaBytes[offset + 1] / 255f * 2f - 1f) * material.NormalScale,
                source.RgbaBytes[offset + 2] / 255f * 2f - 1f);
            normal = normal.LengthSquared() > 0.00000001f &&
                float.IsFinite(normal.LengthSquared())
                    ? Vector3.Normalize(normal)
                    : Vector3.UnitZ;
            byte x = ToByte(normal.X * 0.5f + 0.5f);
            payload[offset] = x;
            payload[offset + 1] = x;
            payload[offset + 2] = ToByte(normal.Y * 0.5f + 0.5f);
            payload[offset + 3] = ToByte(normal.Z * 0.5f + 0.5f);
        }
        return new GfxImageAsset
        {
            Format = (byte)((byte)GfxImageBaseFormat.A8R8G8B8 | (byte)GfxImageFormatFlags.Linear),
            LevelCount = 1,
            DimensionCount = GfxImageDimension.TwoDimensional,
            TextureControl1 = 0x0001aae4,
            Width = checked((ushort)width),
            Height = checked((ushort)height),
            Depth = 1,
            MemoryLocation = GfxImageMemoryLocation.Local,
            MapType = MapType.TwoDimensional,
            TextureSemantic = TextureSemantic.NormalMap,
            Category = ImageCategory.LoadFromFile,
            UseSrgbReads = 0,
            CardMemory = checked((uint)payload.Length),
            BaseWidth = checked((ushort)width),
            BaseHeight = checked((ushort)height),
            BaseDepth = 1,
            BaseLevelCount = 1,
            Cached = GfxImageCached.Auto,
            PayloadByteCount = payload.Length,
            PayloadBytes = payload,
            Name = name
        };
    }

    private static float SrgbToLinear(float value) => value <= 0.04045f
        ? value / 12.92f
        : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);

    private static float LinearToSrgb(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        return value <= 0.0031308f
            ? value * 12.92f
            : 1.055f * MathF.Pow(value, 1f / 2.4f) - 0.055f;
    }

    private static byte ToByte(float value) =>
        (byte)MathF.Round(Math.Clamp(value, 0f, 1f) * byte.MaxValue);

    private static string SafeNamePart(string? value, string fallback, int maximumLength)
    {
        string result = new((value ?? string.Empty)
            .Select(character => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-'
                ? char.ToLowerInvariant(character)
                : '_')
            .ToArray());
        result = result.Trim('_');
        if (result.Length == 0)
            result = fallback;
        return result.Length <= maximumLength ? result : result[..maximumLength];
    }

    private static void WriteString(List<byte> values, string? value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        WriteUInt32(values, checked((uint)bytes.Length));
        values.AddRange(bytes);
    }

    private static void WriteSingle(List<byte> values, float value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, BitConverter.SingleToInt32Bits(value));
        values.AddRange(bytes.ToArray());
    }

    private static void WriteUInt32(List<byte> values, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        values.AddRange(bytes.ToArray());
    }
}
