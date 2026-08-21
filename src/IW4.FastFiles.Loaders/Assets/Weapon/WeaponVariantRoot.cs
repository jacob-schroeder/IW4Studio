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
    int FireAnimLength,
    int FirstRaiseTime,
    int AmmoDropStockMax,
    float AdsDofStart,
    float AdsDofEnd,
    ushort AiVsAiAccuracyGraphKnotCount,
    ushort AiVsPlayerAccuracyGraphKnotCount,
    XPointer<Vec2[]> AiVsAiAccuracyGraphKnotsPointer,
    XPointer<Vec2[]> AiVsPlayerAccuracyGraphKnotsPointer,
    byte MotionTracker,
    byte Enhanced,
    byte DpadIconShowsAmmo,
    byte Padding73);
