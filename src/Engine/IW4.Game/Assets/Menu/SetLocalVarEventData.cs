using IW4.Game.Pointers;

namespace IW4.Game.Assets.Menu;

public sealed class SetLocalVarEventData : EventDataValue
{
    public XPointer<SetLocalVarData> SetLocalVarDataPointer { get; init; }
}
