using IW4.Assets.Math;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponIconPointers
{
    public XPointer<Material.MaterialAsset> HudIconPointer { get; init; }         // 0x1EC
    public int HudIconRatio { get; init; }                                        // 0x1F0
    public XPointer<Material.MaterialAsset> PickupIconPointer { get; init; }      // 0x1F4
    public int PickupIconRatio { get; init; }                                     // 0x1F8
    public XPointer<Material.MaterialAsset> AmmoCounterIconPointer { get; init; } // 0x1FC
    public int AmmoCounterIconRatio { get; init; }                                // 0x200
    public AmmoCounterClipType AmmoCounterClip { get; init; }                     // 0x204
    public int StartAmmo { get; init; }                                           // 0x208
}
