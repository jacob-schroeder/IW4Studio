using IW4.Game.Pointers;

namespace IW4.Game.Assets.Menu;

public sealed class NewsTickerItemDefData : ItemDefDataValue
{
    public XPointer<NewsTickerDef> NewsTickerPointer { get; init; }
}
