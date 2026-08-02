using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed record StaticDvarReference(
    int Index,
    XPointer<StaticDvar> Pointer,
    StaticDvar? StaticDvar);
