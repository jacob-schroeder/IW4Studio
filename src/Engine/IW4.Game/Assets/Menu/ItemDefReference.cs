using IW4.Game.Math;
using IW4.Game.Pointers;

namespace IW4.Game.Assets.Menu;

public sealed record ItemDefReference(
    int Index,
    XPointer<ItemDefAsset> Pointer,
    ItemDefAsset? Item);
