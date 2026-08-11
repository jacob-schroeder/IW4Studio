using IW4.Assets.Assets;
using IW4.Assets.Assets.Fx;
using IW4.Assets.Assets.ImpactFx;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Model;

/// <summary>
/// Frozen fixed-size ImpactFx matrix. Every non-null slot is a logical Fx
/// provider dependency; the matrix itself is unique presence storage.
/// </summary>
internal sealed class FxImpactTableLinkRecipe : AssetLinkRecipe
{
    private FxImpactTableLinkRecipe(
        AssetKey key,
        string originalSerializedName,
        IReadOnlyList<FxImpactEntry> entries,
        XPointerReference entriesPointer,
        LinkAssetFreezeScope freeze)
        : base(
            key,
            originalSerializedName,
            freeze.FreezeProviderName(originalSerializedName, 0, "Asset.Name"))
    {
        LinkStorageTarget table = FreezeEntryTable(entries, entriesPointer, freeze);
        var writer = new LinkTemplateWriter(FxImpactTableAsset.SerializedSize);
        writer.Skip(sizeof(int));
        writer.Skip(sizeof(int));
        Root = LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            writer.Complete(),
            alignment: 4,
            root =>
            [
                NameOperation(root, 0),
                Direct(root, 0x04, table, "ImpactFx.Entries")
            ]);
    }

    internal override LinkStorageSymbol Root { get; }

    public static AssetLinkRecipe Freeze(
        AssetKey key,
        string originalSerializedName,
        FxImpactTableAsset definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(freeze);
        IReadOnlyList<FxImpactEntry> entries = definition.Entries ??
            throw new InvalidDataException("ImpactFx.Entries cannot be null.");
        if (originalSerializedName.StartsWith(','))
        {
            if (entries.Count != 0 || definition.EntriesPointer.Raw != 0)
            {
                throw new InvalidDataException(
                    "A comma-prefixed ImpactFx provider must have a zeroed reference body.");
            }
            return ExternalAssetLinkRecipe.Create(
                key,
                XAssetType.ImpactFx,
                originalSerializedName,
                freeze);
        }
        if (entries.Count != FxImpactTableAsset.EntryCount)
        {
            throw new InvalidDataException(
                $"ImpactFx requires exactly {FxImpactTableAsset.EntryCount} entry rows.");
        }
        return new FxImpactTableLinkRecipe(
            key,
            originalSerializedName,
            entries,
            definition.EntriesPointer.Untyped,
            freeze);
    }

    private static LinkStorageTarget FreezeEntryTable(
        IReadOnlyList<FxImpactEntry> entries,
        XPointerReference pointer,
        LinkAssetFreezeScope freeze)
    {
        FrozenEntry[] frozen = entries
            .Select((entry, index) => FrozenEntry.Freeze(
                entry ?? throw new InvalidDataException(
                    $"ImpactFx.Entries[{index}] cannot be null."),
                index))
            .ToArray();
        var writer = new LinkTemplateWriter(
            checked(frozen.Length * FxImpactEntry.SerializedSize));
        foreach (FrozenEntry entry in frozen)
            writer.WriteBytes(entry.Template);
        return freeze.FreezeStorage(
            pointer,
            writer.Complete(),
            XFileBlockType.LARGE,
            alignment: 4,
            (table, addend) =>
            {
                var operations = new List<LinkOperation>();
                for (int index = 0; index < frozen.Length; index++)
                {
                    frozen[index].AppendOperations(
                        table,
                        checked(addend + index * FxImpactEntry.SerializedSize),
                        operations);
                }
                return operations;
            },
            "ImpactFx.Entries");
    }

    private static DirectStorageLinkOperation Direct(
        LinkStorageSymbol owner,
        int pointerOffset,
        LinkStorageTarget target,
        string fieldPath) =>
        new(
            new LinkStorageCell(owner, pointerOffset),
            target.View,
            target.CanMaterializeRoot,
            fieldPath);

    private sealed class FrozenEntry
    {
        private FrozenEntry(
            byte[] template,
            IReadOnlyList<AssetDependency?> surfaceEffects,
            IReadOnlyList<AssetDependency?> fleshEffects,
            int index)
        {
            Template = template;
            SurfaceEffects = surfaceEffects;
            FleshEffects = fleshEffects;
            Index = index;
        }

        public byte[] Template { get; }
        private IReadOnlyList<AssetDependency?> SurfaceEffects { get; }
        private IReadOnlyList<AssetDependency?> FleshEffects { get; }
        private int Index { get; }

        public static FrozenEntry Freeze(FxImpactEntry entry, int index)
        {
            IReadOnlyList<FxEffectDefAsset?> surfaces = entry.SurfaceEffects ??
                throw new InvalidDataException(
                    $"ImpactFx.Entries[{index}].SurfaceEffects cannot be null.");
            IReadOnlyList<FxEffectDefAsset?> flesh = entry.FleshEffects ??
                throw new InvalidDataException(
                    $"ImpactFx.Entries[{index}].FleshEffects cannot be null.");
            if (surfaces.Count != FxImpactEntry.SurfaceEffectCount ||
                flesh.Count != FxImpactEntry.FleshEffectCount ||
                entry.SurfaceEffectPointers.Count != FxImpactEntry.SurfaceEffectCount ||
                entry.FleshEffectPointers.Count != FxImpactEntry.FleshEffectCount)
            {
                throw new InvalidDataException(
                    $"ImpactFx.Entries[{index}] requires exactly " +
                    $"{FxImpactEntry.SurfaceEffectCount} surface and " +
                    $"{FxImpactEntry.FleshEffectCount} flesh effect slots.");
            }
            AssetDependency?[] frozenSurfaces = surfaces
                .Select((effect, effectIndex) => FreezeSlot(
                    effect,
                    entry.SurfaceEffectPointers[effectIndex].Untyped,
                    $"ImpactFx.Entries[{index}].SurfaceEffects[{effectIndex}]"))
                .ToArray();
            AssetDependency?[] frozenFlesh = flesh
                .Select((effect, effectIndex) => FreezeSlot(
                    effect,
                    entry.FleshEffectPointers[effectIndex].Untyped,
                    $"ImpactFx.Entries[{index}].FleshEffects[{effectIndex}]"))
                .ToArray();
            var writer = new LinkTemplateWriter(FxImpactEntry.SerializedSize);
            writer.Skip(FxImpactEntry.SerializedSize);
            return new FrozenEntry(
                writer.Complete(),
                Array.AsReadOnly(frozenSurfaces),
                Array.AsReadOnly(frozenFlesh),
                index);
        }

        private static AssetDependency? FreezeSlot(
            FxEffectDefAsset? effect,
            XPointerReference pointer,
            string fieldPath) =>
            FreezeProviderDependency(
                pointer,
                effect,
                XAssetType.Fx,
                fieldPath);

        public void AppendOperations(
            LinkStorageSymbol table,
            int baseOffset,
            ICollection<LinkOperation> operations)
        {
            for (int index = 0; index < SurfaceEffects.Count; index++)
            {
                if (SurfaceEffects[index] is { } dependency)
                {
                    operations.Add(ProviderOperation(
                        table,
                        checked(baseOffset + index * sizeof(int)),
                        dependency));
                }
            }
            int fleshOffset = checked(
                baseOffset + FxImpactEntry.SurfaceEffectCount * sizeof(int));
            for (int index = 0; index < FleshEffects.Count; index++)
            {
                if (FleshEffects[index] is { } dependency)
                {
                    operations.Add(ProviderOperation(
                        table,
                        checked(fleshOffset + index * sizeof(int)),
                        dependency));
                }
            }
        }
    }
}
