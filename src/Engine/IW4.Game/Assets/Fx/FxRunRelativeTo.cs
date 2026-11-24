namespace IW4.Game.Assets.Fx;

/// <summary>Bits 6 and 7 of FxElemDef.Flags select the coordinate frame used after spawning.</summary>
public enum FxRunRelativeTo
{
    World = 0,
    Spawn = 1,
    Effect = 2,
    Offset = 3
}
