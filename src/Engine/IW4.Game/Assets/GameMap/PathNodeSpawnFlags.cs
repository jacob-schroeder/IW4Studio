namespace IW4.Game.Assets.GameMap;

[Flags]
public enum PathNodeSpawnFlags : ushort
{
    None = 0,
    DontLink = 0x0001,
    DontStand = 0x0004,
    DontCrouch = 0x0008,
    DontProne = 0x0010,
    Priority = 0x0040,
    HasError = 0x4000,
    /// <summary>Enables the node angle in direction-sensitive path checks.</summary>
    HasAngles = 0x8000
}
