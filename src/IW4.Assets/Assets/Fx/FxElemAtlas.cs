using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Fx;

public sealed record FxElemAtlas(
    byte Behavior,
    byte Index,
    byte Fps,
    byte LoopCount,
    byte ColIndexBits,
    byte RowIndexBits,
    short EntryCount);
