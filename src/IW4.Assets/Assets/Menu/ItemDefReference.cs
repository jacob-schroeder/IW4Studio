using IW4.Assets.Math;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed record ItemDefReference(
    int Index,
    XPointer<ItemDefAsset> Pointer,
    ItemDefAsset? Item);
