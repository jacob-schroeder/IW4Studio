using IW4.Game.Assets.Material;
using IW4.Game.Assets.XModel;
using IW4.Game.Pointers;

namespace IW4.Game.Assets.Fx;

/// <summary>
/// Looping elements store an interval in milliseconds and a count. One-shot
/// elements and their copies in the emission group store a count range.
/// </summary>
public sealed record FxSpawnDef(int FirstWord, int SecondWord)
{
    public FxSpawnDefLooping Looping => new(FirstWord, SecondWord);
    public FxSpawnDefOneShot OneShotCount => new(FirstWord, SecondWord);
}

public readonly record struct FxSpawnDefLooping(int IntervalMsec, int Count);
public readonly record struct FxSpawnDefOneShot(int Base, int Amplitude);
