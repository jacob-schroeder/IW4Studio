using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed class SetLocalVarEventData : EventDataValue
{
    public XPointer<SetLocalVarData> SetLocalVarDataPointer { get; init; }
}
