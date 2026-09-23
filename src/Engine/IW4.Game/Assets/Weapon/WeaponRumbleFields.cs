using IW4.Game.Math;
using IW4.Game.Pointers;

namespace IW4.Game.Assets.Weapon;

public sealed class WeaponRumbleFields
{
    // 0x5B8 / 0x5BC: fire and melee-impact rumble XStrings.
    public XString FireRumblePointer { get; init; }
    public string? FireRumble { get; init; }
    public XString MeleeImpactRumblePointer { get; init; }
    public string? MeleeImpactRumble { get; init; }
}
