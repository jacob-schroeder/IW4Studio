using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Weapon;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using XString = IW4.FastFiles.Pointers.XPointer<string>;

namespace IW4.FastFiles.Loaders.Assets.Weapon;

internal sealed record WeaponVariantRoot(
    int Offset,
    XString InternalNamePointer,
    XPointer<WeaponDef> DefinitionPointer,
    XString DisplayNamePointer,
    XPointer<ushort[]> HideTagsPointer,
    XPointer<XString[]> AnimationNamesPointer,
    float AdsZoomFov,
    int AdsTransitionInTime,
    int AdsTransitionOutTime,
    int ClipSize,
    int ImpactType,
    int FireTime,
    int DpadIconRatio,
    float PenetrateMultiplier,
    float AdsViewKickCenterSpeed,
    float HipViewKickCenterSpeed,
    XString AlternateWeaponNamePointer,
    uint AlternateWeaponIndex,
    int AlternateRaiseTime,
    XPointer<MaterialAsset> KillIconPointer,
    XPointer<MaterialAsset> DpadIconPointer,
    int DropAmmoMin,
    int FirstRaiseTime,
    int DropAmmoMax,
    float AdsDofStart,
    float AdsDofEnd,
    ushort AccuracyGraphKnotCount,
    ushort OriginalAccuracyGraphKnotCount,
    XPointer<Vec2[]> AccuracyGraphKnotsPointer,
    XPointer<Vec2[]> OriginalAccuracyGraphKnotsPointer,
    byte MotionTracker,
    byte Enhanced,
    byte DpadIconShowsAmmo,
    byte Padding73);
