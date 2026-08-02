using IW4.Assets.Math;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponTurnSpeedAndRangeFields
{
    public float MinTurnSpeed { get; init; }                                      // 0x540
    public float MaxTurnSpeed { get; init; }                                      // 0x544
    public float PitchConvergenceTime { get; init; }                              // 0x548
    public float YawConvergenceTime { get; init; }                                // 0x54C
    public float SuppressTime { get; init; }                                      // 0x550
    public float MaxRange { get; init; }                                          // 0x554
    public float AnimationHorizontalRotateIncrement { get; init; }                // 0x558
    public float PlayerPositionDistance { get; init; }                            // 0x55C
    public float ScanSpeed { get; init; }                                         // 0x560
    public float ScanAcceleration { get; init; }                                  // 0x564
}
