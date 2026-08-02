using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed class ItemDefData
{
    public ItemDefDataValue Value { get; init; } = new NoItemDefData();

    public EditFieldItemDefData? EditField => Value as EditFieldItemDefData;
    public ListBoxItemDefData? ListBox => Value as ListBoxItemDefData;
    public MultiItemDefData? Multi => Value as MultiItemDefData;
    public DvarEnumItemDefData? DvarEnum => Value as DvarEnumItemDefData;
    public NewsTickerItemDefData? NewsTicker => Value as NewsTickerItemDefData;
    public TextScrollItemDefData? TextScroll => Value as TextScrollItemDefData;
}
