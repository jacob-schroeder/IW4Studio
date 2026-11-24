using IW4.Game.Assets.Material;
using IW4.Game.Assets.XModel;
using IW4.Game.Pointers;

namespace IW4.Game.Assets.Fx;

// fx_draw_mp SPU 0x8A28 selects the start mode from bits 0–1. Modes 2 and
// 3 both use the sequence index. Bits 2 and 3 independently control playback.
public enum FxAtlasBehavior : byte
{
    StartFixed = 0,
    StartRandom = 1,
    StartIndexed = 2,
    StartIndexedAlternate = 3,
    PlayOverLife = 4,
    LoopOnlyNTimes = 8
}

public sealed record FxElemAtlas(
    FxAtlasBehavior Behavior,
    byte Index,
    byte Fps,
    byte LoopCount,
    byte ColIndexBits,
    byte RowIndexBits,
    short EntryCount)
{
    public FxAtlasBehavior GetStartMode() => Behavior & (FxAtlasBehavior)0x03;
}
