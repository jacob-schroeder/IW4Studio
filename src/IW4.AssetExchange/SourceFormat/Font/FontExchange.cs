using System.Text.Encodings.Web;
using System.Text.Json;
using IW4.Assets.Assets.Font;

namespace IW4.AssetExchange.SourceFormat.Font;

/// <summary>Writes IW4 compiled fonts in the OpenAssetTools font-v1 format.</summary>
public sealed class FontExchange
{
    private static readonly JsonWriterOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = true,
        IndentCharacter = ' ',
        IndentSize = 4
    };

    public IReadOnlyList<string> Unlink(
        string sourceDirectory,
        FontAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string assetName = SourceOutput.NormalizeOwnedAssetName(
            asset.Name,
            "Font");
        Validate(asset, assetName);

        string materialName = asset.Material is null
            ? string.Empty
            : SourceOutput.NormalizeReferencedAssetName(
                asset.Material.Info.Name,
                $"Font '{assetName}' material");
        string? glowMaterialName = asset.GlowMaterial is null
            ? null
            : SourceOutput.NormalizeReferencedAssetName(
                asset.GlowMaterial.Info.Name,
                $"Font '{assetName}' glow material");

        return new SourceOutput(sourceDirectory).WriteBinaryBatch([
            (
                Path.ChangeExtension(assetName, ".json"),
                stream => WriteJson(
                    stream,
                    asset,
                    materialName,
                    glowMaterialName))
        ]);
    }

    private static void Validate(FontAsset asset, string assetName)
    {
        if (asset.PixelHeight < 0)
        {
            throw new InvalidDataException(
                $"Font '{assetName}' has negative pixel height {asset.PixelHeight}.");
        }
        if (asset.GlyphCount < 0 || asset.GlyphCount != asset.Glyphs.Count)
        {
            throw new InvalidDataException(
                $"Font '{assetName}' declares {asset.GlyphCount} glyphs but contains {asset.Glyphs.Count}.");
        }

        for (int index = 0; index < asset.Glyphs.Count; index++)
        {
            FontGlyph glyph = asset.Glyphs[index] ??
                throw new InvalidDataException(
                    $"Font '{assetName}' glyph {index} is null.");
            if (!float.IsFinite(glyph.S0) ||
                !float.IsFinite(glyph.T0) ||
                !float.IsFinite(glyph.S1) ||
                !float.IsFinite(glyph.T1))
            {
                throw new InvalidDataException(
                    $"Font '{assetName}' glyph {index} has a non-finite texture coordinate.");
            }
        }
    }

    private static void WriteJson(
        Stream stream,
        FontAsset asset,
        string materialName,
        string? glowMaterialName)
    {
        using (var writer = new Utf8JsonWriter(stream, JsonOptions))
        {
            writer.WriteStartObject();
            writer.WriteString(
                "$schema",
                "http://openassettools.dev/schema/font.v1.json");
            writer.WriteString("_type", "font");
            writer.WriteNumber("_version", 1);
            writer.WriteString("_game", "iw4");
            writer.WriteNumber("pixelHeight", asset.PixelHeight);
            writer.WriteString("material", materialName);
            if (glowMaterialName is not null)
                writer.WriteString("glowMaterial", glowMaterialName);
            writer.WriteStartArray("glyphs");
            foreach (FontGlyph glyph in asset.Glyphs)
            {
                writer.WriteStartObject();
                if (IsPrintableLetter(glyph.Letter))
                    writer.WriteString("letter", char.ConvertFromUtf32(glyph.Letter));
                else
                    writer.WriteNumber("letter", glyph.Letter);
                writer.WriteNumber("x0", glyph.X0);
                writer.WriteNumber("y0", glyph.Y0);
                writer.WriteNumber("dx", glyph.Dx);
                writer.WriteNumber("pixelWidth", glyph.PixelWidth);
                writer.WriteNumber("pixelHeight", glyph.PixelHeight);
                writer.WriteNumber("s0", glyph.S0);
                writer.WriteNumber("t0", glyph.T0);
                writer.WriteNumber("s1", glyph.S1);
                writer.WriteNumber("t1", glyph.T1);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        stream.WriteByte((byte)'\n');
    }

    private static bool IsPrintableLetter(ushort letter) =>
        letter is >= 0x20 and < 0x7F ||
        letter is >= 0xA1 and <= 0xFF and not 0xAD;
}
