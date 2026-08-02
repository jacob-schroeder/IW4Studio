using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed class MultiItemDefData : ItemDefDataValue
{
    public XPointer<MultiDef> MultiPointer { get; init; }
}
