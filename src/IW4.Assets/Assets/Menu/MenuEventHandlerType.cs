namespace IW4.Assets.Assets.Menu;

public enum MenuEventHandlerType : byte
{
    UnconditionalScript = 0,
    ConditionalScript = 1,
    ElseScript = 2,
    SetLocalVarBool = 3,
    SetLocalVarInt = 4,
    SetLocalVarFloat = 5,
    SetLocalVarString = 6
}
