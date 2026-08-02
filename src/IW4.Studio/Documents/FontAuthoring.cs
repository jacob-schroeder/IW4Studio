using IW4.Assets.Assets.Font;
using IW4.Assets.Assets.Material;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Assets;

namespace IW4.Studio.Documents;

/// <summary>Detached Font source.  Material links retain serialized spelling
/// as symbolic references; runtime Material objects are never held here.</summary>
public sealed class FontAuthoredSnapshot : ITargetZoneDetachedSemanticSnapshot
{
    private readonly FontGlyphBuildData[] _glyphs;
    internal FontAuthoredSnapshot(string? name, int pixelHeight, SymbolicXAssetReference? material, SymbolicXAssetReference? glowMaterial, IEnumerable<FontGlyphBuildData> glyphs)
    { Name = name; PixelHeight = pixelHeight; MaterialReference = material; GlowMaterialReference = glowMaterial; _glyphs = glyphs.ToArray(); }
    public XAssetType AssetType => XAssetType.Font; public string? Name { get; } public int PixelHeight { get; } public SymbolicXAssetReference? MaterialReference { get; } public SymbolicXAssetReference? GlowMaterialReference { get; } public IReadOnlyList<FontGlyphBuildData> Glyphs => Array.AsReadOnly(_glyphs);
    internal static FontAuthoredSnapshot Import(TargetZoneRowSource source) => source.AuthoredDefinition?.SemanticSnapshot is FontAuthoredSnapshot snapshot ? snapshot : throw new InvalidDataException("Font editing requires a capture-time detached semantic snapshot because its nested pointers may be aliases.");
    internal static FontAuthoredSnapshot FromLoaded(FontAsset asset) => new(asset.Name, asset.PixelHeight, Reference(asset.Material), Reference(asset.GlowMaterial), asset.Glyphs.Select(glyph => new FontGlyphBuildData(glyph.Letter, glyph.X0, glyph.Y0, glyph.Dx, glyph.PixelWidth, glyph.PixelHeight, glyph.Padding, glyph.S0, glyph.T0, glyph.S1, glyph.T1)));
    private static SymbolicXAssetReference? Reference(MaterialAsset? value) => value?.Info.Name is { } name ? new SymbolicXAssetReference(XAssetType.Material, name) : null;
}

public sealed class FontDraft
{
    private FontGlyphBuildData[] _glyphs;
    internal FontDraft(FontAuthoredSnapshot source) { Name = source.Name; PixelHeight = source.PixelHeight; MaterialReference = source.MaterialReference; GlowMaterialReference = source.GlowMaterialReference; _glyphs = source.Glyphs.ToArray(); }
    public string? Name { get; } public int PixelHeight { get; private set; } public SymbolicXAssetReference? MaterialReference { get; private set; } public SymbolicXAssetReference? GlowMaterialReference { get; private set; } public IReadOnlyList<FontGlyphBuildData> Glyphs => Array.AsReadOnly(_glyphs.ToArray());
    public void SetPixelHeight(int value) => PixelHeight = value; public void SetMaterialReference(SymbolicXAssetReference? value) => MaterialReference = value; public void SetGlowMaterialReference(SymbolicXAssetReference? value) => GlowMaterialReference = value;
    public void SetGlyph(int index, FontGlyphBuildData value) { if ((uint)index >= _glyphs.Length) throw new ArgumentOutOfRangeException(nameof(index)); _glyphs[index] = value; }
    public void ReplaceGlyphs(IEnumerable<FontGlyphBuildData> value) { ArgumentNullException.ThrowIfNull(value); _glyphs = value.ToArray(); }
    internal FontDraft Clone() => new(new FontAuthoredSnapshot(Name, PixelHeight, MaterialReference, GlowMaterialReference, _glyphs));
}

public sealed class FontBuildData : IFontBuildData
{
    private readonly FontGlyphBuildData[] _glyphs;
    internal FontBuildData(FontDraft draft) { Name = draft.Name; PixelHeight = draft.PixelHeight; MaterialReference = draft.MaterialReference; GlowMaterialReference = draft.GlowMaterialReference; _glyphs = draft.Glyphs.ToArray(); }
    public XAssetType AssetType => XAssetType.Font; public string? Name { get; } public int PixelHeight { get; } public SymbolicXAssetReference? MaterialReference { get; } public SymbolicXAssetReference? GlowMaterialReference { get; } public IReadOnlyList<FontGlyphBuildData> Glyphs => Array.AsReadOnly(_glyphs);
}

public sealed class FontAuthoringAdapter : AssetAuthoringAdapter<FontAuthoredSnapshot, FontDraft, FontBuildData>
{
    private static readonly FontBodyEmitter Validator = new();
    public override XAssetType AssetType => XAssetType.Font; public override FontAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => FontAuthoredSnapshot.Import(source); public override FontDraft CreateDraft(FontAuthoredSnapshot snapshot) => new(snapshot); public override FontDraft CloneDraft(FontDraft draft) => draft.Clone();
    public override IReadOnlyList<AssetValidationIssue> ValidateDraft(FontDraft draft) => Validator.Validate(new FontBuildData(draft)).Select(value => new AssetValidationIssue(value.Path, value.Message, AssetValidationSeverity.Error)).ToArray();
    public override bool SemanticallyEquals(FontDraft left, FontDraft right) => left.Name == right.Name && left.PixelHeight == right.PixelHeight && left.MaterialReference == right.MaterialReference && left.GlowMaterialReference == right.GlowMaterialReference && left.Glyphs.SequenceEqual(right.Glyphs);
    public override FontBuildData ExportBuildData(FontDraft draft) { var data = new FontBuildData(draft); if (Validator.Validate(data).Count != 0) throw new InvalidOperationException("Font draft has validation errors."); return data; }
}
