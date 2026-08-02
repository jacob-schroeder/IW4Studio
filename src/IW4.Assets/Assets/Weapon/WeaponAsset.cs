using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponAsset : BaseAsset
{
    public const int SerializedSize = WeaponVariantDef.SerializedSize;

    // DB_AddXAsset copy window. This is distinct from the 0x74-byte
    // WeaponVariantDef root and is not a second serialized WeaponDef payload.
    public const int NativePoolCopySize = 0x684;

    public WeaponVariantDef Variant { get; init; } = new();

    public string? Name => Variant.InternalName;
    public string? DisplayName => Variant.DisplayName;
    public WeaponDef? Definition => Variant.Definition;
}
