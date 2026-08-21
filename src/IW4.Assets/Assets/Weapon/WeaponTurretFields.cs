using IW4.FastFiles.Pointers;
using FxEffectDefAsset = IW4.Assets.Assets.Fx.FxEffectDefAsset;

namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponTurretFields
{
    public XString OverheatSoundPointer { get; init; }                            // 0x5DC
    public XString OverheatSoundValuePointer { get; init; }
    public string? OverheatSound { get; init; }
    public XPointer<FxEffectDefAsset> OverheatEffectPointer { get; init; }        // 0x5E0
    public FxEffectDefAsset? OverheatEffect { get; init; }
    public XString BarrelSpinRumblePointer { get; init; }                         // 0x5E4
    public string? BarrelSpinRumble { get; init; }
    public float BarrelSpinSpeed { get; init; }                                   // 0x5E8
    public float BarrelSpinUpTime { get; init; }                                  // 0x5EC
    public float BarrelSpinDownTime { get; init; }                                // 0x5F0
    public XString BarrelSpinMaxSoundPointer { get; init; }                       // 0x5F4
    public XString BarrelSpinMaxSoundValuePointer { get; init; }
    public string? BarrelSpinMaxSound { get; init; }
    public IReadOnlyList<WeaponSoundAliasField> BarrelSpinUpSounds { get; init; } = [];   // 0x5F8..0x604
    public IReadOnlyList<WeaponSoundAliasField> BarrelSpinDownSounds { get; init; } = []; // 0x608..0x614
}
