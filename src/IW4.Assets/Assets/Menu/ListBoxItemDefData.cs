using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed class ListBoxItemDefData : ItemDefDataValue
{
    public XPointer<ListBoxDef> ListBoxPointer { get; init; }
}
