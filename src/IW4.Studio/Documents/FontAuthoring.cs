using IW4.AssetExchange.Font;
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
        Array.AsReadOnly(FontAssemblyCompiler.Validate(value.Font)
            .Select(error => new AssetValidationIssue(
                error.FieldPath,
                error.Message,
                AssetValidationSeverity.Error))
            .ToArray());

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
