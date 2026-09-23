using IW4.Game.Pointers;

namespace IW4.Game.Assets.Menu;

public sealed record MenuEventHandlerReference(
    int Index,
    XPointer<MenuEventHandler> Pointer,
    MenuEventHandler? Handler);
