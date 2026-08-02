using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using FxEffectDefAsset = IW4.Assets.Assets.Fx.FxEffectDefAsset;

namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponTurretFields
{
    public XString OverheatSoundPointer { get; init; }                            // 0x5DC
    public string? OverheatSound { get; init; }
    public XPointer<FxEffectDefAsset> OverheatEffectPointer { get; init; }        // 0x5E0
    public XString BarrelSpinRumblePointer { get; init; }                         // 0x5E4
    public string? BarrelSpinRumble { get; init; }
    public float BarrelSpinSpeed { get; init; }                                   // 0x5E8
    public float BarrelSpinUpTime { get; init; }                                  // 0x5EC
    public float BarrelSpinDownTime { get; init; }                                // 0x5F0
    public XString BarrelSpinMaxSoundPointer { get; init; }                       // 0x5F4
    public string? BarrelSpinMaxSound { get; init; }
    public IReadOnlyList<XString> BarrelSpinUpSoundPointers { get; init; } = [];  // 0x5F8..0x604, count 4
    public IReadOnlyList<string?> BarrelSpinUpSoundNames { get; init; } = [];
    public IReadOnlyList<XString> BarrelSpinDownSoundPointers { get; init; } = [];// 0x608..0x614, count 4
    public IReadOnlyList<string?> BarrelSpinDownSoundNames { get; init; } = [];
}
