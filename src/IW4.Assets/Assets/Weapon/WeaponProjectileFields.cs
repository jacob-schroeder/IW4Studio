using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using FxEffectDefAsset = IW4.Assets.Assets.Fx.FxEffectDefAsset;
using XModelAsset = IW4.Assets.Assets.XModel.XModelAsset;

namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponProjectileFields
{
    public XPointer<XModelAsset> ModelPointer { get; init; }                      // 0x420
    public XModelAsset? Model { get; init; }
    public WeaponProjectileExplosion Explosion { get; init; }                     // 0x424
    public XPointer<FxEffectDefAsset> ExplosionEffectPointer { get; init; }       // 0x428
    public XPointer<FxEffectDefAsset> DudEffectPointer { get; init; }             // 0x42C
    public XString ExplosionSoundPointer { get; init; }                           // 0x430
    public string? ExplosionSound { get; init; }
    public XString DudSoundPointer { get; init; }                                 // 0x434
    public string? DudSound { get; init; }
    public WeaponStickiness Stickiness { get; init; }                             // 0x438
    public int LowAmmoWarningThreshold { get; init; }                             // 0x43C
    public float RicochetChance { get; init; }                                    // 0x440

    // 0x444 / 0x448: direct float[31] bounce-response arrays.
    public XPointer<float[]> ParallelBouncePointer { get; init; }
    public IReadOnlyList<float> ParallelBounce { get; init; } = [];
    public XPointer<float[]> PerpendicularBouncePointer { get; init; }
    public IReadOnlyList<float> PerpendicularBounce { get; init; } = [];

    public XPointer<FxEffectDefAsset> TrailEffectPointer { get; init; }           // 0x44C
    public XPointer<FxEffectDefAsset> BeaconEffectPointer { get; init; }          // 0x450
    public Vec3 ProjectileColor { get; init; }                                    // 0x454..0x45C
    public GuidedMissileType GuidedMissileType { get; init; }                     // 0x460
    public float MaxSteeringAcceleration { get; init; }                           // 0x464
    public int IgnitionDelay { get; init; }                                       // 0x468
    public XPointer<FxEffectDefAsset> IgnitionEffectPointer { get; init; }        // 0x46C
    public XString IgnitionSoundPointer { get; init; }                            // 0x470
    public string? IgnitionSound { get; init; }
    public float AdsAimPitch { get; init; }                                       // 0x474
    public float AdsCrosshairInFraction { get; init; }                            // 0x478
    public float AdsCrosshairOutFraction { get; init; }                           // 0x47C
    public WeaponGunKickAndDistanceFields GunKickAndDistance { get; init; } = new(); // 0x480..0x508
}
