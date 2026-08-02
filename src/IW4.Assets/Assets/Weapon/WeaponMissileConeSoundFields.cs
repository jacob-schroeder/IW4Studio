using IW4.Assets.Math;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponMissileConeSoundFields
{
    public XString AliasPointer { get; init; }                                    // 0x618
    public string? Alias { get; init; }
    public XString AliasAtBasePointer { get; init; }                              // 0x61C
    public string? AliasAtBase { get; init; }

    // 0x620..0x650: missile-cone sound geometry and falloff parameters.
    public float RadiusAtTop { get; init; }
    public float RadiusAtBase { get; init; }
    public float Height { get; init; }
    public float OriginOffset { get; init; }
    public float VolumeScaleAtCore { get; init; }
    public float VolumeScaleAtEdge { get; init; }
    public float VolumeScaleCoreSize { get; init; }
    public float PitchAtTop { get; init; }
    public float PitchAtBottom { get; init; }
    public float PitchTopSize { get; init; }
    public float PitchBottomSize { get; init; }
    public float CrossfadeTopSize { get; init; }
    public float CrossfadeBottomSize { get; init; }
}
