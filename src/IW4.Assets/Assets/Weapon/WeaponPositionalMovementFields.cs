using IW4.Assets.Math;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponPositionalMovementFields
{
    public float PositionMoveRate { get; init; }                                   // 0x1B0
    public float PositionProneMoveRate { get; init; }                              // 0x1B4
    public float StandMoveMinSpeed { get; init; }                                  // 0x1B8
    public float DuckedMoveMinSpeed { get; init; }                                 // 0x1BC
    public float ProneMoveMinSpeed { get; init; }                                  // 0x1C0
    public float PositionRotationRate { get; init; }                               // 0x1C4
    public float PositionProneRotationRate { get; init; }                          // 0x1C8
    public float StandRotationMinSpeed { get; init; }                              // 0x1CC
    public float DuckedRotationMinSpeed { get; init; }                             // 0x1D0
    public float ProneRotationMinSpeed { get; init; }                              // 0x1D4
}
