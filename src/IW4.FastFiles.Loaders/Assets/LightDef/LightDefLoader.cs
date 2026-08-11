using IW4.FastFiles.Loaders.Database;
using IW4.FastFiles.Loaders.Assets.Image;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.LightDef;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;

namespace IW4.FastFiles.Loaders.Assets.LightDef;

public sealed class LightDefLoader
{
    private readonly GfxImageLoader _imageLoader = new();

    public LightDefAsset LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            throw new InvalidDataException("Top-level LightDef pointer is null.");

        if (pointer.Type == PointerType.Offset)
        {
            LightDefAsset canonical = context.ResolveCanonicalAsset<LightDefAsset>(
                    pointer,
                    XAssetType.LightDef)
                ?? throw new InvalidDataException(
                    $"Top-level LightDef pointer 0x{unchecked((uint)pointer.Raw):X8} " +
                    "does not resolve to a canonical LightDef asset.");
            PatchCanonicalPointerCell(pointer, canonical, context, "LightDef");
            return canonical;
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
            throw new InvalidDataException($"Top-level LightDef pointer 0x{pointer.Raw:X8} does not reference inline/insert payload data.");

        ProviderRegistrationOccurrence providerRegistration = context.BeginProviderRegistration(pointer);

        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            XBlockAddress rootAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            LightDefAsset lightDef = ReadLightDef(cursor, rootAddress, context);
            LightDefAsset canonical = context.DB_AddXAsset(
                XAssetType.LightDef,
                lightDef.Name,
                lightDef,
                providerRegistration);

            return canonical;
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    private LightDefAsset ReadLightDef(
        FastFileCursor cursor,
        XBlockAddress expectedRootAddress,
        DbLoadExecutionContext context)
    {
        int sourceOffset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(cursor, LightDefAsset.SerializedSize, out XBlockAddress rootAddress);
        if (rootAddress != expectedRootAddress)
            throw new InvalidDataException($"LightDef pointer patched to {expectedRootAddress}, but root loaded at {rootAddress}.");

        var rootCursor = new FastFileCursor(rootBytes, rootAddress);
        XPointer<string> namePointer = context.PointerReader.ReadPointer<string>(rootCursor, XPointerResolutionMode.Direct);
        XPointer<GfxImageAsset> imagePointer = context.PointerReader.ReadPointer<GfxImageAsset>(rootCursor, XPointerResolutionMode.AliasCell);
        byte samplerState = rootCursor.ReadByte();
        byte[] pad09To0B = rootCursor.ReadBytes(3);
        uint lmapLookupStart = rootCursor.ReadUInt32();

        if (rootCursor.Offset != LightDefAsset.SerializedSize)
            throw new InvalidDataException($"LightDef consumed 0x{rootCursor.Offset:X} bytes instead of 0x{LightDefAsset.SerializedSize:X}.");

        string? name;
        GfxImageAsset? image;
        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            name = context.PointerReader.LoadXString(cursor, namePointer);
            image = _imageLoader.LoadFromPointer(
                cursor,
                imagePointer.Untyped,
                context);
        }
        finally
        {
            context.Blocks.Pop();
        }


        return new LightDefAsset
        {
            Offset = sourceOffset,
            RuntimeAddress = rootAddress,
            NamePointer = namePointer,
            Name = name,
            ImagePointer = imagePointer,
            Image = image,
            SamplerState = samplerState,
            Pad09To0B = pad09To0B,
            LmapLookupStart = lmapLookupStart
        };
    }

    private static void PatchCanonicalPointerCell(
        XPointerReference pointer,
        LightDefAsset canonical,
        DbLoadExecutionContext context,
        string targetName)
    {
        XBlockAddress pointerCellAddress = pointer.CellAddress
            ?? throw new InvalidDataException($"Packed {targetName} pointer has no destination cell.");
        int canonicalRaw = canonical.RuntimeAddress?.RawValue
            ?? throw new InvalidDataException($"Canonical {targetName} has no runtime address.");
        context.Blocks.WriteInt32(pointerCellAddress, canonicalRaw);
    }
}
