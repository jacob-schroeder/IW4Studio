using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Weapon;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.Vehicle;

public sealed class VehicleFakeBodyTuning
{
    public float AccelPitch { get; init; }
    public float AccelRoll { get; init; }
    public float VelPitch { get; init; }
    public float VelRoll { get; init; }
    public float SideVelPitch { get; init; }
    public float PitchStrength { get; init; }
    public float RollStrength { get; init; }
    public float PitchDampening { get; init; }
    public float RollDampening { get; init; }
    public float BoatRockingAmplitude { get; init; }
    public float BoatRockingPeriod { get; init; }
    public float BoatRockingRotationPeriod { get; init; }
    public float BoatRockingFadeoutSpeed { get; init; }
    public float BoatBouncingMinForce { get; init; }
    public float BoatBouncingMaxForce { get; init; }
    public float BoatBouncingRate { get; init; }
    public float BoatBouncingFadeinSpeed { get; init; }
    public float BoatBouncingFadeoutSteeringAngle { get; init; }
}
