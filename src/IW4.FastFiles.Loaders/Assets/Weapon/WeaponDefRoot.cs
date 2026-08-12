using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Weapon;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using FxEffectDefAsset = IW4.Assets.Assets.Fx.FxEffectDefAsset;
using TracerDefAsset = IW4.Assets.Assets.Tracer.TracerDefAsset;
using XModelAsset = IW4.Assets.Assets.XModel.XModelAsset;
using PhysCollmapAsset = IW4.Assets.Assets.Physics.PhysCollmapAsset;
using XString = IW4.FastFiles.Pointers.XPointer<string>;

namespace IW4.FastFiles.Loaders.Assets.Weapon;

internal sealed class WeaponDefRoot
{
    public int Offset { get; init; }
    public XBlockAddress Address { get; init; }
    public XString InternalNamePointer { get; set; }
    public XPointer<XPointer<XModelAsset>[]> GunModelsPointer { get; set; }
    public XPointer<XModelAsset> HandModelPointer { get; set; }
    public XPointer<XString[]> RightHandAnimationNamesPointer { get; set; }
    public XPointer<XString[]> LeftHandAnimationNamesPointer { get; set; }
    public XString ModeNamePointer { get; set; }
    public WeaponNoteTrackMapPointers NoteTrackMaps { get; set; }
    public int PlayerAnimType { get; set; }
    public WeaponType WeaponType { get; set; }
    public WeaponClass WeaponClass { get; set; }
    public PenetrateType PenetrateType { get; set; }
    public WeaponInventoryType InventoryType { get; set; }
    public WeaponFireType FireType { get; set; }
    public OffhandClass OffhandClass { get; set; }
    public WeaponStance Stance { get; set; }
    public IReadOnlyList<XPointer<FxEffectDefAsset>> FlashEffectPointers { get; set; } = [];
    public XPointer<XString[]> SoundAliasPointersPointer { get; set; }
    public IReadOnlyList<XString> SoundAliasPointers { get; set; } = [];
    public XPointer<XString[]> BounceSoundPointer { get; set; }
    public IReadOnlyList<XPointer<FxEffectDefAsset>> EffectPointers { get; set; } = [];
    public IReadOnlyList<XPointer<MaterialAsset>> MaterialPointers { get; set; } = [];
    public WeaponReticleFields Reticle { get; set; } = new();
    public WeaponViewMovementFields ViewMovement { get; set; } = new();
    public WeaponPositionalMovementFields PositionalMovement { get; set; } = new();
    public XPointer<XPointer<XModelAsset>[]> WorldGunModelsPointer { get; set; }
    public IReadOnlyList<XPointer<XModelAsset>> WorldModelPointers { get; set; } = [];
    public WeaponIconPointers Icons { get; set; } = new();
    public WeaponAmmoFields Ammo { get; set; } = new();
    public WeaponTimingFields Timing { get; set; } = new();
    public WeaponAimMovementTuningFields AimMovementTuning { get; set; } = new();
    public WeaponOverlayFields Overlay { get; set; } = new();
    public WeaponAdsViewAndSpreadFields AdsViewAndSpread { get; set; } = new();
    public XPointer<PhysCollmapAsset> PhysCollmapPointer { get; set; }
    public WeaponPhysicsFields Physics { get; set; } = new();
    public XPointer<XModelAsset> ProjectileModelPointer { get; set; }
    public int ProjectileModelField { get; set; }
    public IReadOnlyList<XPointer<FxEffectDefAsset>> ProjectileEffectPointers { get; set; } = [];
    public XPointer<XString[]> ProjectileSoundAliasPointersPointer { get; set; }
    public IReadOnlyList<XString> ProjectileSoundAliasPointers { get; set; } = [];
    public IReadOnlyList<int> ProjectileFieldsA { get; set; } = [];
    public XPointer<float[]> ParallelBouncePointer { get; set; }
    public XPointer<float[]> PerpendicularBouncePointer { get; set; }
    public IReadOnlyList<XPointer<FxEffectDefAsset>> ImpactEffectPointers { get; set; } = [];
    public IReadOnlyList<int> ImpactFieldsA { get; set; } = [];
    public int ImpactFieldB { get; set; }
    public IReadOnlyList<int> ImpactFieldsC { get; set; } = [];
    public XPointer<FxEffectDefAsset> ViewShellEjectEffectPointer { get; set; }
    public XString ShellEjectSoundPointer { get; set; }
    public IReadOnlyList<int> ShellEjectFields { get; set; } = [];
    public IReadOnlyList<int> AdsHipGunKickAiDistanceFields { get; set; } = [];
    public XString AccuracyGraphName0Pointer { get; set; }
    public XString AccuracyGraphName1Pointer { get; set; }
    public XPointer<Vec2[]> AccuracyGraphKnotsPointer { get; set; }
    public XPointer<Vec2[]> OriginalAccuracyGraphKnotsPointer { get; set; }
    public ushort LocalGraphKnotCount { get; set; }
    public ushort LocalOriginalGraphKnotCount { get; set; }
    public int AnimationNotifyComparison { get; set; }
    public float LeftArc { get; set; }
    public float RightArc { get; set; }
    public float TopArc { get; set; }
    public float BottomArc { get; set; }
    public float Accuracy { get; set; }
    public float AiSpread { get; set; }
    public float PlayerSpread { get; set; }
    public WeaponTurnSpeedAndRangeFields TurnSpeedAndRange { get; set; } = new();
    public XString UseHintStringPointer { get; set; }
    public XString DropHintStringPointer { get; set; }
    public int UseHintStringIndex { get; set; }
    public int DropHintStringIndex { get; set; }
    public float HorizontalViewJitter { get; set; }
    public float VerticalViewJitter { get; set; }
    public float ScanSpeed { get; set; }
    public float ScanAcceleration { get; set; }
    public int ScanPauseTime { get; set; }
    public XString ScriptNamePointer { get; set; }
    public float OOPosAnimLength { get; set; }
    public float MinDamage { get; set; }
    public int MinPlayerDamage { get; set; }
    public float MaxDamageRange { get; set; }
    public float MinDamageRange { get; set; }
    public float DestabilizationRateTime { get; set; }
    public float DestabilizationCurvatureMax { get; set; }
    public float DestabilizeDistance { get; set; }
    public int DestabilizeDistanceToTimeScale { get; set; }
    public XPointer<float[]> LocationDamageMultipliersPointer { get; set; }
    public XString FireRumblePointer { get; set; }
    public XString MeleeImpactRumblePointer { get; set; }
    public XPointer<TracerDefAsset> TracerPointer { get; set; }
    public float TurretScopeZoomRate { get; set; }
    public float TurretScopeZoomMin { get; set; }
    public float TurretScopeZoomMax { get; set; }
    public float TurretOverheatUpRate { get; set; }
    public float TurretOverheatDownRate { get; set; }
    public float TurretOverheatPenalty { get; set; }
    public XString TurretOverheatSoundPointer { get; set; }
    public XPointer<FxEffectDefAsset> TurretOverheatEffectPointer { get; set; }
    public XString TurretBarrelSpinRumblePointer { get; set; }
    public float TurretBarrelSpinSpeed { get; set; }
    public float TurretBarrelSpinUpTime { get; set; }
    public float TurretBarrelSpinDownTime { get; set; }
    public XString TurretBarrelSpinMaxSoundPointer { get; set; }
    public IReadOnlyList<XString> TurretBarrelSpinUpSoundPointers { get; set; } = [];
    public IReadOnlyList<XString> TurretBarrelSpinDownSoundPointers { get; set; } = [];
    public XString MissileConeSoundAliasPointer { get; set; }
    public XString MissileConeSoundAliasAtBasePointer { get; set; }
    public float[] MissileConeFloats { get; set; } = [];
    public WeaponTailFlags TailFlags { get; set; } = new();
}
