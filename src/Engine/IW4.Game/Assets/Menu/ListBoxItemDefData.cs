using IW4.Game.Pointers;

namespace IW4.Game.Assets.Menu;

public sealed class ListBoxItemDefData : ItemDefDataValue
{
    public XPointer<ListBoxDef> ListBoxPointer { get; init; }
}
