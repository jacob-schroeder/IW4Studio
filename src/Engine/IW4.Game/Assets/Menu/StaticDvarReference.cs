using IW4.Game.Pointers;

namespace IW4.Game.Assets.Menu;

public sealed record StaticDvarReference(
    int Index,
    XPointer<StaticDvar> Pointer,
    StaticDvar? StaticDvar);
