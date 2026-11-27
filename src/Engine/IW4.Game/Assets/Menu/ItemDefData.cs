namespace IW4.Game.Assets.Menu;

public union ItemDefData(
    EditFieldItemDefData,
    ListBoxItemDefData,
    MultiItemDefData,
    DvarEnumItemDefData,
    NewsTickerItemDefData,
    TextScrollItemDefData,
    NoItemDefData)
{
    public static ItemDefData None { get; } = new NoItemDefData();

    public EditFieldItemDefData? EditField => this is EditFieldItemDefData value ? value : null;
    public ListBoxItemDefData? ListBox => this is ListBoxItemDefData value ? value : null;
    public MultiItemDefData? Multi => this is MultiItemDefData value ? value : null;
    public DvarEnumItemDefData? DvarEnum => this is DvarEnumItemDefData value ? value : null;
    public NewsTickerItemDefData? NewsTicker => this is NewsTickerItemDefData value ? value : null;
    public TextScrollItemDefData? TextScroll => this is TextScrollItemDefData value ? value : null;
}
