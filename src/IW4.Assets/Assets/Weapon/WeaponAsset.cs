using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponAsset : BaseAsset
{
    public const int SerializedSize = WeaponVariantDef.SerializedSize;

    // DB_AddXAsset copy window. This is distinct from the 0x74-byte
    // WeaponVariantDef root and is not a second serialized WeaponDef payload.
    public const int NativePoolCopySize = 0x684;
    public override XAssetType SerializedAssetType => XAssetType.Weapon;

    public WeaponVariantDef Variant { get; init; } = new();

    public string? Name => Variant.InternalName;
    public override string? SerializedAssetName => Name;
    public string? DisplayName => Variant.DisplayName;
    public WeaponDef? Definition => Variant.Definition;
}
