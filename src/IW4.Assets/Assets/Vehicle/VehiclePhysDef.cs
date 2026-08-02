using IW4.Assets.Assets.Physics;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Vehicle;

public sealed class VehiclePhysDef
{
    public const int OffsetInVehicleDef = 0x0A8;
    public const int SerializedSize = 0xB4;

    public int PhysicsEnabled { get; init; }
    public XPointer<string> PhysPresetNamePointer { get; init; }
    public string? PhysPresetName { get; init; }
    public XPointer<PhysPresetAsset> PhysPresetPointer { get; init; }
    public PhysPresetAsset? PhysPreset { get; init; }
    public XPointer<string> AccelGraphNamePointer { get; init; }
    public string? AccelGraphName { get; init; }
    public VehicleAxleType SteeringAxle { get; init; }
    public VehicleAxleType PowerAxle { get; init; }
    public VehicleAxleType BrakingAxle { get; init; }
    public float TopSpeed { get; init; }
    public float ReverseSpeed { get; init; }
    public float MaxVelocity { get; init; }
    public float MaxPitch { get; init; }
    public float MaxRoll { get; init; }
    public float SuspensionTravelFront { get; init; }
    public float SuspensionTravelRear { get; init; }
    public float SuspensionStrengthFront { get; init; }
    public float SuspensionDampingFront { get; init; }
    public float SuspensionStrengthRear { get; init; }
    public float SuspensionDampingRear { get; init; }
    public float FrictionBraking { get; init; }
    public float FrictionCoasting { get; init; }
    public float FrictionTopSpeed { get; init; }
    public float FrictionSide { get; init; }
    public float FrictionSideRear { get; init; }
    public float VelocityDependentSlip { get; init; }
    public float RollStability { get; init; }
    public float RollResistance { get; init; }
    public float PitchResistance { get; init; }
    public float YawResistance { get; init; }
    public float UprightStrengthPitch { get; init; }
    public float UprightStrengthRoll { get; init; }
    public float TargetAirPitch { get; init; }
    public float AirYawTorque { get; init; }
    public float AirPitchTorque { get; init; }
    public float MinimumMomentumForCollision { get; init; }
    public float CollisionLaunchForceScale { get; init; }
    public float WreckedMassScale { get; init; }
    public float WreckedBodyFriction { get; init; }
    public float MinimumJoltForNotify { get; init; }
    public float SlipThresholdFront { get; init; }
    public float SlipThresholdRear { get; init; }
    public float SlipFricScaleFront { get; init; }
    public float SlipFricScaleRear { get; init; }
    public float SlipFricRateFront { get; init; }
    public float SlipFricRateRear { get; init; }
    public float SlipYawTorque { get; init; }
}
