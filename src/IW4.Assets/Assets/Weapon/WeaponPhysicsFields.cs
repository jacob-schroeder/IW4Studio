using IW4.Assets.Math;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponPhysicsFields
{
    public float DualWieldViewModelOffset { get; init; }                          // 0x3CC
    public int KillIconRatio { get; init; }                                       // 0x3D0
    public int ReloadAmmoAdd { get; init; }                                       // 0x3D4
    public int ReloadStartAdd { get; init; }                                      // 0x3D8
    public int AmmoDropStockMin { get; init; }                                    // 0x3DC
    public float AmmoDropClipPercentMin { get; init; }                            // 0x3E0
    public float AmmoDropClipPercentMax { get; init; }                            // 0x3E4
    public int ExplosionRadius { get; init; }                                     // 0x3E8
    public int ExplosionRadiusMin { get; init; }                                  // 0x3EC
    public int ExplosionInnerDamage { get; init; }                                // 0x3F0
    public int ExplosionOuterDamage { get; init; }                                // 0x3F4
    public float DamageConeAngle { get; init; }                                   // 0x3F8
    public float BulletExplosionDamageMultiplier { get; init; }                   // 0x3FC
    public float BulletExplosionRadiusMultiplier { get; init; }                   // 0x400
    public int ProjectileSpeed { get; init; }                                     // 0x404
    public int ProjectileSpeedUp { get; init; }                                   // 0x408
    public int ProjectileSpeedForward { get; init; }                              // 0x40C
    public int ProjectileActivateDistance { get; init; }                          // 0x410
    public int ProjectileLifetime { get; init; }                                  // 0x414
    public int TimeToAccelerate { get; init; }                                    // 0x418
    public float ProjectileCurvature { get; init; }                               // 0x41C
}
