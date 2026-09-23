using IW4.Game.Pointers;

namespace IW4.Game.Assets.Menu;

public sealed record XStringReference(
    int Index,
    XString Pointer,
    string? Value);
