using IW4.Assets.Math;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponReticleFields
{
    public int CenterSize { get; init; }                                           // 0x128
    public int SideSize { get; init; }                                             // 0x12C
    public int MinOffset { get; init; }                                            // 0x130
    public ActiveReticleType ActiveType { get; init; }                             // 0x134
}
