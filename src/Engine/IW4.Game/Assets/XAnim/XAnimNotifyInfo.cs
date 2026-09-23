using IW4.Game.ScriptStrings;

namespace IW4.Game.Assets.XAnim;

public sealed record XAnimNotifyInfo(ScriptStringReference Name, float Time)
{
    public const int SerializedSize = 0x08;
}
