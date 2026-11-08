namespace IW4.Game.Assets.XAnim;

/// <summary>
/// Loop and delta bits already used by the console source reader and compiled
/// animation codec. The historical DeltaFlags asset field is a bone count.
/// </summary>
[Flags]
public enum XAnimFlags : byte
{
    None = 0,
    Loop = 1,
    Delta = 2,
    Delta3D = 4
}
