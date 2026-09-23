using IW4.Game.Pointers;

namespace IW4.Game.Assets.Menu;

public sealed class EditFieldItemDefData : ItemDefDataValue
{
    public XPointer<EditFieldDef> EditFieldPointer { get; init; }
}
