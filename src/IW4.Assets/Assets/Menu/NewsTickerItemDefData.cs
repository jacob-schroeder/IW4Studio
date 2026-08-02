using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed class NewsTickerItemDefData : ItemDefDataValue
{
    public XPointer<NewsTickerDef> NewsTickerPointer { get; init; }
}
