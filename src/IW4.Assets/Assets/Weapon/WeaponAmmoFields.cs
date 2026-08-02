using IW4.Assets.Math;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponAmmoFields
{
    public XString AmmoNamePointer { get; init; }                                 // 0x20C
    public string? AmmoName { get; init; }
    public int AmmoIndex { get; init; }                                           // 0x210
    public XString ClipNamePointer { get; init; }                                 // 0x214
    public string? ClipName { get; init; }
    public int ClipIndex { get; init; }                                           // 0x218
    public int MaxAmmo { get; init; }                                             // 0x21C
    public int ShotCount { get; init; }                                           // 0x220
    public XString SharedAmmoCapNamePointer { get; init; }                        // 0x224
    public string? SharedAmmoCapName { get; init; }
    public int SharedAmmoCapIndex { get; init; }                                  // 0x228
    public int SharedAmmoCap { get; init; }                                       // 0x22C
    public int Damage { get; init; }                                              // 0x230
    public int PlayerDamage { get; init; }                                        // 0x234
    public int MeleeDamage { get; init; }                                         // 0x238
    public int DamageType { get; init; }                                          // 0x23C
}
