using IW4.FastFiles.Loaders.Database;
using IW4.FastFiles.Loaders.Assets.Fx;
using IW4.Assets.Assets.Fx;
using IW4.Assets.Assets.ImpactFx;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;
using XString = IW4.FastFiles.Pointers.XPointer<string>;

namespace IW4.FastFiles.Loaders.Assets.ImpactFx;

public sealed class FxImpactTableLoader
{
    private readonly FxEffectDefLoader _fxLoader = new();

    public FxImpactTableAsset LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        return LoadFromPointerCore(cursor, pointer, context, requireAsset: true)
            ?? throw new InvalidDataException("Top-level ImpactFx pointer resolved to null.");
    }

    public FxImpactTableAsset? LoadFromPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        return LoadFromPointerCore(cursor, pointer, context, requireAsset: false);
    }

    private FxImpactTableAsset? LoadFromPointerCore(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context,
        bool requireAsset)
    {
        if (pointer.Type == PointerType.Null)
        {
            if (requireAsset)
                throw new InvalidDataException("Top-level ImpactFx pointer is null.");

            return null;
        }

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<FxImpactTableAsset>(
                pointer,
                FxImpactTableAsset.SerializedSize,
                "ImpactFx");
            FxImpactTableAsset? canonical = context.ResolveCanonicalAsset<FxImpactTableAsset>(
                pointer,
                XAssetType.ImpactFx);
            if (canonical is null)
            {
                throw new InvalidDataException(
                    $"ImpactFx pointer 0x{unchecked((uint)pointer.Raw):X8} does not resolve to a canonical ImpactFx asset.");
            }

            PatchCanonicalPointerCell(pointer, canonical, context);
            return canonical;
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
        {
            throw new InvalidDataException(
                $"ImpactFx pointer 0x{unchecked((uint)pointer.Raw):X8} has unsupported type {pointer.Type}.");
        }

        XBlockAddress? insertCell = pointer.Type == PointerType.Insert
            ? context.Blocks.AllocateInsertPointerCell()
            : null;

        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            XBlockAddress rootAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            FxImpactTableAsset table = ReadFxImpactTable(cursor, rootAddress, context);
            XBlockAddress pointerCellAddress = pointer.CellAddress
                ?? throw new InvalidDataException("Inline ImpactFx pointer has no destination cell.");
            if (table.Name is null)
            {
                throw new InvalidDataException(
                    $"ImpactFx root at source 0x{table.Offset:X} has null name pointer " +
                    $"0x{unchecked((uint)table.NamePointer.Raw):X8}.");
            }
            FxImpactTableAsset canonical = context.DB_AddXAsset(
                XAssetType.ImpactFx,
                table.Name,
                table,
                pointerCellAddress);

            if (insertCell is { } cell)
            {
                int canonicalRaw = canonical.RuntimeAddress?.RawValue
                    ?? throw new InvalidDataException("Canonical ImpactFx has no runtime address.");
                context.Blocks.WriteInt32(cell, canonicalRaw);
            }

            return canonical;
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    private FxImpactTableAsset ReadFxImpactTable(
        FastFileCursor cursor,
        XBlockAddress expectedRootAddress,
        DbLoadExecutionContext context)
    {
        int sourceOffset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(cursor, FxImpactTableAsset.SerializedSize, out XBlockAddress rootAddress);
        if (rootAddress != expectedRootAddress)
            throw new InvalidDataException($"ImpactFx pointer patched to {expectedRootAddress}, but root loaded at {rootAddress}.");

        var rootCursor = new FastFileCursor(rootBytes, rootAddress);
        XString namePointer = ReadXStringPointer(rootCursor);
        XPointer<FxImpactEntry[]> entriesPointer = ReadPointer<FxImpactEntry[]>(rootCursor, XPointerResolutionMode.Direct);

        if (rootCursor.Offset != FxImpactTableAsset.SerializedSize)
            throw new InvalidDataException($"ImpactFx root consumed 0x{rootCursor.Offset:X} bytes instead of 0x{FxImpactTableAsset.SerializedSize:X}.");

        string? name;
        IReadOnlyList<FxImpactEntry> entries;
        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            name = context.PointerReader.LoadXString(cursor, namePointer);
            entries = ReadFxImpactEntryArray(cursor, entriesPointer.Untyped, context);
        }
        finally
        {
            context.Blocks.Pop();
        }


        return new FxImpactTableAsset
        {
            Offset = sourceOffset,
            RuntimeAddress = rootAddress,
            NamePointer = namePointer,
            Name = name,
            EntriesPointer = entriesPointer,
            Entries = entries
        };
    }

    private static void PatchCanonicalPointerCell(
        XPointerReference pointer,
        FxImpactTableAsset canonical,
        DbLoadExecutionContext context)
    {
        XBlockAddress pointerCellAddress = pointer.CellAddress
            ?? throw new InvalidDataException("Packed ImpactFx pointer has no destination cell.");
        int canonicalRaw = canonical.RuntimeAddress?.RawValue
            ?? throw new InvalidDataException("Canonical ImpactFx has no runtime address.");
        context.Blocks.WriteInt32(pointerCellAddress, canonicalRaw);
    }

    private IReadOnlyList<FxImpactEntry> ReadFxImpactEntryArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            return [];

        if (pointer.CellAddress is not { } cellAddress)
            throw new InvalidDataException($"ImpactFx entries pointer 0x{pointer.Raw:X8} has no destination cell address.");

        context.Blocks.AlignCurrent(4);
        XBlockAddress entriesAddress = context.Blocks.CurrentAddress;
        context.Blocks.WriteInt32(cellAddress, XPointerCodec.Encode(entriesAddress));
        byte[] entryBytes = context.Blocks.Load(
            cursor,
            checked(FxImpactTableAsset.EntryCount * FxImpactEntry.SerializedSize));

        var entries = new FxImpactEntry[FxImpactTableAsset.EntryCount];
        var entryCursor = new FastFileCursor(entryBytes, entriesAddress);
        for (int i = 0; i < entries.Length; i++)
        {
            int entryOffset = entryCursor.Offset;
            XBlockAddress entryAddress = entriesAddress.Add(entryOffset);
            IReadOnlyList<XPointer<FxEffectDefAsset>> surfacePointers = ReadFxEffectDefPointerBand(
                entryCursor,
                FxImpactEntry.SurfaceEffectCount);
            IReadOnlyList<XPointer<FxEffectDefAsset>> fleshPointers = ReadFxEffectDefPointerBand(
                entryCursor,
                FxImpactEntry.FleshEffectCount);

            if (entryCursor.Offset - entryOffset != FxImpactEntry.SerializedSize)
                throw new InvalidDataException($"FxImpactEntry consumed 0x{entryCursor.Offset - entryOffset:X} bytes instead of 0x{FxImpactEntry.SerializedSize:X}.");

            IReadOnlyList<FxEffectDefAsset?> surfaceEffects = ReadFxEffectDefPointers(cursor, surfacePointers, context);
            IReadOnlyList<FxEffectDefAsset?> fleshEffects = ReadFxEffectDefPointers(cursor, fleshPointers, context);

            entries[i] = new FxImpactEntry
            {
                Offset = entryAddress.Offset,
                SurfaceEffectPointers = surfacePointers,
                SurfaceEffects = surfaceEffects,
                FleshEffectPointers = fleshPointers,
                FleshEffects = fleshEffects
            };
        }


        return entries;
    }

    private static IReadOnlyList<XPointer<FxEffectDefAsset>> ReadFxEffectDefPointerBand(
        FastFileCursor cursor,
        int count)
    {
        var pointers = new XPointer<FxEffectDefAsset>[count];
        for (int i = 0; i < pointers.Length; i++)
            pointers[i] = ReadPointer<FxEffectDefAsset>(cursor, XPointerResolutionMode.AliasCell);

        return pointers;
    }

    private IReadOnlyList<FxEffectDefAsset?> ReadFxEffectDefPointers(
        FastFileCursor cursor,
        IReadOnlyList<XPointer<FxEffectDefAsset>> pointers,
        DbLoadExecutionContext context)
    {
        var effects = new FxEffectDefAsset?[pointers.Count];
        for (int i = 0; i < effects.Length; i++)
            effects[i] = _fxLoader.LoadFromPointer(cursor, pointers[i].Untyped, context);

        return effects;
    }

    private static XString ReadXStringPointer(FastFileCursor cursor)
    {
        return ReadPointer<string>(cursor, XPointerResolutionMode.Direct);
    }

    private static XPointer<T> ReadPointer<T>(
        FastFileCursor cursor,
        XPointerResolutionMode mode)
    {
        int cellOffset = cursor.Offset;
        return new XPointer<T>(cursor.ReadInt32(), mode, cursor.AddressAt(cellOffset));
    }
}
