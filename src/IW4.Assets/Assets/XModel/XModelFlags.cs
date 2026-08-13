namespace IW4.Assets.Assets.XModel;

/// <summary>
/// IW4 XModel behavior flags. Unnamed bits remain round-trippable.
/// </summary>
[Flags]
public enum XModelFlags : byte
{
    None = 0,
    GroundLighting = 0x01
}
