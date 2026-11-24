namespace IW4.Game.Assets.GameMap;

[Flags]
public enum PathNodeSpawnFlags : ushort
{
    None = 0,
    DontLink = 0x0001,
    IndoorTransition = 0x0002,
    DontStand = 0x0004,
    DontCrouch = 0x0008,
    DontProne = 0x0010,
    Chokepoint = 0x0020,
    Priority = 0x0040,
    Idle = 0x0080,
    CoverPeekOver = 0x0200,
    CoverPeekLeft = 0x0400,
    CoverPeekRight = 0x0800,
    LinksToChoke = 0x1000,
    Indoor = 0x2000,
    HasError = 0x4000,
    /// <summary>Enables the node angle in direction-sensitive path checks.</summary>
    HasAngles = 0x8000
}
