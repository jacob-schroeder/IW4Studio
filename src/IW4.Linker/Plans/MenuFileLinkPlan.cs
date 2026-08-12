using IW4.Assets.Assets.Menu;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Plans;

/// <summary>
/// Frozen MenuFile provider. Its direct LARGE table owns ordered provider
/// AliasCell occurrences for the referenced Menu definitions.
/// </summary>
internal sealed class MenuFileLinkPlan : AssetLinkPlan
{
    private MenuFileLinkPlan(
        AssetKey key,
        string originalSerializedName,
        MenuFileAsset definition,
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
        MenuFileAsset definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (originalSerializedName.StartsWith(','))
        {
            ValidateReferenceShape(definition);
            return ExternalAssetLinkPlan.Create(
                key,
                XAssetType.MenuFile,
                originalSerializedName,
                freeze);
        }

        return new MenuFileLinkPlan(
            key,
            originalSerializedName,
            definition,
            freeze);
    }

    private static void ValidateReferenceShape(MenuFileAsset definition)
    {
        if (definition.MenuCount != 0 || definition.Menus.Count != 0)
        {
            throw new InvalidDataException(
                "A comma-prefixed MenuFile provider must have a zeroed reference body.");
        }
    }

    private LinkStorageSymbol CreateOwnedRoot(
        MenuFileAsset definition,
        LinkAssetFreezeScope freeze)
    {
        if (definition.MenuCount < 0 ||
            definition.MenuCount != definition.Menus.Count)
        {
            throw new InvalidDataException(
                "MenuFile.MenuCount must equal its nonnegative detached Menu rows.");
        }

        LinkStorageTarget? menus = definition.Menus.Count == 0 &&
            definition.MenusPointer.Type == PointerType.Null
                ? null
                : CreateMenuTable(definition, freeze);

        var writer = new LinkTemplateWriter(MenuFileAsset.SerializedSize);
        writer.Skip(sizeof(int));
        writer.WriteInt32(definition.MenuCount);
        writer.Skip(sizeof(int));
        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            writer.Complete(),
            alignment: 4,
            root => menus is null
                ? [NameOperation(root, 0)]
                : [
                    NameOperation(root, 0),
                    new DirectStorageLinkOperation(
                        new LinkStorageCell(root, 0x08),
                        menus.Value.View,
                        menus.Value.CanMaterializeRoot,
                        "MenuFile.Menus")
                ]);
    }

    private static LinkStorageTarget CreateMenuTable(
        MenuFileAsset definition,
        LinkAssetFreezeScope freeze)
    {
        IReadOnlyList<MenuDefReference> menus = definition.Menus;
        var dependencies = new AssetDependency?[menus.Count];
        for (int index = 0; index < menus.Count; index++)
        {
            MenuDefReference row = menus[index] ?? throw new InvalidDataException(
                $"MenuFile.Menus[{index}] cannot be null.");
            if (row.Index != index)
            {
                throw new InvalidDataException(
                    $"MenuFile.Menus[{index}] retains ordinal {row.Index}.");
            }

            dependencies[index] = FreezeProviderDependency(
                row.Pointer.Untyped,
                row.CanonicalMenu,
                XAssetType.Menu,
                $"MenuFile.Menus[{index}]",
                allowExternalReference: freeze.IsAuthoredDetached);
        }

        return freeze.FreezeStorage(
            definition.MenusPointer.Untyped,
            new byte[checked(menus.Count * sizeof(int))],
            XFileBlockType.LARGE,
            alignment: 4,
            (table, baseAddend) => dependencies
                .Select((dependency, index) => (dependency, index))
                .Where(item => item.dependency is not null)
                .Select(item => ProviderOperation(
                    table,
                    checked(baseAddend + item.index * sizeof(int)),
                    item.dependency!.Value)),
            "MenuFile.Menus");
    }
}
