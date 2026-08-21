using IW4.Assets.Assets;
using IW4.Assets.Assets.Weapon;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Strings;
using XString = IW4.FastFiles.Pointers.XPointer<string>;

namespace IW4.Studio.Documents;

/// <summary>Detached, lossless editing state for one existing Weapon asset.</summary>
public sealed partial class WeaponDraft
{
    private WeaponAsset _asset;

    internal WeaponDraft(WeaponAsset source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(source.Variant);
        _asset = WeaponGraph.Copy(source);
        HasDefinition = source.Variant.Definition is not null;
    }

    private WeaponDraft(WeaponDraft source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _asset = WeaponGraph.Copy(source._asset);
        HasDefinition = source.HasDefinition;
    }

    public bool HasDefinition { get; private set; }
    public WeaponVariantDef Variant => _asset.Variant;
    public WeaponDef? Definition => _asset.Variant.Definition;

    internal WeaponDraft Clone() => new(this);
    /// <summary>Creates an independent detached copy for an editor-local baseline.</summary>
    public WeaponDraft Copy() => new(this);
    internal WeaponAsset ToAsset() => WeaponGraph.Copy(_asset);

    /// <summary>Replaces this detached candidate with another detached Weapon candidate.</summary>
    public void ReplaceWith(WeaponDraft source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _asset = WeaponGraph.Copy(source._asset);
        HasDefinition = source.HasDefinition;
    }

    /// <summary>
    /// Replaces all editable variant values while preserving identity, definition
    /// ownership, fixed collection shapes, and unchanged pointer provenance.
    /// </summary>
    public void SetVariantValues(WeaponVariantDef value)
    {
        ArgumentNullException.ThrowIfNull(value);
        WeaponVariantDef current = _asset.Variant;
        if (!StringEquals(current.InternalName, value.InternalName))
            throw new InvalidOperationException("Weapon identity is read-only.");
        EnsureSameCount(current.HideTags, value.HideTags, nameof(value));
        EnsureSameCount(current.AnimationNamePointers, value.AnimationNamePointers, nameof(value));
        EnsureSameCount(current.AnimationNames, value.AnimationNames, nameof(value));
        EnsureSameCount(current.AiVsAiAccuracyGraphKnots, value.AiVsAiAccuracyGraphKnots, nameof(value));
        EnsureSameCount(current.AiVsPlayerAccuracyGraphKnots, value.AiVsPlayerAccuracyGraphKnots, nameof(value));
        WeaponFiniteMutation.Ensure(current, value, nameof(value));

        _asset = new WeaponAsset
        {
            Variant = new WeaponVariantDef
            {
                Offset = current.Offset,
                InternalNamePointer = current.InternalNamePointer,
                InternalName = current.InternalName,
                DefinitionPointer = current.DefinitionPointer,
                Definition = current.Definition is null ? null : WeaponGraph.Copy(current.Definition),
                DisplayNamePointer = XStringIfUnchanged(current.DisplayName, value.DisplayName, current.DisplayNamePointer),
                DisplayName = value.DisplayName,
                HideTagsPointer = current.HideTagsPointer,
                HideTags = PreserveScriptStrings(current.HideTags, value.HideTags),
                AnimationNamesPointer = current.AnimationNamesPointer,
                AnimationNamePointers = PreserveXStringPointers(current.AnimationNames, value.AnimationNames, current.AnimationNamePointers),
                AnimationNames = CopyList(value.AnimationNames),
                AdsZoomFov = value.AdsZoomFov,
                AdsTransitionInTime = value.AdsTransitionInTime,
                AdsTransitionOutTime = value.AdsTransitionOutTime,
                ClipSize = value.ClipSize,
                ImpactType = value.ImpactType,
                FireTime = value.FireTime,
                DpadIconRatio = value.DpadIconRatio,
                PenetrateMultiplier = value.PenetrateMultiplier,
                AdsViewKickCenterSpeed = value.AdsViewKickCenterSpeed,
                HipViewKickCenterSpeed = value.HipViewKickCenterSpeed,
                AlternateWeaponNamePointer = XStringIfUnchanged(current.AlternateWeaponName, value.AlternateWeaponName, current.AlternateWeaponNamePointer),
                AlternateWeaponName = value.AlternateWeaponName,
                AlternateWeaponIndex = value.AlternateWeaponIndex,
                AlternateRaiseTime = value.AlternateRaiseTime,
                KillIconPointer = ProviderPointerIfUnchanged(current.KillIcon, value.KillIcon, current.KillIconPointer),
                DpadIconPointer = ProviderPointerIfUnchanged(current.DpadIcon, value.DpadIcon, current.DpadIconPointer),
                KillIcon = value.KillIcon,
                DpadIcon = value.DpadIcon,
                FireAnimLength = value.FireAnimLength,
                FirstRaiseTime = value.FirstRaiseTime,
                AmmoDropStockMax = value.AmmoDropStockMax,
                AdsDofStart = value.AdsDofStart,
                AdsDofEnd = value.AdsDofEnd,
                AiVsAiAccuracyGraphKnotCount = current.AiVsAiAccuracyGraphKnotCount,
                AiVsPlayerAccuracyGraphKnotCount = current.AiVsPlayerAccuracyGraphKnotCount,
                AiVsAiAccuracyGraphKnotsPointer = current.AiVsAiAccuracyGraphKnotsPointer,
                AiVsAiAccuracyGraphKnots = CopyList(value.AiVsAiAccuracyGraphKnots),
                AiVsPlayerAccuracyGraphKnotsPointer = current.AiVsPlayerAccuracyGraphKnotsPointer,
                AiVsPlayerAccuracyGraphKnots = CopyList(value.AiVsPlayerAccuracyGraphKnots),
                MotionTracker = value.MotionTracker,
                Enhanced = value.Enhanced,
                DpadIconShowsAmmo = value.DpadIconShowsAmmo,
                Padding73 = current.Padding73
            }
        };
    }

    /// <summary>
    /// Replaces all editable nested-definition values. Identity, padding, and fixed
    /// collection shapes cannot change. Only changed semantic cells shed provenance.
    /// </summary>
    public void SetDefinitionValues(WeaponDef value)
    {
        ArgumentNullException.ThrowIfNull(value);
        WeaponDef current = RequireDefinition();
        if (!StringEquals(current.InternalName, value.InternalName))
            throw new InvalidOperationException("Weapon definition identity is read-only.");
        EnsureDefinitionCollectionShapes(current, value);
        WeaponFiniteMutation.Ensure(current, value, nameof(value));

        WeaponDef replacement = new()
        {
            Offset = current.Offset,
            InternalNamePointer = current.InternalNamePointer,
            InternalName = current.InternalName,
            GunModelsPointer = current.GunModelsPointer,
            GunModelPointers = PreserveProviderPointers(current.GunModels, value.GunModels, current.GunModelPointers),
            GunModels = CopyList(value.GunModels),
            HandModelPointer = ProviderPointerIfUnchanged(current.HandModel, value.HandModel, current.HandModelPointer),
            HandModel = value.HandModel,
            RightHandAnimationNamesPointer = current.RightHandAnimationNamesPointer,
            RightHandAnimationNamePointers = PreserveXStringPointers(current.RightHandAnimationNames, value.RightHandAnimationNames, current.RightHandAnimationNamePointers),
            RightHandAnimationNames = CopyList(value.RightHandAnimationNames),
            LeftHandAnimationNamesPointer = current.LeftHandAnimationNamesPointer,
            LeftHandAnimationNamePointers = PreserveXStringPointers(current.LeftHandAnimationNames, value.LeftHandAnimationNames, current.LeftHandAnimationNamePointers),
            LeftHandAnimationNames = CopyList(value.LeftHandAnimationNames),
            ModeNamePointer = XStringIfUnchanged(current.ModeName, value.ModeName, current.ModeNamePointer),
            ModeName = value.ModeName,
            NoteTrackMaps = SanitizeNoteTracks(current.NoteTrackMaps, value.NoteTrackMaps),
            PlayerAnimType = value.PlayerAnimType,
            WeaponType = value.WeaponType,
            WeaponClass = value.WeaponClass,
            PenetrateType = value.PenetrateType,
            InventoryType = value.InventoryType,
            FireType = value.FireType,
            OffhandClass = value.OffhandClass,
            Stance = value.Stance,
            FlashEffects = SanitizeFlashEffects(current.FlashEffects, value.FlashEffects),
            PrimarySounds = SanitizePrimarySounds(current.PrimarySounds, value.PrimarySounds),
            BounceSoundPointer = current.BounceSoundPointer,
            BounceSounds = SanitizeSoundAliases(current.BounceSounds, value.BounceSounds),
            ShellEjectEffects = SanitizeShellEjectEffects(current.ShellEjectEffects, value.ShellEjectEffects),
            Reticle = SanitizeReticle(current.Reticle, value.Reticle),
            ViewMovement = WeaponGraph.Copy(value.ViewMovement),
            PositionalMovement = WeaponGraph.Copy(value.PositionalMovement),
            WorldGunModelsPointer = current.WorldGunModelsPointer,
            WorldGunModelPointers = PreserveProviderPointers(current.WorldGunModels, value.WorldGunModels, current.WorldGunModelPointers),
            WorldGunModels = CopyList(value.WorldGunModels),
            WorldClipModelPointer = ProviderPointerIfUnchanged(current.WorldClipModel, value.WorldClipModel, current.WorldClipModelPointer),
            WorldClipModel = value.WorldClipModel,
            RocketModelPointer = ProviderPointerIfUnchanged(current.RocketModel, value.RocketModel, current.RocketModelPointer),
            RocketModel = value.RocketModel,
            KnifeModelPointer = ProviderPointerIfUnchanged(current.KnifeModel, value.KnifeModel, current.KnifeModelPointer),
            KnifeModel = value.KnifeModel,
            WorldKnifeModelPointer = ProviderPointerIfUnchanged(current.WorldKnifeModel, value.WorldKnifeModel, current.WorldKnifeModelPointer),
            WorldKnifeModel = value.WorldKnifeModel,
            Icons = SanitizeIcons(current.Icons, value.Icons),
            Ammo = SanitizeAmmo(current.Ammo, value.Ammo),
            Overlay = SanitizeOverlay(current.Overlay, value.Overlay),
            Timing = WeaponGraph.Copy(value.Timing),
            AimMovementTuning = WeaponGraph.Copy(value.AimMovementTuning),
            AdsViewAndSpread = WeaponGraph.Copy(value.AdsViewAndSpread),
            PhysCollmapPointer = ProviderPointerIfUnchanged(current.PhysCollmap, value.PhysCollmap, current.PhysCollmapPointer),
            PhysCollmap = value.PhysCollmap,
            PhysCollmapName = value.PhysCollmapName,
            Physics = WeaponGraph.Copy(value.Physics),
            Projectile = SanitizeProjectile(current.Projectile, value.Projectile),
            Accuracy = SanitizeAccuracy(current.Accuracy, value.Accuracy),
            TurnSpeedAndRange = WeaponGraph.Copy(value.TurnSpeedAndRange),
            Hints = SanitizeHints(current.Hints, value.Hints),
            ScriptNamePointer = XStringIfUnchanged(current.ScriptName, value.ScriptName, current.ScriptNamePointer),
            ScriptName = value.ScriptName,
            AdsTransitionInRate = value.AdsTransitionInRate,
            AdsTransitionOutRate = value.AdsTransitionOutRate,
            MinDamage = value.MinDamage,
            MinPlayerDamage = value.MinPlayerDamage,
            MaxDamageRange = value.MaxDamageRange,
            MinDamageRange = value.MinDamageRange,
            DestabilizationRateTime = value.DestabilizationRateTime,
            DestabilizationCurvatureMax = value.DestabilizationCurvatureMax,
            DestabilizeDistance = value.DestabilizeDistance,
            LocationDamageMultipliersPointer = current.LocationDamageMultipliersPointer,
            LocationDamageMultipliers = CopyList(value.LocationDamageMultipliers),
            Rumble = SanitizeRumble(current.Rumble, value.Rumble),
            TracerPointer = ProviderPointerIfUnchanged(current.Tracer, value.Tracer, current.TracerPointer),
            Tracer = value.Tracer,
            TurretScopeZoomRate = value.TurretScopeZoomRate,
            TurretScopeZoomMin = value.TurretScopeZoomMin,
            TurretScopeZoomMax = value.TurretScopeZoomMax,
            TurretOverheatUpRate = value.TurretOverheatUpRate,
            TurretOverheatDownRate = value.TurretOverheatDownRate,
            TurretOverheatPenalty = value.TurretOverheatPenalty,
            Turret = SanitizeTurret(current.Turret, value.Turret),
            MissileConeSound = SanitizeMissile(current.MissileConeSound, value.MissileConeSound),
            TailFlags = PreserveTailPadding(current.TailFlags, value.TailFlags)
        };

        WeaponVariantDef variant = _asset.Variant;
        _asset = new WeaponAsset { Variant = CopyVariantWithDefinition(variant, replacement) };
    }

    private WeaponDef RequireDefinition() => Definition ??
        throw new InvalidOperationException("The Weapon has no nested definition.");

    private static WeaponVariantDef CopyVariantWithDefinition(WeaponVariantDef value, WeaponDef definition) => new()
    {
        Offset = value.Offset,
        InternalNamePointer = value.InternalNamePointer,
        InternalName = value.InternalName,
        DefinitionPointer = value.DefinitionPointer,
        Definition = definition,
        DisplayNamePointer = value.DisplayNamePointer,
        DisplayName = value.DisplayName,
        HideTagsPointer = value.HideTagsPointer,
        HideTags = CopyList(value.HideTags),
        AnimationNamesPointer = value.AnimationNamesPointer,
        AnimationNamePointers = CopyList(value.AnimationNamePointers),
        AnimationNames = CopyList(value.AnimationNames),
        AdsZoomFov = value.AdsZoomFov,
        AdsTransitionInTime = value.AdsTransitionInTime,
        AdsTransitionOutTime = value.AdsTransitionOutTime,
        ClipSize = value.ClipSize,
        ImpactType = value.ImpactType,
        FireTime = value.FireTime,
        DpadIconRatio = value.DpadIconRatio,
        PenetrateMultiplier = value.PenetrateMultiplier,
        AdsViewKickCenterSpeed = value.AdsViewKickCenterSpeed,
        HipViewKickCenterSpeed = value.HipViewKickCenterSpeed,
        AlternateWeaponNamePointer = value.AlternateWeaponNamePointer,
        AlternateWeaponName = value.AlternateWeaponName,
        AlternateWeaponIndex = value.AlternateWeaponIndex,
        AlternateRaiseTime = value.AlternateRaiseTime,
        KillIconPointer = value.KillIconPointer,
        DpadIconPointer = value.DpadIconPointer,
        KillIcon = value.KillIcon,
        DpadIcon = value.DpadIcon,
        FireAnimLength = value.FireAnimLength,
        FirstRaiseTime = value.FirstRaiseTime,
        AmmoDropStockMax = value.AmmoDropStockMax,
        AdsDofStart = value.AdsDofStart,
        AdsDofEnd = value.AdsDofEnd,
        AiVsAiAccuracyGraphKnotCount = value.AiVsAiAccuracyGraphKnotCount,
        AiVsPlayerAccuracyGraphKnotCount = value.AiVsPlayerAccuracyGraphKnotCount,
        AiVsAiAccuracyGraphKnotsPointer = value.AiVsAiAccuracyGraphKnotsPointer,
        AiVsAiAccuracyGraphKnots = CopyList(value.AiVsAiAccuracyGraphKnots),
        AiVsPlayerAccuracyGraphKnotsPointer = value.AiVsPlayerAccuracyGraphKnotsPointer,
        AiVsPlayerAccuracyGraphKnots = CopyList(value.AiVsPlayerAccuracyGraphKnots),
        MotionTracker = value.MotionTracker,
        Enhanced = value.Enhanced,
        DpadIconShowsAmmo = value.DpadIconShowsAmmo,
        Padding73 = value.Padding73
    };

    private static WeaponNoteTrackMaps SanitizeNoteTracks(WeaponNoteTrackMaps current, WeaponNoteTrackMaps value) => new()
    {
        SoundMapKeysPointer = current.SoundMapKeysPointer,
        SoundMapValuesPointer = current.SoundMapValuesPointer,
        SoundMappings = SanitizeNoteTrackMappings(current.SoundMappings, value.SoundMappings),
        RumbleMapKeysPointer = current.RumbleMapKeysPointer,
        RumbleMapValuesPointer = current.RumbleMapValuesPointer,
        RumbleMappings = SanitizeNoteTrackMappings(current.RumbleMappings, value.RumbleMappings)
    };

    private static WeaponFlashEffectFields SanitizeFlashEffects(
        WeaponFlashEffectFields current,
        WeaponFlashEffectFields value) => new()
    {
        ViewPointer = ProviderPointerIfUnchanged(current.View, value.View, current.ViewPointer),
        View = value.View,
        WorldPointer = ProviderPointerIfUnchanged(current.World, value.World, current.WorldPointer),
        World = value.World
    };

    private static WeaponShellEjectEffectFields SanitizeShellEjectEffects(
        WeaponShellEjectEffectFields current,
        WeaponShellEjectEffectFields value) => new()
    {
        ViewPointer = ProviderPointerIfUnchanged(current.View, value.View, current.ViewPointer),
        View = value.View,
        WorldPointer = ProviderPointerIfUnchanged(current.World, value.World, current.WorldPointer),
        World = value.World,
        ViewLastShotPointer = ProviderPointerIfUnchanged(current.ViewLastShot, value.ViewLastShot, current.ViewLastShotPointer),
        ViewLastShot = value.ViewLastShot,
        WorldLastShotPointer = ProviderPointerIfUnchanged(current.WorldLastShot, value.WorldLastShot, current.WorldLastShotPointer),
        WorldLastShot = value.WorldLastShot
    };

    private static WeaponPrimarySoundFields SanitizePrimarySounds(
        WeaponPrimarySoundFields current,
        WeaponPrimarySoundFields value) => new()
    {
        PickupSound = SanitizeSoundAlias(current.PickupSound, value.PickupSound),
        PickupSoundPlayer = SanitizeSoundAlias(current.PickupSoundPlayer, value.PickupSoundPlayer),
        AmmoPickupSound = SanitizeSoundAlias(current.AmmoPickupSound, value.AmmoPickupSound),
        AmmoPickupSoundPlayer = SanitizeSoundAlias(current.AmmoPickupSoundPlayer, value.AmmoPickupSoundPlayer),
        ProjectileSound = SanitizeSoundAlias(current.ProjectileSound, value.ProjectileSound),
        PullbackSound = SanitizeSoundAlias(current.PullbackSound, value.PullbackSound),
        PullbackSoundPlayer = SanitizeSoundAlias(current.PullbackSoundPlayer, value.PullbackSoundPlayer),
        FireSound = SanitizeSoundAlias(current.FireSound, value.FireSound),
        FireSoundPlayer = SanitizeSoundAlias(current.FireSoundPlayer, value.FireSoundPlayer),
        FireSoundPlayerAkimbo = SanitizeSoundAlias(current.FireSoundPlayerAkimbo, value.FireSoundPlayerAkimbo),
        FireLoopSound = SanitizeSoundAlias(current.FireLoopSound, value.FireLoopSound),
        FireLoopSoundPlayer = SanitizeSoundAlias(current.FireLoopSoundPlayer, value.FireLoopSoundPlayer),
        FireStopSound = SanitizeSoundAlias(current.FireStopSound, value.FireStopSound),
        FireStopSoundPlayer = SanitizeSoundAlias(current.FireStopSoundPlayer, value.FireStopSoundPlayer),
        FireLastSound = SanitizeSoundAlias(current.FireLastSound, value.FireLastSound),
        FireLastSoundPlayer = SanitizeSoundAlias(current.FireLastSoundPlayer, value.FireLastSoundPlayer),
        EmptyFireSound = SanitizeSoundAlias(current.EmptyFireSound, value.EmptyFireSound),
        EmptyFireSoundPlayer = SanitizeSoundAlias(current.EmptyFireSoundPlayer, value.EmptyFireSoundPlayer),
        MeleeSwipeSound = SanitizeSoundAlias(current.MeleeSwipeSound, value.MeleeSwipeSound),
        MeleeSwipeSoundPlayer = SanitizeSoundAlias(current.MeleeSwipeSoundPlayer, value.MeleeSwipeSoundPlayer),
        MeleeHitSound = SanitizeSoundAlias(current.MeleeHitSound, value.MeleeHitSound),
        MeleeMissSound = SanitizeSoundAlias(current.MeleeMissSound, value.MeleeMissSound),
        RechamberSound = SanitizeSoundAlias(current.RechamberSound, value.RechamberSound),
        RechamberSoundPlayer = SanitizeSoundAlias(current.RechamberSoundPlayer, value.RechamberSoundPlayer),
        ReloadSound = SanitizeSoundAlias(current.ReloadSound, value.ReloadSound),
        ReloadSoundPlayer = SanitizeSoundAlias(current.ReloadSoundPlayer, value.ReloadSoundPlayer),
        ReloadEmptySound = SanitizeSoundAlias(current.ReloadEmptySound, value.ReloadEmptySound),
        ReloadEmptySoundPlayer = SanitizeSoundAlias(current.ReloadEmptySoundPlayer, value.ReloadEmptySoundPlayer),
        ReloadStartSound = SanitizeSoundAlias(current.ReloadStartSound, value.ReloadStartSound),
        ReloadStartSoundPlayer = SanitizeSoundAlias(current.ReloadStartSoundPlayer, value.ReloadStartSoundPlayer),
        ReloadEndSound = SanitizeSoundAlias(current.ReloadEndSound, value.ReloadEndSound),
        ReloadEndSoundPlayer = SanitizeSoundAlias(current.ReloadEndSoundPlayer, value.ReloadEndSoundPlayer),
        DetonateSound = SanitizeSoundAlias(current.DetonateSound, value.DetonateSound),
        DetonateSoundPlayer = SanitizeSoundAlias(current.DetonateSoundPlayer, value.DetonateSoundPlayer),
        NightVisionWearSound = SanitizeSoundAlias(current.NightVisionWearSound, value.NightVisionWearSound),
        NightVisionWearSoundPlayer = SanitizeSoundAlias(current.NightVisionWearSoundPlayer, value.NightVisionWearSoundPlayer),
        NightVisionRemoveSound = SanitizeSoundAlias(current.NightVisionRemoveSound, value.NightVisionRemoveSound),
        NightVisionRemoveSoundPlayer = SanitizeSoundAlias(current.NightVisionRemoveSoundPlayer, value.NightVisionRemoveSoundPlayer),
        AltSwitchSound = SanitizeSoundAlias(current.AltSwitchSound, value.AltSwitchSound),
        AltSwitchSoundPlayer = SanitizeSoundAlias(current.AltSwitchSoundPlayer, value.AltSwitchSoundPlayer),
        RaiseSound = SanitizeSoundAlias(current.RaiseSound, value.RaiseSound),
        RaiseSoundPlayer = SanitizeSoundAlias(current.RaiseSoundPlayer, value.RaiseSoundPlayer),
        FirstRaiseSound = SanitizeSoundAlias(current.FirstRaiseSound, value.FirstRaiseSound),
        FirstRaiseSoundPlayer = SanitizeSoundAlias(current.FirstRaiseSoundPlayer, value.FirstRaiseSoundPlayer),
        PutawaySound = SanitizeSoundAlias(current.PutawaySound, value.PutawaySound),
        PutawaySoundPlayer = SanitizeSoundAlias(current.PutawaySoundPlayer, value.PutawaySoundPlayer),
        ScanSound = SanitizeSoundAlias(current.ScanSound, value.ScanSound)
    };

    private static WeaponIconPointers SanitizeIcons(WeaponIconPointers current, WeaponIconPointers value) => new()
    {
        HudIconPointer = ProviderPointerIfUnchanged(current.HudIcon, value.HudIcon, current.HudIconPointer),
        HudIcon = value.HudIcon,
        HudIconRatio = value.HudIconRatio,
        PickupIconPointer = ProviderPointerIfUnchanged(current.PickupIcon, value.PickupIcon, current.PickupIconPointer),
        PickupIcon = value.PickupIcon,
        PickupIconRatio = value.PickupIconRatio,
        AmmoCounterIconPointer = ProviderPointerIfUnchanged(current.AmmoCounterIcon, value.AmmoCounterIcon, current.AmmoCounterIconPointer),
        AmmoCounterIcon = value.AmmoCounterIcon,
        AmmoCounterIconRatio = value.AmmoCounterIconRatio,
        AmmoCounterClip = value.AmmoCounterClip,
        StartAmmo = value.StartAmmo
    };

    private static WeaponAmmoFields SanitizeAmmo(WeaponAmmoFields current, WeaponAmmoFields value) => new()
    {
        AmmoNamePointer = XStringIfUnchanged(current.AmmoName, value.AmmoName, current.AmmoNamePointer),
        AmmoName = value.AmmoName,
        AmmoIndex = value.AmmoIndex,
        ClipNamePointer = XStringIfUnchanged(current.ClipName, value.ClipName, current.ClipNamePointer),
        ClipName = value.ClipName,
        ClipIndex = value.ClipIndex,
        MaxAmmo = value.MaxAmmo,
        ShotCount = value.ShotCount,
        SharedAmmoCapNamePointer = XStringIfUnchanged(current.SharedAmmoCapName, value.SharedAmmoCapName, current.SharedAmmoCapNamePointer),
        SharedAmmoCapName = value.SharedAmmoCapName,
        SharedAmmoCapIndex = value.SharedAmmoCapIndex,
        SharedAmmoCap = value.SharedAmmoCap,
        Damage = value.Damage,
        PlayerDamage = value.PlayerDamage,
        MeleeDamage = value.MeleeDamage,
        DamageType = value.DamageType
    };

    private static WeaponReticleFields SanitizeReticle(
        WeaponReticleFields current,
        WeaponReticleFields value) => new()
    {
        CenterMaterialPointer = ProviderPointerIfUnchanged(current.CenterMaterial, value.CenterMaterial, current.CenterMaterialPointer),
        CenterMaterial = value.CenterMaterial,
        SideMaterialPointer = ProviderPointerIfUnchanged(current.SideMaterial, value.SideMaterial, current.SideMaterialPointer),
        SideMaterial = value.SideMaterial,
        CenterSize = value.CenterSize,
        SideSize = value.SideSize,
        MinOffset = value.MinOffset,
        ActiveType = value.ActiveType
    };

    private static WeaponOverlayFields SanitizeOverlay(WeaponOverlayFields current, WeaponOverlayFields value) => new()
    {
        MaterialPointer = ProviderPointerIfUnchanged(current.Material, value.Material, current.MaterialPointer),
        Material = value.Material,
        MaterialLowResPointer = ProviderPointerIfUnchanged(current.MaterialLowRes, value.MaterialLowRes, current.MaterialLowResPointer),
        MaterialLowRes = value.MaterialLowRes,
        MaterialEmpPointer = ProviderPointerIfUnchanged(current.MaterialEmp, value.MaterialEmp, current.MaterialEmpPointer),
        MaterialEmp = value.MaterialEmp,
        MaterialEmpLowResPointer = ProviderPointerIfUnchanged(current.MaterialEmpLowRes, value.MaterialEmpLowRes, current.MaterialEmpLowResPointer),
        MaterialEmpLowRes = value.MaterialEmpLowRes,
        Reticle = value.Reticle,
        Interface = value.Interface,
        Width = value.Width,
        Height = value.Height,
        WidthSplitscreen = value.WidthSplitscreen,
        HeightSplitscreen = value.HeightSplitscreen
    };

    private static WeaponProjectileFields SanitizeProjectile(
        WeaponProjectileFields current,
        WeaponProjectileFields value) => new()
    {
            ModelPointer = ProviderPointerIfUnchanged(current.Model, value.Model, current.ModelPointer),
            Model = value.Model,
            Explosion = value.Explosion,
            ExplosionEffectPointer = ProviderPointerIfUnchanged(current.ExplosionEffect, value.ExplosionEffect, current.ExplosionEffectPointer),
            ExplosionEffect = value.ExplosionEffect,
            DudEffectPointer = ProviderPointerIfUnchanged(current.DudEffect, value.DudEffect, current.DudEffectPointer),
            DudEffect = value.DudEffect,
            ExplosionSoundPointer = XStringIfUnchanged(current.ExplosionSound, value.ExplosionSound, current.ExplosionSoundPointer),
            ExplosionSoundValuePointer = XStringIfUnchanged(current.ExplosionSound, value.ExplosionSound, current.ExplosionSoundValuePointer),
            ExplosionSound = value.ExplosionSound,
            DudSoundPointer = XStringIfUnchanged(current.DudSound, value.DudSound, current.DudSoundPointer),
            DudSoundValuePointer = XStringIfUnchanged(current.DudSound, value.DudSound, current.DudSoundValuePointer),
            DudSound = value.DudSound,
            Stickiness = value.Stickiness,
            LowAmmoWarningThreshold = value.LowAmmoWarningThreshold,
            RicochetChance = value.RicochetChance,
            ParallelBouncePointer = current.ParallelBouncePointer,
            ParallelBounce = CopyList(value.ParallelBounce),
            PerpendicularBouncePointer = current.PerpendicularBouncePointer,
            PerpendicularBounce = CopyList(value.PerpendicularBounce),
            TrailEffectPointer = ProviderPointerIfUnchanged(current.TrailEffect, value.TrailEffect, current.TrailEffectPointer),
            TrailEffect = value.TrailEffect,
            BeaconEffectPointer = ProviderPointerIfUnchanged(current.BeaconEffect, value.BeaconEffect, current.BeaconEffectPointer),
            BeaconEffect = value.BeaconEffect,
            ProjectileColor = value.ProjectileColor,
            GuidedMissileType = value.GuidedMissileType,
            MaxSteeringAcceleration = value.MaxSteeringAcceleration,
            IgnitionDelay = value.IgnitionDelay,
            IgnitionEffectPointer = ProviderPointerIfUnchanged(current.IgnitionEffect, value.IgnitionEffect, current.IgnitionEffectPointer),
            IgnitionEffect = value.IgnitionEffect,
            IgnitionSoundPointer = XStringIfUnchanged(current.IgnitionSound, value.IgnitionSound, current.IgnitionSoundPointer),
            IgnitionSoundValuePointer = XStringIfUnchanged(current.IgnitionSound, value.IgnitionSound, current.IgnitionSoundValuePointer),
            IgnitionSound = value.IgnitionSound,
            AdsAimPitch = value.AdsAimPitch,
            AdsCrosshairInFraction = value.AdsCrosshairInFraction,
            AdsCrosshairOutFraction = value.AdsCrosshairOutFraction,
            GunKickAndDistance = WeaponGraph.Copy(value.GunKickAndDistance)
    };

    private static WeaponAccuracyFields SanitizeAccuracy(WeaponAccuracyFields current, WeaponAccuracyFields value) => new()
    {
        AiVsAiGraphNamePointer = XStringIfUnchanged(current.AiVsAiGraphName, value.AiVsAiGraphName, current.AiVsAiGraphNamePointer),
        AiVsAiGraphName = value.AiVsAiGraphName,
        AiVsPlayerGraphNamePointer = XStringIfUnchanged(current.AiVsPlayerGraphName, value.AiVsPlayerGraphName, current.AiVsPlayerGraphNamePointer),
        AiVsPlayerGraphName = value.AiVsPlayerGraphName,
        OriginalAiVsAiGraphKnotsPointer = current.OriginalAiVsAiGraphKnotsPointer,
        OriginalAiVsAiGraphKnots = CopyList(value.OriginalAiVsAiGraphKnots),
        OriginalAiVsPlayerGraphKnotsPointer = current.OriginalAiVsPlayerGraphKnotsPointer,
        OriginalAiVsPlayerGraphKnots = CopyList(value.OriginalAiVsPlayerGraphKnots),
        OriginalAiVsAiGraphKnotCount = value.OriginalAiVsAiGraphKnotCount,
        OriginalAiVsPlayerGraphKnotCount = value.OriginalAiVsPlayerGraphKnotCount,
        PositionReloadTransitionTime = value.PositionReloadTransitionTime,
        LeftArc = value.LeftArc,
        RightArc = value.RightArc,
        TopArc = value.TopArc,
        BottomArc = value.BottomArc,
        Accuracy = value.Accuracy,
        AiSpread = value.AiSpread,
        PlayerSpread = value.PlayerSpread
    };

    private static WeaponHintFields SanitizeHints(WeaponHintFields current, WeaponHintFields value) => new()
    {
        UseHintStringPointer = XStringIfUnchanged(current.UseHintString, value.UseHintString, current.UseHintStringPointer),
        UseHintString = value.UseHintString,
        DropHintStringPointer = XStringIfUnchanged(current.DropHintString, value.DropHintString, current.DropHintStringPointer),
        DropHintString = value.DropHintString,
        UseHintStringIndex = value.UseHintStringIndex,
        DropHintStringIndex = value.DropHintStringIndex,
        HorizontalViewJitter = value.HorizontalViewJitter,
        VerticalViewJitter = value.VerticalViewJitter,
        ScanSpeed = value.ScanSpeed,
        ScanAcceleration = value.ScanAcceleration,
        ScanPauseTime = value.ScanPauseTime
    };

    private static WeaponRumbleFields SanitizeRumble(WeaponRumbleFields current, WeaponRumbleFields value) => new()
    {
        FireRumblePointer = XStringIfUnchanged(current.FireRumble, value.FireRumble, current.FireRumblePointer),
        FireRumble = value.FireRumble,
        MeleeImpactRumblePointer = XStringIfUnchanged(current.MeleeImpactRumble, value.MeleeImpactRumble, current.MeleeImpactRumblePointer),
        MeleeImpactRumble = value.MeleeImpactRumble
    };

    private static WeaponTurretFields SanitizeTurret(
        WeaponTurretFields current,
        WeaponTurretFields value) => new()
    {
            OverheatSoundPointer = XStringIfUnchanged(current.OverheatSound, value.OverheatSound, current.OverheatSoundPointer),
            OverheatSoundValuePointer = XStringIfUnchanged(current.OverheatSound, value.OverheatSound, current.OverheatSoundValuePointer),
            OverheatSound = value.OverheatSound,
            OverheatEffectPointer = ProviderPointerIfUnchanged(current.OverheatEffect, value.OverheatEffect, current.OverheatEffectPointer),
            OverheatEffect = value.OverheatEffect,
            BarrelSpinRumblePointer = XStringIfUnchanged(current.BarrelSpinRumble, value.BarrelSpinRumble, current.BarrelSpinRumblePointer),
            BarrelSpinRumble = value.BarrelSpinRumble,
            BarrelSpinSpeed = value.BarrelSpinSpeed,
            BarrelSpinUpTime = value.BarrelSpinUpTime,
            BarrelSpinDownTime = value.BarrelSpinDownTime,
            BarrelSpinMaxSoundPointer = XStringIfUnchanged(current.BarrelSpinMaxSound, value.BarrelSpinMaxSound, current.BarrelSpinMaxSoundPointer),
            BarrelSpinMaxSoundValuePointer = XStringIfUnchanged(current.BarrelSpinMaxSound, value.BarrelSpinMaxSound, current.BarrelSpinMaxSoundValuePointer),
            BarrelSpinMaxSound = value.BarrelSpinMaxSound,
            BarrelSpinUpSounds = SanitizeSoundAliases(current.BarrelSpinUpSounds, value.BarrelSpinUpSounds),
            BarrelSpinDownSounds = SanitizeSoundAliases(current.BarrelSpinDownSounds, value.BarrelSpinDownSounds)
    };

    private static WeaponMissileConeSoundFields SanitizeMissile(WeaponMissileConeSoundFields current, WeaponMissileConeSoundFields value) => new()
    {
        AliasPointer = XStringIfUnchanged(current.Alias, value.Alias, current.AliasPointer),
        AliasValuePointer = XStringIfUnchanged(current.Alias, value.Alias, current.AliasValuePointer),
        Alias = value.Alias,
        AliasAtBasePointer = XStringIfUnchanged(current.AliasAtBase, value.AliasAtBase, current.AliasAtBasePointer),
        AliasAtBaseValuePointer = XStringIfUnchanged(current.AliasAtBase, value.AliasAtBase, current.AliasAtBaseValuePointer),
        AliasAtBase = value.AliasAtBase,
        RadiusAtTop = value.RadiusAtTop,
        RadiusAtBase = value.RadiusAtBase,
        Height = value.Height,
        OriginOffset = value.OriginOffset,
        VolumeScaleAtCore = value.VolumeScaleAtCore,
        VolumeScaleAtEdge = value.VolumeScaleAtEdge,
        VolumeScaleCoreSize = value.VolumeScaleCoreSize,
        PitchAtTop = value.PitchAtTop,
        PitchAtBottom = value.PitchAtBottom,
        PitchTopSize = value.PitchTopSize,
        PitchBottomSize = value.PitchBottomSize,
        CrossfadeTopSize = value.CrossfadeTopSize,
        CrossfadeBottomSize = value.CrossfadeBottomSize
    };

    private static WeaponTailFlags PreserveTailPadding(WeaponTailFlags current, WeaponTailFlags value) => new()
    {
        SharedAmmo = value.SharedAmmo,
        LockonSupported = value.LockonSupported,
        RequireLockonToFire = value.RequireLockonToFire,
        BigExplosion = value.BigExplosion,
        NoAdsWhenMagEmpty = value.NoAdsWhenMagEmpty,
        AvoidDropCleanup = value.AvoidDropCleanup,
        InheritsPerks = value.InheritsPerks,
        CrosshairColorChange = value.CrosshairColorChange,
        RifleBullet = value.RifleBullet,
        ArmorPiercing = value.ArmorPiercing,
        BoltAction = value.BoltAction,
        AimDownSight = value.AimDownSight,
        RechamberWhileAds = value.RechamberWhileAds,
        BulletExplosiveDamage = value.BulletExplosiveDamage,
        CookOffHold = value.CookOffHold,
        ClipOnly = value.ClipOnly,
        NoAmmoPickup = value.NoAmmoPickup,
        AdsFireOnly = value.AdsFireOnly,
        CancelAutoHolsterWhenEmpty = value.CancelAutoHolsterWhenEmpty,
        DisableSwitchToWhenEmpty = value.DisableSwitchToWhenEmpty,
        SuppressAmmoReserveDisplay = value.SuppressAmmoReserveDisplay,
        LaserSightDuringNightvision = value.LaserSightDuringNightvision,
        MarkableViewmodel = value.MarkableViewmodel,
        NoDualWield = value.NoDualWield,
        FlipKillIcon = value.FlipKillIcon,
        NoPartialReload = value.NoPartialReload,
        SegmentedReload = value.SegmentedReload,
        BlocksProne = value.BlocksProne,
        Silenced = value.Silenced,
        IsRollingGrenade = value.IsRollingGrenade,
        ProjectileExplosionEffectForceNormalUp = value.ProjectileExplosionEffectForceNormalUp,
        ProjectileImpactExplode = value.ProjectileImpactExplode,
        StickToPlayers = value.StickToPlayers,
        HasDetonator = value.HasDetonator,
        DisableFiring = value.DisableFiring,
        TimedDetonation = value.TimedDetonation,
        Rotate = value.Rotate,
        HoldButtonToThrow = value.HoldButtonToThrow,
        FreezeMovementWhenFiring = value.FreezeMovementWhenFiring,
        ThermalScope = value.ThermalScope,
        AltModeSameWeapon = value.AltModeSameWeapon,
        TurretBarrelSpinEnabled = value.TurretBarrelSpinEnabled,
        MissileConeSoundEnabled = value.MissileConeSoundEnabled,
        MissileConeSoundPitchShiftEnabled = value.MissileConeSoundPitchShiftEnabled,
        MissileConeSoundCrossfadeEnabled = value.MissileConeSoundCrossfadeEnabled,
        OffhandHoldIsCancelable = value.OffhandHoldIsCancelable,
        ReservedPadding = current.ReservedPadding
    };

    private static void EnsureDefinitionCollectionShapes(WeaponDef current, WeaponDef value)
    {
        EnsureSameCount(current.GunModelPointers, value.GunModelPointers, nameof(value));
        EnsureSameCount(current.GunModels, value.GunModels, nameof(value));
        EnsureSameCount(current.RightHandAnimationNamePointers, value.RightHandAnimationNamePointers, nameof(value));
        EnsureSameCount(current.RightHandAnimationNames, value.RightHandAnimationNames, nameof(value));
        EnsureSameCount(current.LeftHandAnimationNamePointers, value.LeftHandAnimationNamePointers, nameof(value));
        EnsureSameCount(current.LeftHandAnimationNames, value.LeftHandAnimationNames, nameof(value));
        EnsureSameCount(current.BounceSounds, value.BounceSounds, nameof(value));
        EnsureSameCount(current.WorldGunModelPointers, value.WorldGunModelPointers, nameof(value));
        EnsureSameCount(current.WorldGunModels, value.WorldGunModels, nameof(value));
        EnsureSameCount(current.Projectile.ParallelBounce, value.Projectile.ParallelBounce, nameof(value));
        EnsureSameCount(current.Projectile.PerpendicularBounce, value.Projectile.PerpendicularBounce, nameof(value));
        EnsureSameCount(current.Accuracy.OriginalAiVsAiGraphKnots, value.Accuracy.OriginalAiVsAiGraphKnots, nameof(value));
        EnsureSameCount(current.Accuracy.OriginalAiVsPlayerGraphKnots, value.Accuracy.OriginalAiVsPlayerGraphKnots, nameof(value));
        EnsureSameCount(current.LocationDamageMultipliers, value.LocationDamageMultipliers, nameof(value));
        EnsureSameCount(current.Turret.BarrelSpinUpSounds, value.Turret.BarrelSpinUpSounds, nameof(value));
        EnsureSameCount(current.Turret.BarrelSpinDownSounds, value.Turret.BarrelSpinDownSounds, nameof(value));
        EnsureSameCount(current.NoteTrackMaps.SoundMappings, value.NoteTrackMaps.SoundMappings, nameof(value));
        EnsureSameCount(current.NoteTrackMaps.RumbleMappings, value.NoteTrackMaps.RumbleMappings, nameof(value));
    }

    private static WeaponSoundAliasField SanitizeSoundAlias(
        WeaponSoundAliasField current,
        WeaponSoundAliasField value) => new()
    {
        Pointer = XStringIfUnchanged(current.Name, value.Name, current.Pointer),
        ValuePointer = XStringIfUnchanged(current.Name, value.Name, current.ValuePointer),
        Name = value.Name
    };

    private static IReadOnlyList<WeaponSoundAliasField> SanitizeSoundAliases(
        IReadOnlyList<WeaponSoundAliasField> current,
        IReadOnlyList<WeaponSoundAliasField> values)
    {
        var result = new WeaponSoundAliasField[values.Count];
        for (int index = 0; index < values.Count; index++)
            result[index] = SanitizeSoundAlias(current[index], values[index]);
        return Array.AsReadOnly(result);
    }

    private static IReadOnlyList<WeaponNoteTrackMapEntry> SanitizeNoteTrackMappings(
        IReadOnlyList<WeaponNoteTrackMapEntry> current,
        IReadOnlyList<WeaponNoteTrackMapEntry> values)
    {
        var result = new WeaponNoteTrackMapEntry[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            result[index] = new WeaponNoteTrackMapEntry
            {
                Key = PreserveScriptString(current[index].Key, values[index].Key),
                Value = PreserveScriptString(current[index].Value, values[index].Value)
            };
        }
        return Array.AsReadOnly(result);
    }

    private static IReadOnlyList<XPointer<T>> PreserveProviderPointers<T>(IReadOnlyList<T?> current, IReadOnlyList<T?> values, IReadOnlyList<XPointer<T>> pointers) where T : BaseAsset
    {
        var result = new XPointer<T>[values.Count];
        for (int index = 0; index < values.Count; index++)
            result[index] = ProviderEquals(current[index], values[index]) ? pointers[index] : default;
        return Array.AsReadOnly(result);
    }

    private static XPointer<T> ProviderPointerIfUnchanged<T>(T? current, T? value, XPointer<T> pointer) where T : BaseAsset =>
        ProviderEquals(current, value) ? pointer : default;

    private static IReadOnlyList<XString> PreserveXStringPointers(IReadOnlyList<string?> current, IReadOnlyList<string?> values, IReadOnlyList<XString> pointers) =>
        Array.AsReadOnly(values.Select((value, index) => XStringIfUnchanged(current[index], value, pointers[index])).ToArray());

    private static XString XStringIfUnchanged(string? current, string? value, XString pointer) =>
        StringEquals(current, value) ? pointer : default;

    private static IReadOnlyList<ScriptStringReference> PreserveScriptStrings(IReadOnlyList<ScriptStringReference> current, IReadOnlyList<ScriptStringReference> values)
    {
        var result = new ScriptStringReference[values.Count];
        for (int index = 0; index < values.Count; index++)
            result[index] = PreserveScriptString(current[index], values[index]);
        return Array.AsReadOnly(result);
    }

    private static ScriptStringReference PreserveScriptString(
        ScriptStringReference current,
        ScriptStringReference value)
    {
        string? text = string.IsNullOrEmpty(value.Text) ? null : value.Text;
        return StringEquals(current.Text, text)
            ? current
            : new ScriptStringReference(0, text, ScriptStringHandle.Null, default);
    }

    private static bool ProviderEquals(BaseAsset? left, BaseAsset? right) =>
        ReferenceEquals(left, right) || left is not null && right is not null &&
        left.SerializedAssetType == right.SerializedAssetType &&
        StringEquals(NormalizeName(left.SerializedAssetName), NormalizeName(right.SerializedAssetName));

    private static string? NormalizeName(string? value) =>
        value?.Replace('\\', '/').ToLowerInvariant();

    private static bool StringEquals(string? left, string? right) =>
        string.Equals(left, right, StringComparison.Ordinal);

    private static void EnsureSameCount<T>(IReadOnlyList<T> current, IReadOnlyList<T> value, string parameterName)
    {
        if (current.Count != value.Count)
            throw new ArgumentException("Weapon fixed collection shapes cannot be resized.", parameterName);
    }

    private static IReadOnlyList<T> CopyList<T>(IReadOnlyList<T> source) =>
        Array.AsReadOnly(source.ToArray());
}
