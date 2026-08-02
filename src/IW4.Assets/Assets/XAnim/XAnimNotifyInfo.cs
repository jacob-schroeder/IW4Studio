using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.XAnim;

public sealed record XAnimNotifyInfo(ushort Name, float Time)
{
    public const int SerializedSize = 0x08;
}
