namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponTurnSpeedAndRangeFields
{
    public float MinVerticalTurnSpeed { get; init; }                              // 0x540
    public float MinHorizontalTurnSpeed { get; init; }                            // 0x544
    public float MaxVerticalTurnSpeed { get; init; }                              // 0x548
    public float MaxHorizontalTurnSpeed { get; init; }                            // 0x54C
    public float PitchConvergenceTime { get; init; }                              // 0x550
    public float YawConvergenceTime { get; init; }                                // 0x554
    public float SuppressionTime { get; init; }                                   // 0x558
    public float MaxRange { get; init; }                                          // 0x55C
    public float AnimationHorizontalRotateIncrement { get; init; }                // 0x560
    public float PlayerPositionDistance { get; init; }                            // 0x564
}
