using IW4.Game.Pointers;

namespace IW4.Game.Assets.Menu;

public sealed class TextScrollItemDefData : ItemDefDataValue
{
    public XPointer<TextScrollDef> TextScrollPointer { get; init; }
}
