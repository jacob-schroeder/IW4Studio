using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponOverlayFields
{
    public XPointer<Material.MaterialAsset> MaterialPointer { get; init; }         // 0x308
    public Material.MaterialAsset? Material { get; init; }
    public XPointer<Material.MaterialAsset> MaterialLowResPointer { get; init; }   // 0x30C
    public Material.MaterialAsset? MaterialLowRes { get; init; }
    public XPointer<Material.MaterialAsset> MaterialEmpPointer { get; init; }      // 0x310
    public Material.MaterialAsset? MaterialEmp { get; init; }
    public XPointer<Material.MaterialAsset> MaterialEmpLowResPointer { get; init; }// 0x314
    public Material.MaterialAsset? MaterialEmpLowRes { get; init; }
    public WeaponOverlayReticle Reticle { get; init; }                            // 0x318
    public WeaponOverlayInterface Interface { get; init; }                        // 0x31C
    public float Width { get; init; }                                             // 0x320
    public float Height { get; init; }                                            // 0x324
    public float WidthSplitscreen { get; init; }                                  // 0x328
    public float HeightSplitscreen { get; init; }                                 // 0x32C
}
