namespace IW4.Assets.Assets.Weapon;

public enum WeaponClass
{
    Rifle = 0,
    Mg = 1,
    Smg = 2,
    Spread = 3,
    Pistol = 4,
    Grenade = 5,
    RocketLauncher = 6,
    Turret = 7,
    NonPlayer = 8,
    Item = 9,

    // Observed in stock serialized weapon definitions; its engine meaning is unknown.
    Unknown10 = 10,
    Unknown11 = 11
}
