using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed record MenuEventHandlerReference(
    int Index,
    XPointer<MenuEventHandler> Pointer,
    MenuEventHandler? Handler);
