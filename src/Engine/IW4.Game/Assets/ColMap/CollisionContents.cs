namespace IW4.Game.Assets.ColMap;

// Named masks from the PS3 SP/MP surface-parameter tables; Solid is the implicit base bit.
// Unlisted bits remain valid wire values.
[Flags]
public enum CollisionContents : int
{
    None = 0,
    Solid = 0x00000001,
    Foliage = 0x00000002,
    AiClipAllowDeath = 0x00000004,
    Glass = 0x00000010,
    Water = 0x00000020,
    CanShootClip = 0x00000040,
    ClipMissile = 0x00000080,
    VehicleClip = 0x00000200,
    ItemClip = 0x00000400,
    Sky = 0x00000800,
    AiNoSight = 0x00001000,
    ClipShot = 0x00002000,
    PlayerClip = 0x00010000,
    MonsterClip = 0x00020000,
    Mantle = 0x01000000,
    Detail = 0x08000000,
    Structural = 0x10000000,
    NoDrop = unchecked((int)0x80000000)
}
