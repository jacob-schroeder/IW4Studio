using IW4.Game.Assets.Material;
using IW4.Game.Assets.XModel;
using IW4.Game.Pointers;

namespace IW4.Game.Assets.Fx;

public sealed record FxElemAtlas(
    byte Behavior,
    byte Index,
    byte Fps,
    byte LoopCount,
    byte ColIndexBits,
    byte RowIndexBits,
    short EntryCount);
