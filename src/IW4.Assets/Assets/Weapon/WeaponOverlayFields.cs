using IW4.Assets.Math;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponOverlayFields
{
    public IReadOnlyList<XPointer<Material.MaterialAsset>> OverlayMaterials { get; init; } = []; // 0x308..0x314
    public WeaponOverlayReticle Reticle { get; init; }                            // 0x318
    public WeaponOverlayInterface Interface { get; init; }                        // 0x31C
    public int Width { get; init; }                                               // 0x320
    public int Height { get; init; }                                              // 0x324
    public int WidthSplitscreen { get; init; }                                    // 0x328
    public int HeightSplitscreen { get; init; }                                   // 0x32C
}
