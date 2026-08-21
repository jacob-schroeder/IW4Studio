using IW4.FastFiles.Pointers;
using FxEffectDefAsset = IW4.Assets.Assets.Fx.FxEffectDefAsset;
using PhysCollmapAsset = IW4.Assets.Assets.Physics.PhysCollmapAsset;
using TracerDefAsset = IW4.Assets.Assets.Tracer.TracerDefAsset;
using XModelAsset = IW4.Assets.Assets.XModel.XModelAsset;

namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponDef
{
    public const int SerializedSize = 0x684;
    public const int GunModelCount = 16;
    public const int WeaponAnimCount = 37;
    public const int NoteTrackMapCount = 16;
    public const int SurfaceCount = 31;
    public const int HitLocationCount = 20;
    public const int WeaponSoundAliasCount = 47;
    public const int TurretBarrelSpinSoundCount = 4;

    public int Offset { get; init; }

    // 0x000: XString.
    public XString InternalNamePointer { get; init; }
    public string? InternalName { get; init; }

    // 0x004: direct XModelPtr[16].
    public XPointer<XPointer<XModelAsset>[]> GunModelsPointer { get; init; }
    public IReadOnlyList<XPointer<XModelAsset>> GunModelPointers { get; init; } = [];
    public IReadOnlyList<XModelAsset?> GunModels { get; init; } = [];

    // 0x008: alias-cell XModel pointer.
    public XPointer<XModelAsset> HandModelPointer { get; init; }
    public XModelAsset? HandModel { get; init; }

    // 0x00C / 0x010: direct XString pointer arrays, count 37.
    public XPointer<XString[]> RightHandAnimationNamesPointer { get; init; }
    public IReadOnlyList<XString> RightHandAnimationNamePointers { get; init; } = [];
    public IReadOnlyList<string?> RightHandAnimationNames { get; init; } = [];
    public XPointer<XString[]> LeftHandAnimationNamesPointer { get; init; }
    public IReadOnlyList<XString> LeftHandAnimationNamePointers { get; init; } = [];
    public IReadOnlyList<string?> LeftHandAnimationNames { get; init; } = [];

    // 0x014: XString.
    public XString ModeNamePointer { get; init; }
    public string? ModeName { get; init; }

    public WeaponNoteTrackMaps NoteTrackMaps { get; init; } = new(); // 0x018..0x024

    public int PlayerAnimType { get; init; }                 // 0x028
    public WeaponType WeaponType { get; init; }              // 0x02C: PS3 runtime discriminator.
    public WeaponClass WeaponClass { get; init; }            // 0x030: PS3 runtime discriminator.
    public PenetrateType PenetrateType { get; init; }         // 0x034
    public WeaponInventoryType InventoryType { get; init; }   // 0x038
    public WeaponFireType FireType { get; init; }             // 0x03C
    public OffhandClass OffhandClass { get; init; }           // 0x040
    public WeaponStance Stance { get; init; }                 // 0x044

    // 0x048..0x120: effect, sound, and material pointer region.
    public IReadOnlyList<XPointer<FxEffectDefAsset>> FlashEffectPointers { get; init; } = [];
    public IReadOnlyList<FxEffectDefAsset?> FlashEffects { get; init; } = [];
    // Root cells point to one-word wrappers; these retain each wrapper's
    // nested XString cell separately from its outer direct pointer.
    public IReadOnlyList<XString> SoundAliasPointers { get; init; } = [];
    public IReadOnlyList<XString> SoundAliasValuePointers { get; init; } = [];
    public IReadOnlyList<string?> SoundAliasNames { get; init; } = [];
    public XPointer<XString[]> BounceSoundPointer { get; init; }
    public IReadOnlyList<XString> BounceSoundPointers { get; init; } = [];
    public IReadOnlyList<XString> BounceSoundValuePointers { get; init; } = [];
    public IReadOnlyList<string?> BounceSoundNames { get; init; } = [];
    public IReadOnlyList<XPointer<FxEffectDefAsset>> EffectPointers { get; init; } = [];
    public IReadOnlyList<FxEffectDefAsset?> Effects { get; init; } = [];
    public IReadOnlyList<XPointer<Material.MaterialAsset>> MaterialPointers { get; init; } = [];
    public IReadOnlyList<Material.MaterialAsset?> Materials { get; init; } = [];
    public WeaponReticleFields Reticle { get; init; } = new();                         // 0x128..0x134
    public WeaponViewMovementFields ViewMovement { get; init; } = new();               // 0x138..0x1AC
    public WeaponPositionalMovementFields PositionalMovement { get; init; } = new();   // 0x1B0..0x1D4

    // 0x1D8: direct XModelPtr[16] world-model variants.
    public XPointer<XPointer<XModelAsset>[]> WorldGunModelsPointer { get; init; }
    public IReadOnlyList<XPointer<XModelAsset>> WorldGunModelPointers { get; init; } = [];
    public IReadOnlyList<XModelAsset?> WorldGunModels { get; init; } = [];

    // 0x1DC: alias-cell world clip XModel pointer.
    public XPointer<XModelAsset> WorldClipModelPointer { get; init; }
    public XModelAsset? WorldClipModel { get; init; }
    // 0x1E0: alias-cell first-person rocket XModel pointer.
    public XPointer<XModelAsset> RocketModelPointer { get; init; }
    public XModelAsset? RocketModel { get; init; }
    // 0x1E4: alias-cell first-person knife XModel pointer.
    public XPointer<XModelAsset> KnifeModelPointer { get; init; }
    public XModelAsset? KnifeModel { get; init; }
    // 0x1E8: alias-cell world knife XModel pointer.
    public XPointer<XModelAsset> WorldKnifeModelPointer { get; init; }
    public XModelAsset? WorldKnifeModel { get; init; }

    public WeaponIconPointers Icons { get; init; } = new();  // 0x1EC..0x208
    public IReadOnlyList<Material.MaterialAsset?> IconMaterials { get; init; } = [];
    public WeaponAmmoFields Ammo { get; init; } = new();     // 0x20C..0x23C
    public WeaponOverlayFields Overlay { get; init; } = new(); // 0x308..0x32C
    public IReadOnlyList<Material.MaterialAsset?> OverlayMaterials { get; init; } = [];
    public WeaponTimingFields Timing { get; init; } = new();                           // 0x240..0x2DC
    public WeaponAimMovementTuningFields AimMovementTuning { get; init; } = new();     // 0x2E0..0x304
    public WeaponAdsViewAndSpreadFields AdsViewAndSpread { get; init; } = new();       // 0x330..0x3C4

    // 0x3C8: alias-cell PhysCollmap pointer.
    public XPointer<PhysCollmapAsset> PhysCollmapPointer { get; init; }
    public PhysCollmapAsset? PhysCollmap { get; init; }
    public string? PhysCollmapName { get; init; }
    public WeaponPhysicsFields Physics { get; init; } = new();                         // 0x3CC..0x41C

    public WeaponProjectileFields Projectile { get; init; } = new(); // 0x420..0x470
    public IReadOnlyList<FxEffectDefAsset?> ProjectileEffects { get; init; } = [];
    public IReadOnlyList<FxEffectDefAsset?> ImpactEffects { get; init; } = [];
    public FxEffectDefAsset? ViewShellEjectEffect { get; init; }
    public WeaponAccuracyFields Accuracy { get; init; } = new();     // 0x50C..0x53C
    public WeaponTurnSpeedAndRangeFields TurnSpeedAndRange { get; init; } = new();     // 0x540..0x564
    public WeaponHintFields Hints { get; init; } = new();            // 0x568..0x574

    // 0x58C: ScriptName XString.
    public XString ScriptNamePointer { get; init; }
    public string? ScriptName { get; init; }
    public float OOPosAnimLength { get; init; }                                        // 0x590
    public float MinDamage { get; init; }                                              // 0x594
    public int MinPlayerDamage { get; init; }                                          // 0x598
    public float MaxDamageRange { get; init; }                                         // 0x59C
    public float MinDamageRange { get; init; }                                         // 0x5A0
    public float DestabilizationRateTime { get; init; }                                // 0x5A4
    public float DestabilizationCurvatureMax { get; init; }                            // 0x5A8
    public float DestabilizeDistance { get; init; }                                    // 0x5AC
    public int DestabilizeDistanceToTimeScale { get; init; }                           // 0x5B0

    // 0x5B4: direct float[20] hit-location multiplier array.
    public XPointer<float[]> LocationDamageMultipliersPointer { get; init; }
    public IReadOnlyList<float> LocationDamageMultipliers { get; init; } = [];

    public WeaponRumbleFields Rumble { get; init; } = new();          // 0x5B8..0x5BC
    public XPointer<TracerDefAsset> TracerPointer { get; init; }      // 0x5C0
    public TracerDefAsset? Tracer { get; init; }
    public float TurretScopeZoomRate { get; init; }                                     // 0x5C4
    public float TurretScopeZoomMin { get; init; }                                      // 0x5C8
    public float TurretScopeZoomMax { get; init; }                                      // 0x5CC
    public float TurretOverheatUpRate { get; init; }                                    // 0x5D0
    public float TurretOverheatDownRate { get; init; }                                  // 0x5D4
    public float TurretOverheatPenalty { get; init; }                                   // 0x5D8
    public WeaponTurretFields Turret { get; init; } = new();          // 0x5DC..0x608
    public FxEffectDefAsset? TurretOverheatEffect { get; init; }
    public WeaponMissileConeSoundFields MissileConeSound { get; init; } = new(); // 0x618..0x650
    public WeaponTailFlags TailFlags { get; init; } = new();          // 0x654..0x683
}
