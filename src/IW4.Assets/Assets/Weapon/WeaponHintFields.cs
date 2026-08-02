using IW4.Assets.Math;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponHintFields
{
    // 0x568: use-hint XString.
    public XString UseHintStringPointer { get; init; }
    public string? UseHintString { get; init; }

    // 0x56C..0x574: drop-hint string and hint indices.
    public XString DropHintStringPointer { get; init; }
    public string? DropHintString { get; init; }
    public int UseHintStringIndex { get; init; }                                  // 0x570
    public int DropHintStringIndex { get; init; }                                 // 0x574
    public float HorizontalViewJitter { get; init; }                              // 0x578
    public float VerticalViewJitter { get; init; }                                // 0x57C
    public float ScanSpeed { get; init; }                                         // 0x580
    public float ScanAcceleration { get; init; }                                  // 0x584
    public int ScanPauseTime { get; init; }                                       // 0x588
}
