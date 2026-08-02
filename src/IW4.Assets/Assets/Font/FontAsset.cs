using IW4.Assets.Assets.Material;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Font;

public sealed class FontAsset : BaseAsset
{
    public const int SerializedSize = 0x18;
    public const int GlyphSerializedSize = 0x18;

    public XString NamePointer { get; init; }
    public string? Name { get; init; }
    public int PixelHeight { get; init; }
    public int GlyphCount { get; init; }
    public XPointer<MaterialAsset> MaterialPointer { get; init; }
    public MaterialAsset? Material { get; init; }
    public XPointer<MaterialAsset> GlowMaterialPointer { get; init; }
    public MaterialAsset? GlowMaterial { get; init; }
    public XPointer<FontGlyph[]> GlyphsPointer { get; init; }
    public IReadOnlyList<FontGlyph> Glyphs { get; init; } = [];
}
