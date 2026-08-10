using IW4.FastFiles.Loaders.Database;
using System.Text;
using IW4.Assets.Assets.MapEnts;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;

namespace IW4.FastFiles.Loaders.Assets.MapEnts;

public sealed class AddonMapEntsLoader
{
    private readonly MapTriggersLoader _mapTriggersLoader = new();

    public AddonMapEntsAsset LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            throw new InvalidDataException("Top-level AddonMapEnts pointer is null.");

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<AddonMapEntsAsset>(
                pointer,
                AddonMapEntsAsset.SerializedSize,
                "AddonMapEnts");
            AddonMapEntsAsset canonical = context.ResolveCanonicalAsset<AddonMapEntsAsset>(
                    pointer,
                    XAssetType.AddonMapEnts)
                ?? throw new InvalidDataException(
                    $"Top-level AddonMapEnts pointer 0x{unchecked((uint)pointer.Raw):X8} " +
                    "does not resolve to a canonical AddonMapEnts asset.");
            PatchCanonicalPointerCell(pointer, canonical, context);
            return canonical;
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
        {
            throw new InvalidDataException(
                $"AddonMapEnts pointer 0x{unchecked((uint)pointer.Raw):X8} has unsupported type {pointer.Type}.");
        }

        ProviderRegistrationOccurrence providerRegistration = context.BeginProviderRegistration(pointer);

        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            XBlockAddress rootAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            AddonMapEntsAsset addonMapEnts = ReadAddonMapEnts(cursor, rootAddress, context);
            AddonMapEntsAsset canonical = context.DB_AddXAsset(
                XAssetType.AddonMapEnts,
                addonMapEnts.Name,
                addonMapEnts,
                providerRegistration);

            return canonical;
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    // The fixed 0x24-byte root is staged in TEMP, followed by its name, entity
    // bytes, and embedded MapTriggers payloads in LARGE.
    private AddonMapEntsAsset ReadAddonMapEnts(
        FastFileCursor cursor,
        XBlockAddress expectedRootAddress,
        DbLoadExecutionContext context)
    {
        int sourceOffset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(
            cursor,
            AddonMapEntsAsset.SerializedSize,
            out XBlockAddress rootAddress);
        if (rootAddress != expectedRootAddress)
        {
            throw new InvalidDataException(
                $"AddonMapEnts pointer patched to {expectedRootAddress}, but root loaded at {rootAddress}.");
        }

        var rootCursor = new FastFileCursor(rootBytes, rootAddress);
        XPointer<string> namePointer = ReadPointer<string>(rootCursor, context);
        XPointer<byte[]> entityStringPointer = ReadPointer<byte[]>(rootCursor, context);
        int numEntityChars = rootCursor.ReadInt32();
        MapTriggers trigger = _mapTriggersLoader.ReadHeader(rootCursor, context);

        if (rootCursor.Offset != AddonMapEntsAsset.SerializedSize)
        {
            throw new InvalidDataException(
                $"AddonMapEnts consumed 0x{rootCursor.Offset:X} bytes instead of " +
                $"0x{AddonMapEntsAsset.SerializedSize:X}.");
        }

        string? name;
        IReadOnlyList<byte> entityStringBytes;
        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            name = context.PointerReader.LoadXString(cursor, namePointer);
            entityStringBytes = ReadByteArray(
                cursor,
                entityStringPointer.Untyped,
                numEntityChars,
                context);
            trigger = _mapTriggersLoader.LoadPayloads(cursor, trigger, context);
        }
        finally
        {
            context.Blocks.Pop();
        }

        return new AddonMapEntsAsset
        {
            Offset = sourceOffset,
            RuntimeAddress = rootAddress,
            NamePointer = namePointer,
            Name = name,
            EntityStringPointer = entityStringPointer,
            EntityStringBytes = entityStringBytes,
            EntityString = entityStringBytes.Count == 0
                ? null
                : Encoding.Latin1.GetString(entityStringBytes.ToArray()).TrimEnd('\0'),
            NumEntityChars = numEntityChars,
            Trigger = trigger
        };
    }

    private static IReadOnlyList<byte> ReadByteArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"AddonMapEnts.entityString has negative count {count}.");

        if (pointer.Type == PointerType.Null)
        {
            if (count != 0)
            {
                throw new InvalidDataException(
                    $"AddonMapEnts.entityString is null with non-zero count {count}.");
            }

            return [];
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
        {
            throw new InvalidDataException(
                $"AddonMapEnts.entityString pointer 0x{pointer.Raw:X8} is not null/inline/insert.");
        }

        XBlockAddress targetAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 1);
        byte[] bytes = context.Blocks.Load(
            cursor,
            count,
            out XBlockAddress loadedAddress);
        if (loadedAddress != targetAddress)
        {
            throw new InvalidDataException(
                $"AddonMapEnts.entityString pointer patched to {targetAddress}, " +
                $"but data loaded at {loadedAddress}.");
        }

        return bytes;
    }

    private static XPointer<T> ReadPointer<T>(
        FastFileCursor cursor,
        DbLoadExecutionContext context) => context.PointerReader.ReadPointer<T>(
            cursor,
            XPointerResolutionMode.Direct);

    private static void PatchCanonicalPointerCell(
        XPointerReference pointer,
        AddonMapEntsAsset canonical,
        DbLoadExecutionContext context)
    {
        XBlockAddress pointerCellAddress = pointer.CellAddress
            ?? throw new InvalidDataException("Packed AddonMapEnts pointer has no destination cell.");
        int canonicalRaw = canonical.RuntimeAddress?.RawValue
            ?? throw new InvalidDataException("Canonical AddonMapEnts has no runtime address.");
        context.Blocks.WriteInt32(pointerCellAddress, canonicalRaw);
    }
}
