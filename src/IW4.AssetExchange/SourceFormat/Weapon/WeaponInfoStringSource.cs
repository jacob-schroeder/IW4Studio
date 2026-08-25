using IW4.AssetExchange.SourceFormat.InfoString;
using IW4.Assets.Assets;
using IW4.Assets.Assets.Weapon;
using IW4.FastFiles.Strings;

namespace IW4.AssetExchange.SourceFormat.Weapon;

internal static partial class WeaponInfoStringSource
{
    private static readonly string[] WeaponTypeNames =
        ["bullet", "grenade", "projectile", "riotshield"];
    private static readonly string[] WeaponClassNames =
        ["rifle", "sniper", "mg", "smg", "spread", "pistol", "grenade",
            "rocketlauncher", "turret", "throwingknife", "non-player", "item"];
    private static readonly string[] PenetrateTypeNames =
        ["none", "small", "medium", "large"];
    private static readonly string[] ImpactTypeNames =
        ["none", "bullet_small", "bullet_large", "bullet_ap", "bullet_explode",
            "shotgun", "shotgun_explode", "grenade_bounce", "grenade_explode",
            "rocket_explode", "projectile_dud"];
    private static readonly string[] InventoryTypeNames =
        ["primary", "offhand", "item", "altmode", "exclusive", "scavenger"];
    private static readonly string[] FireTypeNames =
        ["Full Auto", "Single Shot", "2-Round Burst", "3-Round Burst",
            "4-Round Burst", "Double Barrel"];
    private static readonly string[] OffhandClassNames =
        ["None", "Frag Grenade", "Smoke Grenade", "Flash Grenade",
            "Throwing Knife", "Other"];
    private static readonly string[] PlayerAnimationTypeNames =
        ["none", "other", "pistol", "smg", "autorifle", "mg", "sniper",
            "rocketlauncher", "explosive", "grenade", "turret", "c4", "m203",
            "hold", "briefcase", "riotshield", "laptop", "throwingknife"];
    private static readonly string[] ActiveReticleNames =
        ["None", "Pip-On-A-Stick", "Bouncing diamond"];
    private static readonly string[] ProjectileExplosionNames =
        ["grenade", "rocket", "flashbang", "none", "dud", "smoke",
            "heavy explosive"];
    private static readonly string[] StickinessNames =
        ["Don't stick", "Stick to all", "Stick to all, orient to surface",
            "Stick to ground", "Stick to ground, maintain yaw", "Knife"];
    private static readonly string[] GuidedMissileNames =
        ["None", "Sidewinder", "Hellfire", "Javelin"];
    private static readonly string[] OverlayReticleNames = ["none", "crosshair"];
    private static readonly string[] OverlayInterfaceNames =
        ["None", "Javelin", "Turret Scope"];
    private static readonly string[] AmmoCounterClipNames =
        ["None", "Magazine", "ShortMagazine", "Shotgun", "Rocket", "Beltfed",
            "AltWeapon"];
    private static readonly string[] IconRatioNames = ["1:1", "2:1", "4:1"];

    private static readonly (string Key, WeaponAnimationSlot Slot)[] AnimationFields =
    [
        ("idleAnim", WeaponAnimationSlot.Idle),
        ("emptyIdleAnim", WeaponAnimationSlot.EmptyIdle),
        ("fireAnim", WeaponAnimationSlot.Fire),
        ("holdFireAnim", WeaponAnimationSlot.HoldFire),
        ("lastShotAnim", WeaponAnimationSlot.LastShot),
        ("detonateAnim", WeaponAnimationSlot.Detonate),
        ("rechamberAnim", WeaponAnimationSlot.Rechamber),
        ("meleeAnim", WeaponAnimationSlot.Melee),
        ("meleeChargeAnim", WeaponAnimationSlot.MeleeCharge),
        ("reloadAnim", WeaponAnimationSlot.Reload),
        ("reloadEmptyAnim", WeaponAnimationSlot.ReloadEmpty),
        ("reloadStartAnim", WeaponAnimationSlot.ReloadStart),
        ("reloadEndAnim", WeaponAnimationSlot.ReloadEnd),
        ("raiseAnim", WeaponAnimationSlot.Raise),
        ("dropAnim", WeaponAnimationSlot.Drop),
        ("firstRaiseAnim", WeaponAnimationSlot.FirstRaise),
        ("breachRaiseAnim", WeaponAnimationSlot.BreachRaise),
        ("altRaiseAnim", WeaponAnimationSlot.AltRaise),
        ("altDropAnim", WeaponAnimationSlot.AltDrop),
        ("quickRaiseAnim", WeaponAnimationSlot.QuickRaise),
        ("quickDropAnim", WeaponAnimationSlot.QuickDrop),
        ("emptyRaiseAnim", WeaponAnimationSlot.EmptyRaise),
        ("emptyDropAnim", WeaponAnimationSlot.EmptyDrop),
        ("sprintInAnim", WeaponAnimationSlot.SprintIn),
        ("sprintLoopAnim", WeaponAnimationSlot.SprintLoop),
        ("sprintOutAnim", WeaponAnimationSlot.SprintOut),
        ("stunnedAnimStart", WeaponAnimationSlot.StunnedStart),
        ("stunnedAnimLoop", WeaponAnimationSlot.StunnedLoop),
        ("stunnedAnimEnd", WeaponAnimationSlot.StunnedEnd),
        ("nightVisionWearAnim", WeaponAnimationSlot.NightVisionWear),
        ("nightVisionRemoveAnim", WeaponAnimationSlot.NightVisionRemove),
        ("adsFireAnim", WeaponAnimationSlot.AdsFire),
        ("adsLastShotAnim", WeaponAnimationSlot.AdsLastShot),
        ("adsRechamberAnim", WeaponAnimationSlot.AdsRechamber),
        ("adsUpAnim", WeaponAnimationSlot.AdsUp),
        ("adsDownAnim", WeaponAnimationSlot.AdsDown)
    ];

    private static readonly (string Key, WeaponPrimarySoundSlot Slot)[] PrimarySoundFields =
    [
        ("pickupSound", WeaponPrimarySoundSlot.PickupSound),
        ("pickupSoundPlayer", WeaponPrimarySoundSlot.PickupSoundPlayer),
        ("ammoPickupSound", WeaponPrimarySoundSlot.AmmoPickupSound),
        ("ammoPickupSoundPlayer", WeaponPrimarySoundSlot.AmmoPickupSoundPlayer),
        ("projectileSound", WeaponPrimarySoundSlot.ProjectileSound),
        ("pullbackSound", WeaponPrimarySoundSlot.PullbackSound),
        ("pullbackSoundPlayer", WeaponPrimarySoundSlot.PullbackSoundPlayer),
        ("fireSound", WeaponPrimarySoundSlot.FireSound),
        ("fireSoundPlayer", WeaponPrimarySoundSlot.FireSoundPlayer),
        ("fireSoundPlayerAkimbo", WeaponPrimarySoundSlot.FireSoundPlayerAkimbo),
        ("loopFireSound", WeaponPrimarySoundSlot.FireLoopSound),
        ("loopFireSoundPlayer", WeaponPrimarySoundSlot.FireLoopSoundPlayer),
        ("stopFireSound", WeaponPrimarySoundSlot.FireStopSound),
        ("stopFireSoundPlayer", WeaponPrimarySoundSlot.FireStopSoundPlayer),
        ("lastShotSound", WeaponPrimarySoundSlot.FireLastSound),
        ("lastShotSoundPlayer", WeaponPrimarySoundSlot.FireLastSoundPlayer),
        ("emptyFireSound", WeaponPrimarySoundSlot.EmptyFireSound),
        ("emptyFireSoundPlayer", WeaponPrimarySoundSlot.EmptyFireSoundPlayer),
        ("meleeSwipeSound", WeaponPrimarySoundSlot.MeleeSwipeSound),
        ("meleeSwipeSoundPlayer", WeaponPrimarySoundSlot.MeleeSwipeSoundPlayer),
        ("meleeHitSound", WeaponPrimarySoundSlot.MeleeHitSound),
        ("meleeMissSound", WeaponPrimarySoundSlot.MeleeMissSound),
        ("rechamberSound", WeaponPrimarySoundSlot.RechamberSound),
        ("rechamberSoundPlayer", WeaponPrimarySoundSlot.RechamberSoundPlayer),
        ("reloadSound", WeaponPrimarySoundSlot.ReloadSound),
        ("reloadSoundPlayer", WeaponPrimarySoundSlot.ReloadSoundPlayer),
        ("reloadEmptySound", WeaponPrimarySoundSlot.ReloadEmptySound),
        ("reloadEmptySoundPlayer", WeaponPrimarySoundSlot.ReloadEmptySoundPlayer),
        ("reloadStartSound", WeaponPrimarySoundSlot.ReloadStartSound),
        ("reloadStartSoundPlayer", WeaponPrimarySoundSlot.ReloadStartSoundPlayer),
        ("reloadEndSound", WeaponPrimarySoundSlot.ReloadEndSound),
        ("reloadEndSoundPlayer", WeaponPrimarySoundSlot.ReloadEndSoundPlayer),
        ("detonateSound", WeaponPrimarySoundSlot.DetonateSound),
        ("detonateSoundPlayer", WeaponPrimarySoundSlot.DetonateSoundPlayer),
        ("nightVisionWearSound", WeaponPrimarySoundSlot.NightVisionWearSound),
        ("nightVisionWearSoundPlayer", WeaponPrimarySoundSlot.NightVisionWearSoundPlayer),
        ("nightVisionRemoveSound", WeaponPrimarySoundSlot.NightVisionRemoveSound),
        ("nightVisionRemoveSoundPlayer", WeaponPrimarySoundSlot.NightVisionRemoveSoundPlayer),
        ("raiseSound", WeaponPrimarySoundSlot.RaiseSound),
        ("raiseSoundPlayer", WeaponPrimarySoundSlot.RaiseSoundPlayer),
        ("firstRaiseSound", WeaponPrimarySoundSlot.FirstRaiseSound),
        ("firstRaiseSoundPlayer", WeaponPrimarySoundSlot.FirstRaiseSoundPlayer),
        ("altSwitchSound", WeaponPrimarySoundSlot.AltSwitchSound),
        ("altSwitchSoundPlayer", WeaponPrimarySoundSlot.AltSwitchSoundPlayer),
        ("putawaySound", WeaponPrimarySoundSlot.PutawaySound),
        ("putawaySoundPlayer", WeaponPrimarySoundSlot.PutawaySoundPlayer),
        ("scanSound", WeaponPrimarySoundSlot.ScanSound)
    ];

    private static readonly string[] SurfaceNames =
        ["Default", "Bark", "Brick", "Carpet", "Cloth", "Concrete", "Dirt",
            "Flesh", "Foliage", "Glass", "Grass", "Gravel", "Ice", "Metal",
            "Mud", "Paper", "Plaster", "Rock", "Sand", "Snow", "Water",
            "Wood", "Asphalt", "Ceramic", "Plastic", "Rubber", "Cushion",
            "Fruit", "PaintedMetal", "RiotShield", "Slush"];

    private static readonly string[] BounceSoundSuffixes =
        ["_default", "_bark", "_brick", "_carpet", "_cloth", "_concrete",
            "_dirt", "_flesh", "_foliage", "_glass", "_grass", "_gravel",
            "_ice", "_metal", "_mud", "_paper", "_plaster", "_rock",
            "_sand", "_snow", "_water", "_wood", "_asphalt", "_ceramic",
            "_plastic", "_rubber", "_cushion", "_fruit", "_paintedmetal",
            "_riotshield", "_slush"];

    private static readonly (string Key, HitLocation Location)[] HitLocationFields =
    [
        ("locNone", HitLocation.None),
        ("locHelmet", HitLocation.Helmet),
        ("locHead", HitLocation.Head),
        ("locNeck", HitLocation.Neck),
        ("locTorsoUpper", HitLocation.UpperTorso),
        ("locTorsoLower", HitLocation.LowerTorso),
        ("locRightArmUpper", HitLocation.RightUpperArm),
        ("locRightArmLower", HitLocation.RightLowerArm),
        ("locRightHand", HitLocation.RightHand),
        ("locLeftArmUpper", HitLocation.LeftUpperArm),
        ("locLeftArmLower", HitLocation.LeftLowerArm),
        ("locLeftHand", HitLocation.LeftHand),
        ("locRightLegUpper", HitLocation.RightUpperLeg),
        ("locRightLegLower", HitLocation.RightLowerLeg),
        ("locRightFoot", HitLocation.RightFoot),
        ("locLeftLegUpper", HitLocation.LeftUpperLeg),
        ("locLeftLegLower", HitLocation.LeftLowerLeg),
        ("locLeftFoot", HitLocation.LeftFoot),
        ("locGun", HitLocation.Gun)
    ];

    public static InfoStringSourceWriter Create(WeaponAsset asset, string assetName)
    {
        WeaponVariantDef variant = asset.Variant;
        WeaponDef definition = variant.Definition ?? throw new InvalidDataException(
            $"Weapon '{assetName}' has no materialized WeaponDef body.");
        var source = new InfoStringSourceWriter("WEAPONFILE");

        AddIdentityAndAnimations(source, variant, definition, assetName);
        AddEffectsSoundsAndMovement(source, variant, definition, assetName);
        AddAmmoTimingAndFlags(source, variant, definition, assetName);
        AddProjectileAndAim(source, variant, definition, assetName);
        AddAccuracyAndTurret(source, variant, definition, assetName);
        return source;
    }

    private static string Materialized(int pointerRaw, string? value, string field) =>
        InfoStringSourceWriter.MaterializedString(pointerRaw, value, field);

    private static string Referenced(int pointerRaw, BaseAsset? asset, string field) =>
        InfoStringSourceWriter.ReferencedAssetName(
            pointerRaw,
            asset?.SerializedAssetName,
            field);

    private static string ReferencedName(int pointerRaw, string? name, string field) =>
        InfoStringSourceWriter.ReferencedAssetName(pointerRaw, name, field);

    private static string Sound(WeaponSoundAliasField sound, string field) =>
        Materialized(Presence(sound.Pointer.Raw, sound.ValuePointer.Raw),
            sound.Name, field);

    private static int Presence(int first, int second) =>
        first != 0 || second != 0 ? 1 : 0;

    private static void RequireList(
        int pointerRaw,
        int actualCount,
        int expectedCount,
        string field) =>
        InfoStringSourceWriter.RequireFixedPayload(
            pointerRaw,
            actualCount,
            expectedCount,
            field);

    private static string ScriptString(
        ScriptStringReference reference,
        string field) =>
        InfoStringSourceWriter.ScriptStringText(reference, field);
}
