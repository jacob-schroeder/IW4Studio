using IW4.Loaders.Database;
using IW4.Loaders.Assets.Fx;
using IW4.Game.Assets.Fx;
using IW4.Game.Assets.ImpactFx;
using IW4.Game.Pointers;
using IW4.Game.Zone;
using IW4.Runtime.Database;
using IW4.Game.IO;
using XString = IW4.Game.Pointers.XPointer<string>;

namespace IW4.Loaders.Assets.ImpactFx;

public sealed class FxImpactTableLoader : XAssetLoader<FxImpactTableAsset>
{
    private readonly FxEffectDefLoader _fxLoader = new();

    public FxImpactTableLoader() : base(XAssetType.ImpactFx, FxImpactTableAsset.SerializedSize, "ImpactFx")
    {
    }

    protected override FxImpactTableAsset RegisterAsset(
        FxImpactTableAsset asset,
        ProviderRegistrationOccurrence providerRegistration,
        DbLoadExecutionContext context)
    {
        if (asset.Name is null)
        {
            throw new InvalidDataException(
                $"ImpactFx root at source 0x{asset.Offset:X} has null name pointer " +
                $"0x{unchecked((uint)asset.NamePointer.Raw):X8}.");
        }
        return base.RegisterAsset(asset, providerRegistration, context);
    }

    protected override FxImpactTableAsset ReadBody(
        FastFileCursor cursor,
        XBlockAddress expectedRootAddress,
        DbLoadExecutionContext context)
    {
        int sourceOffset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(cursor, FxImpactTableAsset.SerializedSize, out XBlockAddress rootAddress);
        if (rootAddress != expectedRootAddress)
            throw new InvalidDataException($"ImpactFx pointer patched to {expectedRootAddress}, but root loaded at {rootAddress}.");

        var rootCursor = new FastFileCursor(rootBytes, rootAddress);
        XString namePointer = ReadXStringPointer(rootCursor, context);
        XPointer<FxImpactEntry[]> entriesPointer = ReadPointer<FxImpactEntry[]>(rootCursor, context, XPointerResolutionMode.Direct);

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
                FxImpactEntry.SurfaceEffectCount,
                context);
            IReadOnlyList<XPointer<FxEffectDefAsset>> fleshPointers = ReadFxEffectDefPointerBand(
                entryCursor,
                FxImpactEntry.FleshEffectCount,
                context);

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
        int count,
        DbLoadExecutionContext context)
    {
        var pointers = new XPointer<FxEffectDefAsset>[count];
        for (int i = 0; i < pointers.Length; i++)
            pointers[i] = ReadPointer<FxEffectDefAsset>(cursor, context, XPointerResolutionMode.AliasCell);

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

    private static XString ReadXStringPointer(FastFileCursor cursor, DbLoadExecutionContext context)
    {
        return ReadPointer<string>(cursor, context, XPointerResolutionMode.Direct);
    }

    private static XPointer<T> ReadPointer<T>(
        FastFileCursor cursor,
        DbLoadExecutionContext context,
        XPointerResolutionMode mode)
    {
        return context.PointerReader.ReadDeferredPointer<T>(cursor, mode);
    }
}
