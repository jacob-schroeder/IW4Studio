using IW4.FastFiles.Strings;

namespace IW4.Assets.Assets.XAnim;

public sealed record XAnimNotifyInfo(ScriptStringReference Name, float Time)
{
    public const int SerializedSize = 0x08;
}
