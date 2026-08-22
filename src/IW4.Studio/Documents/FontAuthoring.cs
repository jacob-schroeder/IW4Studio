using IW4.Assets.Assets.Font;
using IW4.FastFiles.Zone;

namespace IW4.Studio.Documents;

public sealed class FontDraft
{
    internal FontDraft(FontAsset value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Font = Copy(value);
    }

    private FontDraft(FontDraft value) => Font = Copy(value.Font);

    public FontAsset Font { get; }

    internal FontDraft Clone() => new(this);

    internal FontAsset ToAsset() => Copy(Font);

    private static FontAsset Copy(FontAsset value) => new()
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
}

internal sealed class FontAdapter : AssetAuthoringAdapter<FontAsset, FontDraft>
{
    public override XAssetType AssetType => XAssetType.Font;

    public override FontDraft CreateDraft(FontAsset value) => new(value);

    public override FontDraft CloneDraft(FontDraft value) => value.Clone();

    public override FontAsset CreateDefinition(FontDraft value) => value.ToAsset();

    public override IReadOnlyList<AssetValidationIssue> Validate(FontDraft value) =>
        FontAuthoringValidator.Validate(value.Font);

    public override bool SemanticallyEquals(FontDraft left, FontDraft right)
    {
        FontAsset x = left.Font;
        FontAsset y = right.Font;
        return string.Equals(x.Name, y.Name, StringComparison.Ordinal) &&
            x.PixelHeight == y.PixelHeight &&
            x.GlyphCount == y.GlyphCount &&
            ProviderNameEquals(x.Material, y.Material) &&
            ProviderNameEquals(x.GlowMaterial, y.GlowMaterial) &&
            x.Glyphs.SequenceEqual(y.Glyphs);
    }

    private static bool ProviderNameEquals(
        IW4.Assets.Assets.Material.MaterialAsset? left,
        IW4.Assets.Assets.Material.MaterialAsset? right) =>
        ReferenceEquals(left, right) ||
        left is not null &&
        right is not null &&
        string.Equals(left.Info.Name, right.Info.Name, StringComparison.Ordinal);
}

internal static class FontAuthoringValidator
{
    private const int NativeAsciiGlyphCount = 96;

    public static IReadOnlyList<AssetValidationIssue> Validate(FontAsset value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var issues = new List<AssetValidationIssue>();
        if (string.IsNullOrWhiteSpace(value.Name))
        {
            issues.Add(Error("font.name", "A Font requires an asset name."));
        }
        if (value.PixelHeight is <= 0 or > byte.MaxValue)
        {
            issues.Add(Error(
                "font.pixelHeight",
                $"IW4 Font pixel height must be between 1 and {byte.MaxValue}."));
        }
        if (value.GlyphCount != value.Glyphs.Count)
        {
            issues.Add(Error(
                "font.glyphCount",
                "Font.GlyphCount must equal its detached glyph count."));
        }
        if (value.Glyphs.Count < NativeAsciiGlyphCount)
        {
            issues.Add(Error(
                "font.glyphs",
                "The native IW4 glyph table requires 96 direct ASCII entries."));
        }
        else
        {
            for (int ordinal = 0; ordinal < NativeAsciiGlyphCount; ordinal++)
            {
                ushort expected = checked((ushort)(0x20 + ordinal));
                FontGlyph? glyph = value.Glyphs[ordinal];
                if (glyph is not null && glyph.Letter == expected)
                    continue;
                issues.Add(Error(
                    $"font.glyphs[{ordinal}].letter",
                    $"Native IW4 glyph ordinal {ordinal} must be U+{expected:X4}."));
                break;
            }
            for (int ordinal = NativeAsciiGlyphCount + 1;
                 ordinal < value.Glyphs.Count;
                 ordinal++)
            {
                FontGlyph? previous = value.Glyphs[ordinal - 1];
                FontGlyph? current = value.Glyphs[ordinal];
                if (previous is not null &&
                    current is not null &&
                    previous.Letter <= current.Letter)
                    continue;
                issues.Add(Error(
                    $"font.glyphs[{ordinal}].letter",
                    "The native IW4 glyph suffix must be sorted by UTF-16 code unit."));
                break;
            }
        }
        if (value.Material is null || string.IsNullOrWhiteSpace(value.Material.Info.Name))
        {
            issues.Add(Error(
                "font.material",
                "A Font requires a materialized primary Material provider with an asset name."));
        }
        if (value.GlowMaterial is not null &&
            string.IsNullOrWhiteSpace(value.GlowMaterial.Info.Name))
        {
            issues.Add(Error(
                "font.glowMaterial",
                "A materialized Font glow Material requires an asset name."));
        }

        for (int index = 0; index < value.Glyphs.Count; index++)
        {
            FontGlyph? glyph = value.Glyphs[index];
            if (glyph is null)
            {
                issues.Add(Error(
                    $"font.glyphs[{index}]",
                    "Font glyph rows cannot be null."));
                continue;
            }
            if (!float.IsFinite(glyph.S0) ||
                !float.IsFinite(glyph.T0) ||
                !float.IsFinite(glyph.S1) ||
                !float.IsFinite(glyph.T1) ||
                glyph.S0 < 0f || glyph.S0 > 1f ||
                glyph.T0 < 0f || glyph.T0 > 1f ||
                glyph.S1 < glyph.S0 || glyph.S1 > 1f ||
                glyph.T1 < glyph.T0 || glyph.T1 > 1f)
            {
                issues.Add(Error(
                    $"font.glyphs[{index}].uv",
                    "Font glyph UV bounds must be finite, normalized, and ordered."));
            }
        }
        return Array.AsReadOnly(issues.ToArray());
    }

    private static AssetValidationIssue Error(string fieldPath, string message) =>
        new(fieldPath, message, AssetValidationSeverity.Error);
}
