using System.Text.Json;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Physics;
using IW4.Assets.Assets.Vehicle;
using IW4.Assets.Assets.Weapon;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Assets;

namespace IW4.Studio.Documents;

/// <summary>Detached VehicleDef projection.  It intentionally converts every
/// runtime asset pointer to a symbolic reference and retains sound alias
/// nested-cell values as strings.</summary>
public sealed class VehicleAuthoredSnapshot : ITargetZoneDetachedSemanticSnapshot
{
    internal VehicleAuthoredSnapshot(VehicleBuildData data) => Data = data.Copy();
    internal VehicleBuildData Data { get; }
    public XAssetType AssetType => XAssetType.Vehicle;
    internal static VehicleAuthoredSnapshot Import(TargetZoneRowSource source) =>
        source.AuthoredDefinition?.SemanticSnapshot is VehicleAuthoredSnapshot snapshot
            ? snapshot : throw new InvalidDataException("Vehicle editing requires a capture-time detached semantic snapshot.");
    internal static VehicleAuthoredSnapshot FromLoaded(VehicleDefAsset asset) => new(VehicleBuildData.FromLoaded(asset));
}

public sealed class VehicleBuildData : IVehicleBuildData
{
    public XAssetType AssetType => XAssetType.Vehicle;
    public string? Name { get; init; }
    public int Type { get; init; }
    public string? UseHintString { get; init; }
    public int Health { get; init; }
    public int QuadBarrel { get; init; }
    public IReadOnlyList<float> MovementScalars { get; init; } = ZeroFloats(7);
    public VehicleFakeBodyBuildData FakeBody { get; init; }
    public float CollisionDamage { get; init; }
    public float CollisionSpeed { get; init; }
    public VehicleVec3BuildData KillcamOffset { get; init; }
    public IReadOnlyList<int> DamageValues { get; init; } = ZeroInts(7);
    public VehiclePhysicsBuildData Physics { get; init; } = new() { Scalars = ZeroFloats(38) };
    public IReadOnlyList<float> BoostAndSteeringScalars { get; init; } = ZeroFloats(8);
    public int CamLookEnabled { get; init; }
    public IReadOnlyList<float> CameraScalars { get; init; } = ZeroFloats(6);
    public string? TurretWeaponName { get; init; }
    public SymbolicXAssetReference? TurretWeaponReference { get; init; }
    public IReadOnlyList<float> TurretScalars { get; init; } = ZeroFloats(5);
    public string? TurretSpinSound { get; init; }
    public string? TurretStopSound { get; init; }
    public int TrophyEnabled { get; init; }
    public float TrophyRadius { get; init; }
    public float TrophyInactiveRadius { get; init; }
    public int TrophyAmmoCount { get; init; }
    public float TrophyReloadTime { get; init; }
    public IReadOnlyList<ushort> TrophyTags { get; init; } = ZeroUshorts(4);
    public SymbolicXAssetReference? CompassFriendlyIconReference { get; init; }
    public SymbolicXAssetReference? CompassEnemyIconReference { get; init; }
    public float CompassIconWidth { get; init; }
    public float CompassIconHeight { get; init; }
    public VehicleEngineSoundsBuildData EngineSounds { get; init; } = new();
    public VehicleSuspensionSoundsBuildData SuspensionSounds { get; init; } = new();
    public string? CollisionSound { get; init; }
    public float CollisionBlendSpeed { get; init; }
    public string? SpeedSound { get; init; }
    public float SpeedSoundBlendSpeed { get; init; }
    public string? SurfaceSoundPrefix { get; init; }
    public IReadOnlyList<string?> SurfaceSoundAliases { get; init; } = Enumerable.Repeat<string?>(null, 31).ToArray();
    public float SurfaceSoundBlendSpeed { get; init; }
    public float SlideVolume { get; init; }
    public float SlideBlendSpeed { get; init; }
    public float InAirPitch { get; init; }

    internal VehicleBuildData Copy() => new()
    {
        Name = Name, Type = Type, UseHintString = UseHintString, Health = Health, QuadBarrel = QuadBarrel,
        MovementScalars = MovementScalars.ToArray(), FakeBody = FakeBody, CollisionDamage = CollisionDamage, CollisionSpeed = CollisionSpeed, KillcamOffset = KillcamOffset,
        DamageValues = DamageValues.ToArray(), Physics = Copy(Physics), BoostAndSteeringScalars = BoostAndSteeringScalars.ToArray(), CamLookEnabled = CamLookEnabled, CameraScalars = CameraScalars.ToArray(),
        TurretWeaponName = TurretWeaponName, TurretWeaponReference = TurretWeaponReference, TurretScalars = TurretScalars.ToArray(), TurretSpinSound = TurretSpinSound, TurretStopSound = TurretStopSound,
        TrophyEnabled = TrophyEnabled, TrophyRadius = TrophyRadius, TrophyInactiveRadius = TrophyInactiveRadius, TrophyAmmoCount = TrophyAmmoCount, TrophyReloadTime = TrophyReloadTime, TrophyTags = TrophyTags.ToArray(),
        CompassFriendlyIconReference = CompassFriendlyIconReference, CompassEnemyIconReference = CompassEnemyIconReference, CompassIconWidth = CompassIconWidth, CompassIconHeight = CompassIconHeight,
        EngineSounds = Copy(EngineSounds), SuspensionSounds = Copy(SuspensionSounds), CollisionSound = CollisionSound, CollisionBlendSpeed = CollisionBlendSpeed, SpeedSound = SpeedSound, SpeedSoundBlendSpeed = SpeedSoundBlendSpeed,
        SurfaceSoundPrefix = SurfaceSoundPrefix, SurfaceSoundAliases = SurfaceSoundAliases.ToArray(), SurfaceSoundBlendSpeed = SurfaceSoundBlendSpeed, SlideVolume = SlideVolume, SlideBlendSpeed = SlideBlendSpeed, InAirPitch = InAirPitch
    };

    internal static VehicleBuildData FromLoaded(VehicleDefAsset value) => new()
    {
        Name = value.Name, Type = (int)value.Type, UseHintString = value.UseHintString, Health = value.Health, QuadBarrel = value.QuadBarrel,
        MovementScalars = [value.TexScrollScale, value.TopSpeed, value.Accel, value.RotRate, value.RotAccel, value.MaxBodyPitch, value.MaxBodyRoll],
        FakeBody = Fake(value.FakeBody), CollisionDamage = value.CollisionDamage, CollisionSpeed = value.CollisionSpeed, KillcamOffset = new(value.KillcamOffset.X, value.KillcamOffset.Y, value.KillcamOffset.Z),
        DamageValues = [value.PlayerProtected, value.BulletDamage, value.ArmorPiercingDamage, value.GrenadeDamage, value.ProjectileDamage, value.ProjectileSplashDamage, value.HeavyExplosiveDamage],
        Physics = PhysicsFromLoaded(value.Phys), BoostAndSteeringScalars = [value.BoostDuration, value.BoostRechargeTime, value.BoostAcceleration, value.SuspensionTravel, value.MaxSteeringAngle, value.SteeringLerp, value.MinSteeringScale, value.MinSteeringSpeed],
        CamLookEnabled = value.CamLookEnabled, CameraScalars = [value.CamLerp, value.CamPitchInfluence, value.CamRollInfluence, value.CamFovIncrease, value.CamFovOffset, value.CamFovSpeed],
        TurretWeaponName = value.TurretWeaponName, TurretWeaponReference = Reference(XAssetType.Weapon, value.TurretWeapon?.Name), TurretScalars = [value.TurretHorizSpanLeft, value.TurretHorizSpanRight, value.TurretVertSpanUp, value.TurretVertSpanDown, value.TurretRotRate],
        TurretSpinSound = value.TurretSpinSound.Value, TurretStopSound = value.TurretStopSound.Value, TrophyEnabled = value.TrophyEnabled, TrophyRadius = value.TrophyRadius, TrophyInactiveRadius = value.TrophyInactiveRadius, TrophyAmmoCount = value.TrophyAmmoCount, TrophyReloadTime = value.TrophyReloadTime, TrophyTags = value.TrophyTags.ToArray(),
        CompassFriendlyIconReference = Reference(XAssetType.Material, value.CompassFriendlyIcon?.Info.Name), CompassEnemyIconReference = Reference(XAssetType.Material, value.CompassEnemyIcon?.Info.Name), CompassIconWidth = value.CompassIconWidth, CompassIconHeight = value.CompassIconHeight,
        EngineSounds = Engine(value.EngineSounds), SuspensionSounds = Suspension(value.SuspensionSounds), CollisionSound = value.CollisionSound.Value, CollisionBlendSpeed = value.CollisionBlendSpeed, SpeedSound = value.SpeedSound.Value, SpeedSoundBlendSpeed = value.SpeedSoundBlendSpeed,
        SurfaceSoundPrefix = value.SurfaceSoundPrefix, SurfaceSoundAliases = value.SurfaceSoundAliases.ToArray(), SurfaceSoundBlendSpeed = value.SurfaceSoundBlendSpeed, SlideVolume = value.SlideVolume, SlideBlendSpeed = value.SlideBlendSpeed, InAirPitch = value.InAirPitch
    };

    private static VehicleFakeBodyBuildData Fake(VehicleFakeBodyTuning value) => new(value.AccelPitch, value.AccelRoll, value.VelPitch, value.VelRoll, value.SideVelPitch, value.PitchStrength, value.RollStrength, value.PitchDampening, value.RollDampening, value.BoatRockingAmplitude, value.BoatRockingPeriod, value.BoatRockingRotationPeriod, value.BoatRockingFadeoutSpeed, value.BoatBouncingMinForce, value.BoatBouncingMaxForce, value.BoatBouncingRate, value.BoatBouncingFadeinSpeed, value.BoatBouncingFadeoutSteeringAngle);
    private static VehiclePhysicsBuildData PhysicsFromLoaded(VehiclePhysDef value) => new()
    {
        PhysicsEnabled = value.PhysicsEnabled, PhysPresetName = value.PhysPresetName, PhysPresetReference = Reference(XAssetType.PhysPreset, value.PhysPreset?.Name), AccelGraphName = value.AccelGraphName,
        SteeringAxle = (int)value.SteeringAxle, PowerAxle = (int)value.PowerAxle, BrakingAxle = (int)value.BrakingAxle,
        Scalars = [value.TopSpeed, value.ReverseSpeed, value.MaxVelocity, value.MaxPitch, value.MaxRoll, value.SuspensionTravelFront, value.SuspensionTravelRear, value.SuspensionStrengthFront, value.SuspensionDampingFront, value.SuspensionStrengthRear, value.SuspensionDampingRear, value.FrictionBraking, value.FrictionCoasting, value.FrictionTopSpeed, value.FrictionSide, value.FrictionSideRear, value.VelocityDependentSlip, value.RollStability, value.RollResistance, value.PitchResistance, value.YawResistance, value.UprightStrengthPitch, value.UprightStrengthRoll, value.TargetAirPitch, value.AirYawTorque, value.AirPitchTorque, value.MinimumMomentumForCollision, value.CollisionLaunchForceScale, value.WreckedMassScale, value.WreckedBodyFriction, value.MinimumJoltForNotify, value.SlipThresholdFront, value.SlipThresholdRear, value.SlipFricScaleFront, value.SlipFricScaleRear, value.SlipFricRateFront, value.SlipFricRateRear, value.SlipYawTorque]
    };
    private static VehicleEngineSoundsBuildData Engine(VehicleEngineSoundFields value) => new() { IdleLow = value.IdleLowSound.Value, IdleHigh = value.IdleHighSound.Value, EngineLow = value.EngineLowSound.Value, EngineHigh = value.EngineHighSound.Value, EngineSoundSpeed = value.EngineSoundSpeed, EngineStartUp = value.EngineStartUpSound.Value, EngineStartUpLength = value.EngineStartUpLength, EngineShutdown = value.EngineShutdownSound.Value, EngineIdle = value.EngineIdleSound.Value, EngineSustain = value.EngineSustainSound.Value, EngineRampUp = value.EngineRampUpSound.Value, EngineRampUpLength = value.EngineRampUpLength, EngineRampDown = value.EngineRampDownSound.Value, EngineRampDownLength = value.EngineRampDownLength };
    private static VehicleSuspensionSoundsBuildData Suspension(VehicleSuspensionSoundFields value) => new() { Soft = value.SuspensionSoftSound.Value, SoftCompression = value.SuspensionSoftCompression, Hard = value.SuspensionHardSound.Value, HardCompression = value.SuspensionHardCompression };
    private static VehiclePhysicsBuildData Copy(VehiclePhysicsBuildData value) => new() { PhysicsEnabled = value.PhysicsEnabled, PhysPresetName = value.PhysPresetName, PhysPresetReference = value.PhysPresetReference, AccelGraphName = value.AccelGraphName, SteeringAxle = value.SteeringAxle, PowerAxle = value.PowerAxle, BrakingAxle = value.BrakingAxle, Scalars = value.Scalars.ToArray() };
    private static VehicleEngineSoundsBuildData Copy(VehicleEngineSoundsBuildData value) => new() { IdleLow = value.IdleLow, IdleHigh = value.IdleHigh, EngineLow = value.EngineLow, EngineHigh = value.EngineHigh, EngineSoundSpeed = value.EngineSoundSpeed, EngineStartUp = value.EngineStartUp, EngineStartUpLength = value.EngineStartUpLength, EngineShutdown = value.EngineShutdown, EngineIdle = value.EngineIdle, EngineSustain = value.EngineSustain, EngineRampUp = value.EngineRampUp, EngineRampUpLength = value.EngineRampUpLength, EngineRampDown = value.EngineRampDown, EngineRampDownLength = value.EngineRampDownLength };
    private static VehicleSuspensionSoundsBuildData Copy(VehicleSuspensionSoundsBuildData value) => new() { Soft = value.Soft, SoftCompression = value.SoftCompression, Hard = value.Hard, HardCompression = value.HardCompression };
    private static SymbolicXAssetReference? Reference(XAssetType type, string? name) => name is null ? null : new(type, name.StartsWith(",", StringComparison.Ordinal) ? name : $",{name}");
    private static float[] ZeroFloats(int count) => new float[count]; private static int[] ZeroInts(int count) => new int[count]; private static ushort[] ZeroUshorts(int count) => new ushort[count];
}

public sealed class VehicleDraft
{
    private VehicleBuildData _data;
    internal VehicleDraft(VehicleBuildData data) => _data = data.Copy();
    public VehicleBuildData Data => _data.Copy();
    public void Replace(VehicleBuildData value) { ArgumentNullException.ThrowIfNull(value); _data = value.Copy(); }
    internal VehicleDraft Clone() => new(_data);
}

public sealed class VehicleAuthoringAdapter : AssetAuthoringAdapter<VehicleAuthoredSnapshot, VehicleDraft, VehicleBuildData>
{
    private static readonly VehicleBodyEmitter Validator = new();
    public override XAssetType AssetType => XAssetType.Vehicle;
    public override VehicleAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => VehicleAuthoredSnapshot.Import(source);
    public override VehicleDraft CreateDraft(VehicleAuthoredSnapshot snapshot) => new(snapshot.Data);
    public override VehicleDraft CloneDraft(VehicleDraft draft) => draft.Clone();
    public override IReadOnlyList<AssetValidationIssue> ValidateDraft(VehicleDraft draft) => Validator.Validate(draft.Data).Select(value => new AssetValidationIssue(value.Path, value.Message, AssetValidationSeverity.Error)).ToArray();
    public override bool SemanticallyEquals(VehicleDraft left, VehicleDraft right) => JsonSerializer.Serialize(left.Data) == JsonSerializer.Serialize(right.Data);
    public override VehicleBuildData ExportBuildData(VehicleDraft draft) { VehicleBuildData data = draft.Data; if (Validator.Validate(data).Count != 0) throw new InvalidOperationException("Vehicle draft has validation errors and cannot produce build data."); return data; }
}
