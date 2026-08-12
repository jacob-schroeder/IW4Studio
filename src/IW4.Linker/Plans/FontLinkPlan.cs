using IW4.Assets.Assets.Font;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Plans;

/// <summary>
/// Frozen Font provider. Material fields are provider AliasCells; glyphs are
/// one direct LARGE allocation whose value does not imply storage identity.
/// </summary>
internal sealed class FontLinkPlan : AssetLinkPlan
{
    private FontLinkPlan(
        AssetKey key,
        string originalSerializedName,
        FontAsset definition,
        LinkAssetFreezeScope freeze)
        : base(
            key,
            originalSerializedName,
            freeze.FreezeProviderName(originalSerializedName, 0, "Asset.Name"))
    {
        Root = CreateOwnedRoot(definition, freeze);
    }

    internal override LinkStorageSymbol Root { get; }

    public static AssetLinkPlan Freeze(
        AssetKey key,
        string originalSerializedName,
        FontAsset definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (originalSerializedName.StartsWith(','))
        {
            ValidateReferenceShape(definition);
            return ExternalAssetLinkPlan.Create(
                key,
                XAssetType.Font,
                originalSerializedName,
                freeze);
        }

        return new FontLinkPlan(
            key,
            originalSerializedName,
            definition,
            freeze);
    }

    private static void ValidateReferenceShape(FontAsset definition)
    {
        if (definition.PixelHeight != 0 ||
            definition.GlyphCount != 0 ||
            definition.Material is not null ||
            definition.MaterialPointer.Raw != 0 ||
            definition.GlowMaterial is not null ||
            definition.GlowMaterialPointer.Raw != 0 ||
            definition.Glyphs.Count != 0)
        {
            throw new InvalidDataException(
                "A comma-prefixed Font provider must have a zeroed reference body.");
        }
    }

    private LinkStorageSymbol CreateOwnedRoot(
        FontAsset definition,
        LinkAssetFreezeScope freeze)
    {
        if (definition.GlyphCount < 0 ||
            definition.GlyphCount != definition.Glyphs.Count)
        {
            throw new InvalidDataException(
                "Font.GlyphCount must equal its nonnegative detached glyph count.");
        }

        AssetDependency? material = FreezeProviderDependency(
            definition.MaterialPointer.Untyped,
            definition.Material,
            XAssetType.Material,
            "Font.Material");
        AssetDependency? glowMaterial = FreezeProviderDependency(
            definition.GlowMaterialPointer.Untyped,
            definition.GlowMaterial,
            XAssetType.Material,
            "Font.GlowMaterial");
        LinkStorageTarget? glyphs = definition.Glyphs.Count == 0
            ? null
            : CreateGlyphStorage(definition, freeze);

        var writer = new LinkTemplateWriter(FontAsset.SerializedSize);
        writer.Skip(sizeof(int));
        writer.WriteInt32(definition.PixelHeight);
        writer.WriteInt32(definition.GlyphCount);
        writer.Skip(sizeof(int));
        writer.Skip(sizeof(int));
        writer.Skip(sizeof(int));

        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            writer.Complete(),
            alignment: 4,
            root => CreateOperations(root, material, glowMaterial, glyphs));
    }

    private IEnumerable<LinkOperation> CreateOperations(
        LinkStorageSymbol root,
        AssetDependency? material,
        AssetDependency? glowMaterial,
        LinkStorageTarget? glyphs)
    {
        yield return NameOperation(root, 0);
        if (material is { } materialDependency)
            yield return ProviderOperation(root, 0x0c, materialDependency);
        if (glowMaterial is { } glowDependency)
            yield return ProviderOperation(root, 0x10, glowDependency);
        if (glyphs is { } glyphStorage)
        {
            yield return new DirectStorageLinkOperation(
                new LinkStorageCell(root, 0x14),
                glyphStorage.View,
                glyphStorage.CanMaterializeRoot,
                "Font.Glyphs");
        }
    }

    private static LinkStorageTarget CreateGlyphStorage(
        FontAsset definition,
        LinkAssetFreezeScope freeze)
    {
        IReadOnlyList<FontGlyph> glyphs = definition.Glyphs;
        var writer = new LinkTemplateWriter(
            checked(glyphs.Count * FontAsset.GlyphSerializedSize));
        for (int index = 0; index < glyphs.Count; index++)
        {
            FontGlyph glyph = glyphs[index] ?? throw new InvalidDataException(
                $"Font.Glyphs[{index}] cannot be null.");
            writer.WriteUInt16(glyph.Letter);
            writer.WriteByte(unchecked((byte)glyph.X0));
            writer.WriteByte(unchecked((byte)glyph.Y0));
            writer.WriteByte(glyph.Dx);
            writer.WriteByte(glyph.PixelWidth);
            writer.WriteByte(glyph.PixelHeight);
            writer.WriteByte(glyph.Padding);
            writer.WriteSingle(glyph.S0);
            writer.WriteSingle(glyph.T0);
            writer.WriteSingle(glyph.S1);
            writer.WriteSingle(glyph.T1);
        }

        return freeze.FreezeStorage(
            definition.GlyphsPointer.Untyped,
            writer.Complete(),
            XFileBlockType.LARGE,
            alignment: 4,
            operations: null,
            "Font.Glyphs");
    }
}
