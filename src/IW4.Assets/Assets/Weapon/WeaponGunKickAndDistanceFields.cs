using IW4.Assets.Math;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponGunKickAndDistanceFields
{
    public int AdsGunKickReducedKickBullets { get; init; }                        // 0x480
    public float AdsGunKickReducedKickPercent { get; init; }                      // 0x484
    public float AdsGunKickPitchMin { get; init; }                                // 0x488
    public float AdsGunKickPitchMax { get; init; }                                // 0x48C
    public float AdsGunKickYawMin { get; init; }                                  // 0x490
    public float AdsGunKickYawMax { get; init; }                                  // 0x494
    public float AdsGunKickAcceleration { get; init; }                            // 0x498
    public float AdsGunKickSpeedMax { get; init; }                                // 0x49C
    public float AdsGunKickSpeedDecay { get; init; }                              // 0x4A0
    public float AdsGunKickStaticDecay { get; init; }                             // 0x4A4
    public float AdsViewKickPitchMin { get; init; }                               // 0x4A8
    public float AdsViewKickPitchMax { get; init; }                               // 0x4AC
    public float AdsViewKickYawMin { get; init; }                                 // 0x4B0
    public float AdsViewKickYawMax { get; init; }                                 // 0x4B4
    public float AdsViewScatterMin { get; init; }                                 // 0x4B8
    public float AdsViewScatterMax { get; init; }                                 // 0x4BC
    public float AdsSpread { get; init; }                                         // 0x4C0
    public int HipGunKickReducedKickBullets { get; init; }                        // 0x4C4
    public float HipGunKickReducedKickPercent { get; init; }                      // 0x4C8
    public float HipGunKickPitchMin { get; init; }                                // 0x4CC
    public float HipGunKickPitchMax { get; init; }                                // 0x4D0
    public float HipGunKickYawMin { get; init; }                                  // 0x4D4
    public float HipGunKickYawMax { get; init; }                                  // 0x4D8
    public float HipGunKickAcceleration { get; init; }                            // 0x4DC
    public float HipGunKickSpeedMax { get; init; }                                // 0x4E0
    public float HipGunKickSpeedDecay { get; init; }                              // 0x4E4
    public float HipGunKickStaticDecay { get; init; }                             // 0x4E8
    public float HipViewKickPitchMin { get; init; }                               // 0x4EC
    public float HipViewKickPitchMax { get; init; }                               // 0x4F0
    public float HipViewKickYawMin { get; init; }                                 // 0x4F4
    public float HipViewKickYawMax { get; init; }                                 // 0x4F8
    public float HipViewScatterMin { get; init; }                                 // 0x4FC
    public float HipViewScatterMax { get; init; }                                 // 0x500
    public float FightDistance { get; init; }                                     // 0x504
    public float MaxDistance { get; init; }                                       // 0x508
}
