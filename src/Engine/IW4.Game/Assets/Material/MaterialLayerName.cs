using IW4.Game.Pointers;

namespace IW4.Game.Assets.Material;

public sealed record MaterialLayerName(
    int Index,
    XString Pointer,
    string? Value);
