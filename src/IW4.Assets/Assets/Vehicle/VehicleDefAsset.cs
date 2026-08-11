using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Weapon;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Strings;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.Vehicle;

public sealed class VehicleDefAsset : BaseAsset
{
    public const int SerializedSize = 0x2D0;
    public const int ScriptStringOffset = 0x1D0;
    public const int ScriptStringCount = 4;
    public const int SurfaceSoundOffset = 0x244;
    public const int SurfaceSoundCount = 31;
    public override XAssetType SerializedAssetType => XAssetType.Vehicle;

    public XPointer<string> NamePointer { get; init; }
    public string? Name { get; init; }
    public override string? SerializedAssetName => Name;
    public VehicleType Type { get; init; }
    public XPointer<string> UseHintStringPointer { get; init; }
    public string? UseHintString { get; init; }
    public int Health { get; init; }
    public int QuadBarrel { get; init; }
    public float TexScrollScale { get; init; }
    public float TopSpeed { get; init; }
    public float Accel { get; init; }
    public float RotRate { get; init; }
    public float RotAccel { get; init; }
    public float MaxBodyPitch { get; init; }
    public float MaxBodyRoll { get; init; }
    public VehicleFakeBodyTuning FakeBody { get; init; } = new();
    public float CollisionDamage { get; init; }
    public float CollisionSpeed { get; init; }
    public VehicleVec3 KillcamOffset { get; init; } = new();
    public int PlayerProtected { get; init; }
    public int BulletDamage { get; init; }
    public int ArmorPiercingDamage { get; init; }
    public int GrenadeDamage { get; init; }
    public int ProjectileDamage { get; init; }
    public int ProjectileSplashDamage { get; init; }
    public int HeavyExplosiveDamage { get; init; }
    public VehiclePhysDef Phys { get; init; } = new();
    public float BoostDuration { get; init; }
    public float BoostRechargeTime { get; init; }
    public float BoostAcceleration { get; init; }
    public float SuspensionTravel { get; init; }
    public float MaxSteeringAngle { get; init; }
    public float SteeringLerp { get; init; }
    public float MinSteeringScale { get; init; }
    public float MinSteeringSpeed { get; init; }
    public int CamLookEnabled { get; init; }
    public float CamLerp { get; init; }
    public float CamPitchInfluence { get; init; }
    public float CamRollInfluence { get; init; }
    public float CamFovIncrease { get; init; }
    public float CamFovOffset { get; init; }
    public float CamFovSpeed { get; init; }
    public XPointer<string> TurretWeaponNamePointer { get; init; }
    public string? TurretWeaponName { get; init; }
    public XPointer<WeaponAsset> TurretWeaponPointer { get; init; }
    public WeaponAsset? TurretWeapon { get; init; }
    public WeaponVariantDef? TurretWeaponVariant => TurretWeapon?.Variant;
    public float TurretHorizSpanLeft { get; init; }
    public float TurretHorizSpanRight { get; init; }
    public float TurretVertSpanUp { get; init; }
    public float TurretVertSpanDown { get; init; }
    public float TurretRotRate { get; init; }
    public VehicleSoundAliasField TurretSpinSound { get; init; } = VehicleSoundAliasField.Empty;
    public VehicleSoundAliasField TurretStopSound { get; init; } = VehicleSoundAliasField.Empty;
    public int TrophyEnabled { get; init; }
    public float TrophyRadius { get; init; }
    public float TrophyInactiveRadius { get; init; }
    public int TrophyAmmoCount { get; init; }
    public float TrophyReloadTime { get; init; }
    public XBlockAddress? ScriptStringsAddress { get; init; }
    public IReadOnlyList<ScriptStringReference> TrophyTags { get; init; } = [];
    public XPointer<MaterialAsset> CompassFriendlyIconPointer { get; init; }
    public MaterialAsset? CompassFriendlyIcon { get; init; }
    public XPointer<MaterialAsset> CompassEnemyIconPointer { get; init; }
    public MaterialAsset? CompassEnemyIcon { get; init; }
    public float CompassIconWidth { get; init; }
    public float CompassIconHeight { get; init; }
    public VehicleEngineSoundFields EngineSounds { get; init; } = new();
    public VehicleSuspensionSoundFields SuspensionSounds { get; init; } = new();
    public VehicleSoundAliasField CollisionSound { get; init; } = VehicleSoundAliasField.Empty;
    public float CollisionBlendSpeed { get; init; }
    public VehicleSoundAliasField SpeedSound { get; init; } = VehicleSoundAliasField.Empty;
    public float SpeedSoundBlendSpeed { get; init; }
    public XPointer<string> SurfaceSoundPrefixPointer { get; init; }
    public string? SurfaceSoundPrefix { get; init; }
    public IReadOnlyList<VehicleSoundAliasField> SurfaceSoundFields { get; init; } = [];
    public float SurfaceSoundBlendSpeed { get; init; }
    public float SlideVolume { get; init; }
    public float SlideBlendSpeed { get; init; }
    public float InAirPitch { get; init; }
}
