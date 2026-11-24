namespace IW4.Game.Assets.Weapon;

// PS3 weapon variant +0x24. The dispatch table at 0x00089FC0 maps these
// values to sound routes and separate FxImpactTable entry indices.
public enum WeaponImpactType : int
{
    None = 0,
    BulletSmall = 1,
    BulletLarge = 2,
    BulletAp = 3,
    BulletExplode = 4,
    Shotgun = 5,
    ShotgunExplode = 6,
    GrenadeBounce = 7,
    GrenadeExplode = 8,
    RocketExplode = 9,
    ProjectileDud = 10
}
