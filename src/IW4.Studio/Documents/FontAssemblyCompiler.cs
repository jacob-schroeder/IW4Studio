using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using IW4.Assets.Assets;
using IW4.Assets.Assets.Font;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;

namespace IW4.Studio.Documents;

public sealed record FontRasterization(
    int PixelHeight,
    int AtlasWidth,
    int AtlasHeight,
    IReadOnlyList<byte> RgbaBytes,
    IReadOnlyList<FontRasterizedGlyph> Glyphs);

public sealed record FontRasterizedGlyph(
    ushort Letter,
    sbyte X0,
    sbyte Y0,
    byte Dx,
    byte PixelWidth,
    byte PixelHeight,
    int AtlasX,
    int AtlasY);

public sealed record FontAssemblyCompileResult(
    FontAsset Definition,
    IReadOnlyList<BaseAsset> Providers,
    IReadOnlyList<AssetValidationIssue> Issues)
{
    public bool IsSuccess => Issues.All(
        issue => issue.Severity != AssetValidationSeverity.Error);
}

/// <summary>
/// Converts renderer-neutral glyph pixels and metrics into one detached IW4
/// Font definition plus its owned Material/Image provider closure.
/// </summary>
public static class FontAssemblyCompiler
{
    private const int MaximumAtlasSize = 4096;

    public static FontAssemblyCompileResult Compile(
        FontAsset template,
        FontRasterization rasterization)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(rasterization);
        var issues = new List<AssetValidationIssue>();
        ValidateRasterization(template, rasterization, issues);

        MaterialTextureDef? primaryAtlasRow = SelectAtlasRow(
            template.Material,
            "font.material",
            issues);
        MaterialTextureDef? glowAtlasRow = template.GlowMaterial is null
            ? null
            : SelectAtlasRow(template.GlowMaterial, "font.glowMaterial", issues);
        if (issues.Any(issue => issue.Severity == AssetValidationSeverity.Error))
        {
            return new FontAssemblyCompileResult(
                CopyFont(template),
                [],
                Array.AsReadOnly(issues.ToArray()));
        }

        string digest = ContentDigest(template, rasterization);
        string fontName = SafeNamePart(
            template.Name?.Replace('\\', '/').Split('/').LastOrDefault(),
            "font",
            48);
        string imageName = $"{fontName}_studio_atlas_{digest}";
        string materialName = $"fonts/{fontName}_studio_{digest}";
        string glowMaterialName = $"{materialName}_glow";

        GfxImageAsset image = CreateAtlasImage(
            imageName,
            rasterization,
            primaryAtlasRow!.Image!);
        MaterialAsset material = CloneMaterialTemplate(
            template.Material!,
            materialName,
            primaryAtlasRow,
            image);
        MaterialAsset? glowMaterial = template.GlowMaterial is null
            ? null
            : CloneMaterialTemplate(
                template.GlowMaterial,
                glowMaterialName,
                glowAtlasRow!,
                image);
        FontGlyph[] glyphs = rasterization.Glyphs.Select(glyph => new FontGlyph(
            glyph.Letter,
            glyph.X0,
            glyph.Y0,
            glyph.Dx,
            glyph.PixelWidth,
            glyph.PixelHeight,
            0,
            glyph.AtlasX / (float)rasterization.AtlasWidth,
            glyph.AtlasY / (float)rasterization.AtlasHeight,
            (glyph.AtlasX + glyph.PixelWidth) / (float)rasterization.AtlasWidth,
            (glyph.AtlasY + glyph.PixelHeight) / (float)rasterization.AtlasHeight))
            .ToArray();
        var definition = new FontAsset
        {
            Offset = template.Offset,
            RuntimeAddress = template.RuntimeAddress,
            Name = template.Name,
            PixelHeight = template.PixelHeight,
            GlyphCount = glyphs.Length,
            Material = material,
            GlowMaterial = glowMaterial,
            Glyphs = glyphs
        };
        BaseAsset[] providers = glowMaterial is null
            ? [image, material]
            : [image, material, glowMaterial];
        return new FontAssemblyCompileResult(
            definition,
            Array.AsReadOnly(providers),
            Array.AsReadOnly(issues.ToArray()));
    }

    private static void ValidateRasterization(
        FontAsset template,
        FontRasterization rasterization,
        ICollection<AssetValidationIssue> issues)
    {
        foreach (AssetValidationIssue issue in FontAuthoringValidator.Validate(template))
            issues.Add(issue);
        if (rasterization.PixelHeight != template.PixelHeight)
        {
            issues.Add(Error(
                "font.pixelHeight",
                $"The rasterized pixel height must preserve the template value {template.PixelHeight}."));
        }
        if (rasterization.AtlasWidth is <= 0 or > MaximumAtlasSize ||
            rasterization.AtlasHeight is <= 0 or > MaximumAtlasSize)
        {
            issues.Add(Error(
                "font.atlas",
                $"The IW4 font atlas must be between 1 and {MaximumAtlasSize} pixels on each axis."));
        }
        else if (!IsPowerOfTwo(rasterization.AtlasWidth) ||
                 !IsPowerOfTwo(rasterization.AtlasHeight))
        {
            issues.Add(Error(
                "font.atlas",
                "The IW4 font atlas dimensions must be powers of two."));
        }

        long expectedRgbaBytes = (long)rasterization.AtlasWidth *
            rasterization.AtlasHeight * 4;
        if (rasterization.RgbaBytes is null ||
            expectedRgbaBytes > int.MaxValue ||
            rasterization.RgbaBytes.Count != expectedRgbaBytes)
        {
            issues.Add(Error(
                "font.atlas.rgbaBytes",
                "The atlas pixels must contain exactly four tightly packed RGBA bytes per pixel."));
        }
        if (rasterization.Glyphs is null ||
            rasterization.Glyphs.Count != template.Glyphs.Count)
        {
            issues.Add(Error(
                "font.glyphs",
                "Replacement must preserve the template Font character set and glyph count."));
            return;
        }

        for (int index = 0; index < rasterization.Glyphs.Count; index++)
        {
            FontRasterizedGlyph? glyph = rasterization.Glyphs[index];
            if (glyph is null)
            {
                issues.Add(Error(
                    $"font.glyphs[{index}]",
                    "Rasterized glyph rows cannot be null."));
                continue;
            }
            FontGlyph? templateGlyph = template.Glyphs[index];
            if (templateGlyph is not null && glyph.Letter != templateGlyph.Letter)
            {
                issues.Add(Error(
                    $"font.glyphs[{index}].letter",
                    $"Replacement must preserve U+{templateGlyph.Letter:X4} at this native table ordinal."));
            }
            long right = (long)glyph.AtlasX + glyph.PixelWidth;
            long bottom = (long)glyph.AtlasY + glyph.PixelHeight;
            if (glyph.AtlasX < 0 ||
                glyph.AtlasY < 0 ||
                right > rasterization.AtlasWidth ||
                bottom > rasterization.AtlasHeight)
            {
                issues.Add(Error(
                    $"font.glyphs[{index}].atlasBounds",
                    "The rasterized glyph rectangle lies outside the font atlas."));
            }
        }
    }

    private static MaterialTextureDef? SelectAtlasRow(
        MaterialAsset? template,
        string fieldPath,
        ICollection<AssetValidationIssue> issues)
    {
        if (template is null || string.IsNullOrWhiteSpace(template.Info.Name))
        {
            issues.Add(Error(
                fieldPath,
                "Font compilation requires a materialized Material template with an asset name."));
            return null;
        }
        if (template.TechniqueSet is null)
        {
            issues.Add(Error(
                fieldPath,
                "Font compilation requires a materialized technique-set dependency."));
        }
        if (template.TextureCount != template.Textures.Count)
        {
            issues.Add(Error(
                $"{fieldPath}.textures",
                "The Material texture count does not match its detached texture table."));
        }
        if (template.Textures.Any(row => row.Water is not null ||
                row.Semantic == TextureSemantic.WaterMap))
        {
            issues.Add(Error(
                $"{fieldPath}.textures",
                "Water-material templates cannot be used for an IW4 Font atlas."));
        }
        MaterialTextureDef[] candidates = template.Textures
            .Where(row => row.Image is not null && row.Water is null)
            .ToArray();
        if (candidates.Length != 1)
        {
            issues.Add(Error(
                $"{fieldPath}.textures",
                "An IW4 Font Material template must have exactly one materialized image texture row."));
            return null;
        }
        return candidates[0];
    }

    private static GfxImageAsset CreateAtlasImage(
        string name,
        FontRasterization rasterization,
        GfxImageAsset template)
    {
        int pixelByteCount = checked(rasterization.AtlasWidth * rasterization.AtlasHeight * 4);
        int payloadByteCount = checked((pixelByteCount + 0x7f) & ~0x7f);
        var payload = new byte[payloadByteCount];
        int pixelCount = checked(rasterization.AtlasWidth * rasterization.AtlasHeight);
        for (int pixel = 0; pixel < pixelCount; pixel++)
        {
            int offset = pixel * 4;
            payload[offset] = rasterization.RgbaBytes[offset + 3];
            payload[offset + 1] = rasterization.RgbaBytes[offset];
            payload[offset + 2] = rasterization.RgbaBytes[offset + 1];
            payload[offset + 3] = rasterization.RgbaBytes[offset + 2];
        }
        return new GfxImageAsset
        {
            Format = (byte)((byte)GfxImageBaseFormat.A8R8G8B8 |
                (byte)GfxImageFormatFlags.Linear),
            LevelCount = 1,
            DimensionCount = GfxImageDimension.TwoDimensional,
            TextureControl1 = 0x0001aae4,
            Width = checked((ushort)rasterization.AtlasWidth),
            Height = checked((ushort)rasterization.AtlasHeight),
            Depth = 1,
            MemoryLocation = GfxImageMemoryLocation.Local,
            // CELL_GCM_TEXTURE_LN consumes the explicit byte row pitch at +0x10.
            RenderTargetPitch = checked((uint)rasterization.AtlasWidth * 4),
            MapType = MapType.TwoDimensional,
            TextureSemantic = template.TextureSemantic,
            Category = ImageCategory.LoadFromFile,
            UseSrgbReads = template.UseSrgbReads,
            CardMemory = checked((uint)payload.Length),
            BaseWidth = checked((ushort)rasterization.AtlasWidth),
            BaseHeight = checked((ushort)rasterization.AtlasHeight),
            BaseDepth = 1,
            BaseLevelCount = 1,
            Cached = GfxImageCached.Auto,
            PayloadByteCount = payload.Length,
            PayloadBytes = payload,
            Name = name
        };
    }

    private static MaterialAsset CloneMaterialTemplate(
        MaterialAsset template,
        string name,
        MaterialTextureDef atlasRow,
        GfxImageAsset atlasImage) => new()
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
        StateFlags = template.StateFlags,
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
            Image = ReferenceEquals(row, atlasRow) ? atlasImage : row.Image,
            Water = row.Water
        }).ToArray(),
        Constants = template.Constants.Select(row => new MaterialConstantDef
        {
            NameHash = row.NameHash,
            NameBytes = row.NameBytes.ToArray(),
            Literal = row.Literal
        }).ToArray(),
        StateBits = template.StateBits.Select(row => new GfxStateBits
        {
            LoadBits = row.LoadBits.ToArray(),
            CommandWordCount = row.CommandWordCount
        }).ToArray(),
        XStrings = template.XStrings.Select(row => new MaterialXStringEntry(
            row.Index,
            default,
            row.Value)).ToArray()
    };

    private static FontAsset CopyFont(FontAsset value) => new()
    {
        Offset = value.Offset,
        RuntimeAddress = value.RuntimeAddress,
        NamePointer = value.NamePointer,
        Name = value.Name,
        PixelHeight = value.PixelHeight,
        GlyphCount = value.GlyphCount,
        MaterialPointer = value.MaterialPointer,
        Material = value.Material,
        GlowMaterialPointer = value.GlowMaterialPointer,
        GlowMaterial = value.GlowMaterial,
        GlyphsPointer = value.GlyphsPointer,
        Glyphs = value.Glyphs.ToArray()
    };

    private static string ContentDigest(
        FontAsset template,
        FontRasterization rasterization)
    {
        using SHA256 hash = SHA256.Create();
        var payload = new List<byte>();
        WriteString(payload, template.Name);
        WriteString(payload, template.Material?.Info.Name);
        WriteString(payload, template.GlowMaterial?.Info.Name);
        WriteInt32(payload, rasterization.PixelHeight);
        WriteInt32(payload, rasterization.AtlasWidth);
        WriteInt32(payload, rasterization.AtlasHeight);
        WriteInt32(payload, rasterization.RgbaBytes.Count);
        payload.AddRange(rasterization.RgbaBytes);
        WriteInt32(payload, rasterization.Glyphs.Count);
        foreach (FontRasterizedGlyph glyph in rasterization.Glyphs)
        {
            WriteUInt16(payload, glyph.Letter);
            payload.Add(unchecked((byte)glyph.X0));
            payload.Add(unchecked((byte)glyph.Y0));
            payload.Add(glyph.Dx);
            payload.Add(glyph.PixelWidth);
            payload.Add(glyph.PixelHeight);
            WriteInt32(payload, glyph.AtlasX);
            WriteInt32(payload, glyph.AtlasY);
        }
        return Convert.ToHexString(hash.ComputeHash(payload.ToArray()))
            .ToLowerInvariant()[..16];
    }

    private static string SafeNamePart(
        string? value,
        string fallback,
        int maximumLength)
    {
        string result = new((value ?? string.Empty)
            .Select(character => character is >= 'a' and <= 'z' or
                >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-'
                    ? char.ToLowerInvariant(character)
                    : '_')
            .ToArray());
        result = result.Trim('_');
        if (result.Length == 0)
            result = fallback;
        return result.Length <= maximumLength ? result : result[..maximumLength];
    }

    private static bool IsPowerOfTwo(int value) =>
        value > 0 && (value & (value - 1)) == 0;

    private static void WriteString(List<byte> values, string? value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        WriteInt32(values, bytes.Length);
        values.AddRange(bytes);
    }

    private static void WriteUInt16(List<byte> values, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        values.AddRange(bytes.ToArray());
    }

    private static void WriteInt32(List<byte> values, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        values.AddRange(bytes.ToArray());
    }

    private static AssetValidationIssue Error(string fieldPath, string message) =>
        new(fieldPath, message, AssetValidationSeverity.Error);
}
