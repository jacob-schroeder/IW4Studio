using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed record MenuDefReference(
    int Index,
    XPointer<MenuDefAsset> Pointer,
    MenuDefAsset? Menu);
