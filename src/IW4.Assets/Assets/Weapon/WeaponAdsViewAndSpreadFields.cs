using IW4.Assets.Math;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponAdsViewAndSpreadFields
{
    public float AdsBobFactor { get; init; }                                      // 0x330
    public float AdsViewBobMultiplier { get; init; }                              // 0x334
    public float HipSpreadStandMin { get; init; }                                 // 0x338
    public float HipSpreadDuckedMin { get; init; }                                // 0x33C
    public float HipSpreadProneMin { get; init; }                                 // 0x340
    public float HipSpreadStandMax { get; init; }                                 // 0x344
    public float HipSpreadDuckedMax { get; init; }                                // 0x348
    public float HipSpreadProneMax { get; init; }                                 // 0x34C
    public float HipSpreadDecayRate { get; init; }                                // 0x350
    public float HipSpreadFireAdd { get; init; }                                  // 0x354
    public float HipSpreadTurnAdd { get; init; }                                  // 0x358
    public float HipSpreadMoveAdd { get; init; }                                  // 0x35C
    public float HipSpreadDuckedDecay { get; init; }                              // 0x360
    public float HipSpreadProneDecay { get; init; }                               // 0x364
    public float HipReticleSidePosition { get; init; }                            // 0x368
    public float AdsIdleAmount { get; init; }                                     // 0x36C
    public float HipIdleAmount { get; init; }                                     // 0x370
    public float AdsIdleSpeed { get; init; }                                      // 0x374
    public float HipIdleSpeed { get; init; }                                      // 0x378
    public float IdleCrouchFactor { get; init; }                                  // 0x37C
    public float IdleProneFactor { get; init; }                                   // 0x380
    public float GunMaxPitch { get; init; }                                       // 0x384
    public float GunMaxYaw { get; init; }                                         // 0x388
    public float SwayMaxAngle { get; init; }                                      // 0x38C
    public float SwayLerpSpeed { get; init; }                                     // 0x390
    public float SwayPitchScale { get; init; }                                    // 0x394
    public float SwayYawScale { get; init; }                                      // 0x398
    public float SwayHorizontalScale { get; init; }                               // 0x39C
    public float SwayVerticalScale { get; init; }                                 // 0x3A0
    public float SwayShellShockScale { get; init; }                               // 0x3A4
    public float AdsSwayMaxAngle { get; init; }                                   // 0x3A8
    public float AdsSwayLerpSpeed { get; init; }                                  // 0x3AC
    public float AdsSwayPitchScale { get; init; }                                 // 0x3B0
    public float AdsSwayYawScale { get; init; }                                   // 0x3B4
    public float AdsSwayHorizontalScale { get; init; }                            // 0x3B8
    public float AdsSwayVerticalScale { get; init; }                              // 0x3BC
    public float AdsViewErrorMin { get; init; }                                   // 0x3C0
    public float AdsViewErrorMax { get; init; }                                   // 0x3C4
}
