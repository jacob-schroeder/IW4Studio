using IW4.FastFiles.Loaders.Database;
using IW4.FastFiles.Loaders.Assets.Material;
using IW4.Assets.Assets.Font;
using IW4.Assets.Assets.Material;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;
using XString = IW4.FastFiles.Pointers.XPointer<string>;

namespace IW4.FastFiles.Loaders.Assets.Font;

public sealed class FontLoader
{
    private readonly MaterialLoader _materialLoader = new();

    public FontAsset LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            throw new InvalidDataException("Top-level Font pointer is null.");

        if (pointer.Type == PointerType.Offset)
        {
            FontAsset canonical = context.ResolveCanonicalAsset<FontAsset>(
                    pointer,
                    XAssetType.Font)
                ?? throw new InvalidDataException(
                    $"Top-level Font pointer 0x{unchecked((uint)pointer.Raw):X8} " +
                    "does not resolve to a canonical Font asset.");
            PatchCanonicalPointerCell(pointer, canonical, context, "Font");
            return canonical;
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
            throw new InvalidDataException($"Top-level Font pointer 0x{pointer.Raw:X8} does not reference inline/insert payload data.");

        XBlockAddress? insertCell = pointer.Type == PointerType.Insert
            ? context.Blocks.AllocateInsertPointerCell()
            : null;

        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            XBlockAddress rootAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            FontAsset font = ReadFont(cursor, rootAddress, context);
            XBlockAddress pointerCellAddress = pointer.CellAddress
                ?? throw new InvalidDataException("Inline Font pointer has no destination cell.");
            FontAsset canonical = context.DB_AddXAsset(
                XAssetType.Font,
                font.Name,
                font,
                pointerCellAddress);

            if (insertCell is { } cell)
            {
                int canonicalRaw = canonical.RuntimeAddress?.RawValue
                    ?? throw new InvalidDataException("Canonical Font has no runtime address.");
                context.Blocks.WriteInt32(cell, canonicalRaw);
            }

            return canonical;
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    private FontAsset ReadFont(
        FastFileCursor cursor,
        XBlockAddress expectedRootAddress,
        DbLoadExecutionContext context)
    {
        int offset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(cursor, FontAsset.SerializedSize, out XBlockAddress rootAddress);
        if (rootAddress != expectedRootAddress)
            throw new InvalidDataException($"Font pointer patched to {expectedRootAddress}, but root loaded at {rootAddress}.");

        var rootCursor = new FastFileCursor(rootBytes, rootAddress);

        XString namePointer = context.PointerReader.ReadPointer<string>(rootCursor, XPointerResolutionMode.Direct);
        int pixelHeight = rootCursor.ReadInt32();
        int glyphCount = rootCursor.ReadInt32();
        XPointer<MaterialAsset> materialPointer = context.PointerReader.ReadPointer<MaterialAsset>(rootCursor, XPointerResolutionMode.AliasCell);
        XPointer<MaterialAsset> glowMaterialPointer = context.PointerReader.ReadPointer<MaterialAsset>(rootCursor, XPointerResolutionMode.AliasCell);
        XPointer<FontGlyph[]> glyphsPointer = context.PointerReader.ReadPointer<FontGlyph[]>(rootCursor, XPointerResolutionMode.Direct);

        if (rootCursor.Offset != FontAsset.SerializedSize)
            throw new InvalidDataException($"Font consumed 0x{rootCursor.Offset:X} bytes instead of 0x{FontAsset.SerializedSize:X}.");


        string? name;
        MaterialAsset? material;
        MaterialAsset? glowMaterial;
        IReadOnlyList<FontGlyph> glyphs;

        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            name = context.PointerReader.LoadXString(cursor, namePointer);
            material = ReadMaterialPointer(cursor, materialPointer.Untyped, context);
            glowMaterial = ReadMaterialPointer(cursor, glowMaterialPointer.Untyped, context);
            glyphs = ReadGlyphArray(cursor, glyphsPointer.Untyped, glyphCount, context);
        }
        finally
        {
            context.Blocks.Pop();
        }

        return new FontAsset
        {
            Offset = offset,
            RuntimeAddress = rootAddress,
            NamePointer = namePointer,
            Name = name,
            PixelHeight = pixelHeight,
            GlyphCount = glyphCount,
            MaterialPointer = materialPointer,
            Material = material,
            GlowMaterialPointer = glowMaterialPointer,
            GlowMaterial = glowMaterial,
            GlyphsPointer = glyphsPointer,
            Glyphs = glyphs
        };
    }

    private static void PatchCanonicalPointerCell(
        XPointerReference pointer,
        FontAsset canonical,
        DbLoadExecutionContext context,
        string targetName)
    {
        XBlockAddress pointerCellAddress = pointer.CellAddress
            ?? throw new InvalidDataException($"Packed {targetName} pointer has no destination cell.");
        int canonicalRaw = canonical.RuntimeAddress?.RawValue
            ?? throw new InvalidDataException($"Canonical {targetName} has no runtime address.");
        context.Blocks.WriteInt32(pointerCellAddress, canonicalRaw);
    }

    private MaterialAsset? ReadMaterialPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        return _materialLoader.LoadFromPointer(cursor, pointer, context);
    }

    private static IReadOnlyList<FontGlyph> ReadGlyphArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int glyphCount,
        DbLoadExecutionContext context)
    {
        if (glyphCount < 0)
            throw new InvalidDataException($"Invalid negative Font glyph count {glyphCount}.");

        int byteCount = checked(glyphCount * FontAsset.GlyphSerializedSize);
        if (pointer.Type == PointerType.Null)
            return [];

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<FontGlyph[]>(pointer, byteCount, "FontGlyph[]");
            return [];
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
            return [];

        XBlockAddress? insertCell = pointer.Type == PointerType.Insert
            ? context.Blocks.AllocateInsertPointerCell()
            : null;

        XBlockAddress glyphAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        byte[] glyphBytes = context.Blocks.Load(cursor, byteCount);
        if (insertCell is { } cell)
            context.Blocks.WriteInt32(cell, XPointerCodec.Encode(glyphAddress));

        var glyphCursor = new FastFileCursor(glyphBytes, glyphAddress);
        var glyphs = new FontGlyph[glyphCount];
        for (int i = 0; i < glyphs.Length; i++)
            glyphs[i] = ReadGlyph(glyphCursor);


        return glyphs;
    }

    private static FontGlyph ReadGlyph(FastFileCursor cursor)
    {
        int start = cursor.Offset;
        var glyph = new FontGlyph(
            cursor.ReadUInt16(),
            unchecked((sbyte)cursor.ReadByte()),
            unchecked((sbyte)cursor.ReadByte()),
            cursor.ReadByte(),
            cursor.ReadByte(),
            cursor.ReadByte(),
            cursor.ReadByte(),
            ReadSingle(cursor),
            ReadSingle(cursor),
            ReadSingle(cursor),
            ReadSingle(cursor));

        if (cursor.Offset - start != FontAsset.GlyphSerializedSize)
            throw new InvalidDataException($"FontGlyph consumed 0x{cursor.Offset - start:X} bytes instead of 0x{FontAsset.GlyphSerializedSize:X}.");

        return glyph;
    }

    private static float ReadSingle(FastFileCursor cursor)
    {
        return BitConverter.Int32BitsToSingle(cursor.ReadInt32());
    }
}
