using IW4.FastFiles.Pointers;
using IW4.Assets.Assets.Material;
using IW4.FastFiles.Strings;

namespace IW4.Assets.Assets.Weapon;

public sealed class WeaponVariantDef
{
    public const int SerializedSize = 0x74;
    public const int HideTagCount = 32;
    public const int WeaponAnimCount = 37;

    public int Offset { get; init; }

    // 0x00: XString.
    public XString InternalNamePointer { get; init; }
    public string? InternalName { get; init; }

    // 0x04: direct WeaponDef pointer.
    public XPointer<WeaponDef> DefinitionPointer { get; init; }
    public WeaponDef? Definition { get; init; }

    // 0x08: XString.
    public XString DisplayNamePointer { get; init; }
    public string? DisplayName { get; init; }

    // 0x0C: direct ScriptString[32].
    public XPointer<ushort[]> HideTagsPointer { get; init; }
    public IReadOnlyList<ScriptStringReference> HideTags { get; init; } = [];

    // 0x10: direct XString pointer array, count 37.
    public XPointer<XString[]> AnimationNamesPointer { get; init; }
    public IReadOnlyList<XString> AnimationNamePointers { get; init; } = [];
    public IReadOnlyList<string?> AnimationNames { get; init; } = [];

    public float AdsZoomFov { get; init; }                  // 0x14
    public int AdsTransitionInTime { get; init; }           // 0x18
    public int AdsTransitionOutTime { get; init; }          // 0x1C
    public int ClipSize { get; init; }                      // 0x20
    public int ImpactType { get; init; }                    // 0x24
    public int FireTime { get; init; }                      // 0x28
    public int DpadIconRatio { get; init; }                 // 0x2C
    public float PenetrateMultiplier { get; init; }         // 0x30
    public float AdsViewKickCenterSpeed { get; init; }      // 0x34
    public float HipViewKickCenterSpeed { get; init; }      // 0x38

    // 0x3C: XString.
    public XString AlternateWeaponNamePointer { get; init; }
    public string? AlternateWeaponName { get; init; }

    public uint AlternateWeaponIndex { get; init; }         // 0x40
    public int AlternateRaiseTime { get; init; }            // 0x44

    // 0x48 / 0x4C: alias-cell Material pointers.
    public XPointer<Material.MaterialAsset> KillIconPointer { get; init; }
    public XPointer<Material.MaterialAsset> DpadIconPointer { get; init; }
    /// <summary>Materials resolved from the serialized icon pointers.</summary>
    public MaterialAsset? KillIcon { get; init; }
    public MaterialAsset? DpadIcon { get; init; }

    public int DropAmmoMin { get; init; }                   // 0x50
    public int FirstRaiseTime { get; init; }                // 0x54
    public int DropAmmoMax { get; init; }                   // 0x58
    public float AdsDofStart { get; init; }                 // 0x5C
    public float AdsDofEnd { get; init; }                   // 0x60

    // 0x64 / 0x66: these are the counts used by WeaponDef +0x514/+0x518 loaders.
    public ushort AccuracyGraphKnotCount { get; init; }
    public ushort OriginalAccuracyGraphKnotCount { get; init; }

    // 0x68 / 0x6C: direct Vec2 arrays.
    public XPointer<Math.Vec2[]> AccuracyGraphKnotsPointer { get; init; }
    public IReadOnlyList<Math.Vec2> AccuracyGraphKnots { get; init; } = [];
    public XPointer<Math.Vec2[]> OriginalAccuracyGraphKnotsPointer { get; init; }
    public IReadOnlyList<Math.Vec2> OriginalAccuracyGraphKnots { get; init; } = [];

    public byte MotionTracker { get; init; }                // 0x70
    public byte Enhanced { get; init; }                     // 0x71
    public byte DpadIconShowsAmmo { get; init; }            // 0x72
    public byte Padding73 { get; init; }                    // 0x73
}
