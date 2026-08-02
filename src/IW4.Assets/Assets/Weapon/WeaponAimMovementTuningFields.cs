using IW4.Assets.Math;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponAimMovementTuningFields
{
    public float AutoAimRange { get; init; }                                      // 0x2E0
    public float AimAssistRange { get; init; }                                    // 0x2E4
    public float AimAssistRangeAds { get; init; }                                 // 0x2E8
    public float AimPadding { get; init; }                                        // 0x2EC
    public float EnemyCrosshairRange { get; init; }                               // 0x2F0
    public float MoveSpeedScale { get; init; }                                    // 0x2F4
    public float AdsMoveSpeedScale { get; init; }                                 // 0x2F8
    public float SprintDurationScale { get; init; }                               // 0x2FC
    public float AdsZoomInFraction { get; init; }                                 // 0x300
    public float AdsZoomOutFraction { get; init; }                                // 0x304
}
