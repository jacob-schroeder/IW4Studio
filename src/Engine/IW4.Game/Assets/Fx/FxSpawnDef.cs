using IW4.Game.Assets.Material;
using IW4.Game.Assets.XModel;
using IW4.Game.Pointers;

namespace IW4.Game.Assets.Fx;

/// <summary>The two serialized words use looping or one-shot count-range arms.</summary>
public sealed record FxSpawnDef(int FirstWord, int SecondWord)
{
    public FxSpawnDefLooping Looping => new(FirstWord, SecondWord);
    public FxSpawnDefOneShot OneShotCount => new(FirstWord, SecondWord);
}

public readonly record struct FxSpawnDefLooping(int IntervalMsec, int Count);
public readonly record struct FxSpawnDefOneShot(int Base, int Amplitude);
