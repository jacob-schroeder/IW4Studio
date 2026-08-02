using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed class MenuFileAsset : BaseAsset
{
    public const int SerializedSize = 0x0c;

    // 0x00: XString name for the type-0x18 asset.
    public XString NamePointer { get; init; }
    public string? Name { get; init; }

    // 0x04: number of entries in the MenuDef pointer table.
    public int MenuCount { get; init; }

    // 0x08: direct pointer to MenuCount type-0x19 Menu pointer cells.
    public XPointer<XPointer<MenuDefAsset>[]> MenusPointer { get; init; }
    public IReadOnlyList<MenuDefReference> Menus { get; init; } = [];
}
