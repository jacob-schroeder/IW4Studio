using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.Menu;

public sealed class MenuFileAsset : BaseAsset
{
    public const int SerializedSize = 0x0c;
    public override XAssetType SerializedAssetType => XAssetType.MenuFile;

    // 0x00: XString name for the type-0x18 asset.
    public XString NamePointer { get; init; }
    public string? Name { get; init; }
    public override string? SerializedAssetName => Name;

    // 0x04: number of entries in the MenuDef pointer table.
    public int MenuCount { get; init; }

    // 0x08: direct pointer to MenuCount type-0x19 Menu pointer cells.
    public XPointer<XPointer<MenuDefAsset>[]> MenusPointer { get; init; }
    // Resolved canonical Menu entries selected by DB_AddXAsset.
    public IReadOnlyList<MenuDefReference> Menus { get; init; } = [];
}
