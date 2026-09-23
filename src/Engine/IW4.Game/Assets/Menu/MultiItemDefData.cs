using IW4.Game.Pointers;

namespace IW4.Game.Assets.Menu;

public sealed class MultiItemDefData : ItemDefDataValue
{
    public XPointer<MultiDef> MultiPointer { get; init; }
}
