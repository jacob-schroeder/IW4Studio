using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponReticleFields
{
    public XPointer<Material.MaterialAsset> CenterMaterialPointer { get; init; }   // 0x120
    public Material.MaterialAsset? CenterMaterial { get; init; }
    public XPointer<Material.MaterialAsset> SideMaterialPointer { get; init; }     // 0x124
    public Material.MaterialAsset? SideMaterial { get; init; }
    public int CenterSize { get; init; }                                           // 0x128
    public int SideSize { get; init; }                                             // 0x12C
    public int MinOffset { get; init; }                                            // 0x130
    public ActiveReticleType ActiveType { get; init; }                             // 0x134
}
