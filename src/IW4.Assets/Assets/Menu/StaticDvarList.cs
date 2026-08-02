using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed class StaticDvarList
{
    public const int SerializedSize = 0x08;

    public int NumStaticDvars { get; init; }
    public XPointer<XPointer<StaticDvar>[]> StaticDvars { get; init; }
    public IReadOnlyList<StaticDvarReference> LoadedStaticDvars { get; set; } = [];
}
