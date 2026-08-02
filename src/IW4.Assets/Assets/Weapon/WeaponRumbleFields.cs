using IW4.Assets.Math;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponRumbleFields
{
    // 0x5B8 / 0x5BC: fire and melee-impact rumble XStrings.
    public XString FireRumblePointer { get; init; }
    public string? FireRumble { get; init; }
    public XString MeleeImpactRumblePointer { get; init; }
    public string? MeleeImpactRumble { get; init; }
}
