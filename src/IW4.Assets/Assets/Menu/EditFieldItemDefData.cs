using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed class EditFieldItemDefData : ItemDefDataValue
{
    public XPointer<EditFieldDef> EditFieldPointer { get; init; }
}
