using IW4.Loaders.Database;
using IW4.Loaders.Assets.Material;
using IW4.Game.Assets.Font;
using IW4.Game.Assets.Material;
using IW4.Game.Pointers;
using IW4.Game.Zone;
using IW4.Runtime.Database;
using IW4.Game.IO;
using XString = IW4.Game.Pointers.XPointer<string>;

namespace IW4.Loaders.Assets.Font;

public sealed class FontLoader : XAssetLoader<FontAsset>
{
    private readonly MaterialLoader _materialLoader = new();

    protected override bool ValidatePackedPointerRange => false;

    public FontLoader() : base(XAssetType.Font, FontAsset.SerializedSize, "Font")
    {
    }

    protected override FontAsset ReadBody(
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
            cursor.ReadSingle(),
            cursor.ReadSingle(),
            cursor.ReadSingle(),
            cursor.ReadSingle());

        if (cursor.Offset - start != FontAsset.GlyphSerializedSize)
            throw new InvalidDataException($"FontGlyph consumed 0x{cursor.Offset - start:X} bytes instead of 0x{FontAsset.GlyphSerializedSize:X}.");

        return glyph;
    }

}
