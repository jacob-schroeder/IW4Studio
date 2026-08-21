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
        EnsureSameCount(current.AccuracyGraphKnots, value.AccuracyGraphKnots, nameof(value));
        EnsureSameCount(current.OriginalAccuracyGraphKnots, value.OriginalAccuracyGraphKnots, nameof(value));
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
                DropAmmoMin = value.DropAmmoMin,
                FirstRaiseTime = value.FirstRaiseTime,
                DropAmmoMax = value.DropAmmoMax,
                AdsDofStart = value.AdsDofStart,
                AdsDofEnd = value.AdsDofEnd,
                AccuracyGraphKnotCount = current.AccuracyGraphKnotCount,
                OriginalAccuracyGraphKnotCount = current.OriginalAccuracyGraphKnotCount,
                AccuracyGraphKnotsPointer = current.AccuracyGraphKnotsPointer,
                AccuracyGraphKnots = CopyList(value.AccuracyGraphKnots),
                OriginalAccuracyGraphKnotsPointer = current.OriginalAccuracyGraphKnotsPointer,
                OriginalAccuracyGraphKnots = CopyList(value.OriginalAccuracyGraphKnots),
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
            FlashEffectPointers = PreserveProviderPointers(current.FlashEffects, value.FlashEffects, current.FlashEffectPointers),
            FlashEffects = CopyList(value.FlashEffects),
            SoundAliasPointers = PreserveXStringPointers(current.SoundAliasNames, value.SoundAliasNames, current.SoundAliasPointers),
            SoundAliasValuePointers = PreserveXStringPointers(current.SoundAliasNames, value.SoundAliasNames, current.SoundAliasValuePointers),
            SoundAliasNames = CopyList(value.SoundAliasNames),
            BounceSoundPointer = current.BounceSoundPointer,
            BounceSoundPointers = PreserveXStringPointers(current.BounceSoundNames, value.BounceSoundNames, current.BounceSoundPointers),
            BounceSoundValuePointers = PreserveXStringPointers(current.BounceSoundNames, value.BounceSoundNames, current.BounceSoundValuePointers),
            BounceSoundNames = CopyList(value.BounceSoundNames),
            EffectPointers = PreserveProviderPointers(current.Effects, value.Effects, current.EffectPointers),
            Effects = CopyList(value.Effects),
            MaterialPointers = PreserveProviderPointers(current.Materials, value.Materials, current.MaterialPointers),
            Materials = CopyList(value.Materials),
            Reticle = WeaponGraph.Copy(value.Reticle),
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
            Icons = SanitizeIcons(current.Icons, value.Icons, current.IconMaterials, value.IconMaterials),
            IconMaterials = CopyList(value.IconMaterials),
            Ammo = SanitizeAmmo(current.Ammo, value.Ammo),
            Overlay = SanitizeOverlay(current.Overlay, value.Overlay, current.OverlayMaterials, value.OverlayMaterials),
            OverlayMaterials = CopyList(value.OverlayMaterials),
            Timing = WeaponGraph.Copy(value.Timing),
            AimMovementTuning = WeaponGraph.Copy(value.AimMovementTuning),
            AdsViewAndSpread = WeaponGraph.Copy(value.AdsViewAndSpread),
            PhysCollmapPointer = ProviderPointerIfUnchanged(current.PhysCollmap, value.PhysCollmap, current.PhysCollmapPointer),
            PhysCollmap = value.PhysCollmap,
            PhysCollmapName = value.PhysCollmapName,
            Physics = WeaponGraph.Copy(value.Physics),
            Projectile = SanitizeProjectile(current, value),
            ProjectileEffects = CopyList(value.ProjectileEffects),
            ImpactEffects = CopyList(value.ImpactEffects),
            ViewShellEjectEffect = value.ViewShellEjectEffect,
            Accuracy = SanitizeAccuracy(current.Accuracy, value.Accuracy),
            TurnSpeedAndRange = WeaponGraph.Copy(value.TurnSpeedAndRange),
            Hints = SanitizeHints(current.Hints, value.Hints),
            ScriptNamePointer = XStringIfUnchanged(current.ScriptName, value.ScriptName, current.ScriptNamePointer),
            ScriptName = value.ScriptName,
            OOPosAnimLength = value.OOPosAnimLength,
            MinDamage = value.MinDamage,
            MinPlayerDamage = value.MinPlayerDamage,
            MaxDamageRange = value.MaxDamageRange,
            MinDamageRange = value.MinDamageRange,
            DestabilizationRateTime = value.DestabilizationRateTime,
            DestabilizationCurvatureMax = value.DestabilizationCurvatureMax,
            DestabilizeDistance = value.DestabilizeDistance,
            DestabilizeDistanceToTimeScale = value.DestabilizeDistanceToTimeScale,
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
            Turret = SanitizeTurret(current, value),
            TurretOverheatEffect = value.TurretOverheatEffect,
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
        DropAmmoMin = value.DropAmmoMin,
        FirstRaiseTime = value.FirstRaiseTime,
        DropAmmoMax = value.DropAmmoMax,
        AdsDofStart = value.AdsDofStart,
        AdsDofEnd = value.AdsDofEnd,
        AccuracyGraphKnotCount = value.AccuracyGraphKnotCount,
        OriginalAccuracyGraphKnotCount = value.OriginalAccuracyGraphKnotCount,
        AccuracyGraphKnotsPointer = value.AccuracyGraphKnotsPointer,
        AccuracyGraphKnots = CopyList(value.AccuracyGraphKnots),
        OriginalAccuracyGraphKnotsPointer = value.OriginalAccuracyGraphKnotsPointer,
        OriginalAccuracyGraphKnots = CopyList(value.OriginalAccuracyGraphKnots),
        MotionTracker = value.MotionTracker,
        Enhanced = value.Enhanced,
        DpadIconShowsAmmo = value.DpadIconShowsAmmo,
        Padding73 = value.Padding73
    };

    private static WeaponNoteTrackMaps SanitizeNoteTracks(WeaponNoteTrackMaps current, WeaponNoteTrackMaps value) => new()
    {
        SoundMapKeysPointer = current.SoundMapKeysPointer,
        SoundMapKeys = PreserveScriptStrings(current.SoundMapKeys, value.SoundMapKeys),
        SoundMapValuesPointer = current.SoundMapValuesPointer,
        SoundMapValues = PreserveScriptStrings(current.SoundMapValues, value.SoundMapValues),
        RumbleMapKeysPointer = current.RumbleMapKeysPointer,
        RumbleMapKeys = PreserveScriptStrings(current.RumbleMapKeys, value.RumbleMapKeys),
        RumbleMapValuesPointer = current.RumbleMapValuesPointer,
        RumbleMapValues = PreserveScriptStrings(current.RumbleMapValues, value.RumbleMapValues)
    };

    private static WeaponIconPointers SanitizeIcons(WeaponIconPointers current, WeaponIconPointers value, IReadOnlyList<IW4.Assets.Assets.Material.MaterialAsset?> currentMaterials, IReadOnlyList<IW4.Assets.Assets.Material.MaterialAsset?> materials) => new()
    {
        HudIconPointer = ProviderPointerIfUnchanged(currentMaterials[0], materials[0], current.HudIconPointer),
        HudIconRatio = value.HudIconRatio,
        PickupIconPointer = ProviderPointerIfUnchanged(currentMaterials[1], materials[1], current.PickupIconPointer),
        PickupIconRatio = value.PickupIconRatio,
        AmmoCounterIconPointer = ProviderPointerIfUnchanged(currentMaterials[2], materials[2], current.AmmoCounterIconPointer),
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

    private static WeaponOverlayFields SanitizeOverlay(WeaponOverlayFields current, WeaponOverlayFields value, IReadOnlyList<IW4.Assets.Assets.Material.MaterialAsset?> currentMaterials, IReadOnlyList<IW4.Assets.Assets.Material.MaterialAsset?> materials) => new()
    {
        OverlayMaterials = PreserveProviderPointers(currentMaterials, materials, current.OverlayMaterials),
        Reticle = value.Reticle,
        Interface = value.Interface,
        Width = value.Width,
        Height = value.Height,
        WidthSplitscreen = value.WidthSplitscreen,
        HeightSplitscreen = value.HeightSplitscreen
    };

    private static WeaponProjectileFields SanitizeProjectile(WeaponDef currentDefinition, WeaponDef valueDefinition)
    {
        WeaponProjectileFields current = currentDefinition.Projectile;
        WeaponProjectileFields value = valueDefinition.Projectile;
        return new WeaponProjectileFields
        {
            ModelPointer = ProviderPointerIfUnchanged(current.Model, value.Model, current.ModelPointer),
            Model = value.Model,
            Explosion = value.Explosion,
            ExplosionEffectPointer = ProviderPointerIfUnchanged(currentDefinition.ProjectileEffects[0], valueDefinition.ProjectileEffects[0], current.ExplosionEffectPointer),
            DudEffectPointer = ProviderPointerIfUnchanged(currentDefinition.ProjectileEffects[1], valueDefinition.ProjectileEffects[1], current.DudEffectPointer),
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
            TrailEffectPointer = ProviderPointerIfUnchanged(currentDefinition.ImpactEffects[0], valueDefinition.ImpactEffects[0], current.TrailEffectPointer),
            BeaconEffectPointer = ProviderPointerIfUnchanged(currentDefinition.ImpactEffects[1], valueDefinition.ImpactEffects[1], current.BeaconEffectPointer),
            ProjectileColor = value.ProjectileColor,
            GuidedMissileType = value.GuidedMissileType,
            MaxSteeringAcceleration = value.MaxSteeringAcceleration,
            IgnitionDelay = value.IgnitionDelay,
            // The semantic provider is named ViewShellEjectEffect in the recovered
            // model while this serialized cell is named IgnitionEffectPointer.
            IgnitionEffectPointer = ProviderPointerIfUnchanged(currentDefinition.ViewShellEjectEffect, valueDefinition.ViewShellEjectEffect, current.IgnitionEffectPointer),
            IgnitionSoundPointer = XStringIfUnchanged(current.IgnitionSound, value.IgnitionSound, current.IgnitionSoundPointer),
            IgnitionSoundValuePointer = XStringIfUnchanged(current.IgnitionSound, value.IgnitionSound, current.IgnitionSoundValuePointer),
            IgnitionSound = value.IgnitionSound,
            AdsAimPitch = value.AdsAimPitch,
            AdsCrosshairInFraction = value.AdsCrosshairInFraction,
            AdsCrosshairOutFraction = value.AdsCrosshairOutFraction,
            GunKickAndDistance = WeaponGraph.Copy(value.GunKickAndDistance)
        };
    }

    private static WeaponAccuracyFields SanitizeAccuracy(WeaponAccuracyFields current, WeaponAccuracyFields value) => new()
    {
        GraphName0Pointer = XStringIfUnchanged(current.GraphName0, value.GraphName0, current.GraphName0Pointer),
        GraphName0 = value.GraphName0,
        GraphName1Pointer = XStringIfUnchanged(current.GraphName1, value.GraphName1, current.GraphName1Pointer),
        GraphName1 = value.GraphName1,
        GraphKnotsPointer = current.GraphKnotsPointer,
        GraphKnots = CopyList(value.GraphKnots),
        OriginalGraphKnotsPointer = current.OriginalGraphKnotsPointer,
        OriginalGraphKnots = CopyList(value.OriginalGraphKnots),
        LocalGraphKnotCount = value.LocalGraphKnotCount,
        LocalOriginalGraphKnotCount = value.LocalOriginalGraphKnotCount,
        AnimationNotifyComparison = value.AnimationNotifyComparison,
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

    private static WeaponTurretFields SanitizeTurret(WeaponDef currentDefinition, WeaponDef valueDefinition)
    {
        WeaponTurretFields current = currentDefinition.Turret;
        WeaponTurretFields value = valueDefinition.Turret;
        return new WeaponTurretFields
        {
            OverheatSoundPointer = XStringIfUnchanged(current.OverheatSound, value.OverheatSound, current.OverheatSoundPointer),
            OverheatSoundValuePointer = XStringIfUnchanged(current.OverheatSound, value.OverheatSound, current.OverheatSoundValuePointer),
            OverheatSound = value.OverheatSound,
            OverheatEffectPointer = ProviderPointerIfUnchanged(currentDefinition.TurretOverheatEffect, valueDefinition.TurretOverheatEffect, current.OverheatEffectPointer),
            BarrelSpinRumblePointer = XStringIfUnchanged(current.BarrelSpinRumble, value.BarrelSpinRumble, current.BarrelSpinRumblePointer),
            BarrelSpinRumble = value.BarrelSpinRumble,
            BarrelSpinSpeed = value.BarrelSpinSpeed,
            BarrelSpinUpTime = value.BarrelSpinUpTime,
            BarrelSpinDownTime = value.BarrelSpinDownTime,
            BarrelSpinMaxSoundPointer = XStringIfUnchanged(current.BarrelSpinMaxSound, value.BarrelSpinMaxSound, current.BarrelSpinMaxSoundPointer),
            BarrelSpinMaxSoundValuePointer = XStringIfUnchanged(current.BarrelSpinMaxSound, value.BarrelSpinMaxSound, current.BarrelSpinMaxSoundValuePointer),
            BarrelSpinMaxSound = value.BarrelSpinMaxSound,
            BarrelSpinUpSoundPointers = PreserveXStringPointers(current.BarrelSpinUpSoundNames, value.BarrelSpinUpSoundNames, current.BarrelSpinUpSoundPointers),
            BarrelSpinUpSoundValuePointers = PreserveXStringPointers(current.BarrelSpinUpSoundNames, value.BarrelSpinUpSoundNames, current.BarrelSpinUpSoundValuePointers),
            BarrelSpinUpSoundNames = CopyList(value.BarrelSpinUpSoundNames),
            BarrelSpinDownSoundPointers = PreserveXStringPointers(current.BarrelSpinDownSoundNames, value.BarrelSpinDownSoundNames, current.BarrelSpinDownSoundPointers),
            BarrelSpinDownSoundValuePointers = PreserveXStringPointers(current.BarrelSpinDownSoundNames, value.BarrelSpinDownSoundNames, current.BarrelSpinDownSoundValuePointers),
            BarrelSpinDownSoundNames = CopyList(value.BarrelSpinDownSoundNames)
        };
    }

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
        EnsureSameCount(current.FlashEffectPointers, value.FlashEffectPointers, nameof(value));
        EnsureSameCount(current.FlashEffects, value.FlashEffects, nameof(value));
        EnsureSameCount(current.SoundAliasPointers, value.SoundAliasPointers, nameof(value));
        EnsureSameCount(current.SoundAliasValuePointers, value.SoundAliasValuePointers, nameof(value));
        EnsureSameCount(current.SoundAliasNames, value.SoundAliasNames, nameof(value));
        EnsureSameCount(current.BounceSoundPointers, value.BounceSoundPointers, nameof(value));
        EnsureSameCount(current.BounceSoundValuePointers, value.BounceSoundValuePointers, nameof(value));
        EnsureSameCount(current.BounceSoundNames, value.BounceSoundNames, nameof(value));
        EnsureSameCount(current.EffectPointers, value.EffectPointers, nameof(value));
        EnsureSameCount(current.Effects, value.Effects, nameof(value));
        EnsureSameCount(current.MaterialPointers, value.MaterialPointers, nameof(value));
        EnsureSameCount(current.Materials, value.Materials, nameof(value));
        EnsureSameCount(current.WorldGunModelPointers, value.WorldGunModelPointers, nameof(value));
        EnsureSameCount(current.WorldGunModels, value.WorldGunModels, nameof(value));
        EnsureSameCount(current.IconMaterials, value.IconMaterials, nameof(value));
        EnsureSameCount(current.Overlay.OverlayMaterials, value.Overlay.OverlayMaterials, nameof(value));
        EnsureSameCount(current.OverlayMaterials, value.OverlayMaterials, nameof(value));
        EnsureSameCount(current.ProjectileEffects, value.ProjectileEffects, nameof(value));
        EnsureSameCount(current.ImpactEffects, value.ImpactEffects, nameof(value));
        EnsureSameCount(current.Projectile.ParallelBounce, value.Projectile.ParallelBounce, nameof(value));
        EnsureSameCount(current.Projectile.PerpendicularBounce, value.Projectile.PerpendicularBounce, nameof(value));
        EnsureSameCount(current.Accuracy.GraphKnots, value.Accuracy.GraphKnots, nameof(value));
        EnsureSameCount(current.Accuracy.OriginalGraphKnots, value.Accuracy.OriginalGraphKnots, nameof(value));
        EnsureSameCount(current.LocationDamageMultipliers, value.LocationDamageMultipliers, nameof(value));
        EnsureSameCount(current.Turret.BarrelSpinUpSoundPointers, value.Turret.BarrelSpinUpSoundPointers, nameof(value));
        EnsureSameCount(current.Turret.BarrelSpinUpSoundValuePointers, value.Turret.BarrelSpinUpSoundValuePointers, nameof(value));
        EnsureSameCount(current.Turret.BarrelSpinUpSoundNames, value.Turret.BarrelSpinUpSoundNames, nameof(value));
        EnsureSameCount(current.Turret.BarrelSpinDownSoundPointers, value.Turret.BarrelSpinDownSoundPointers, nameof(value));
        EnsureSameCount(current.Turret.BarrelSpinDownSoundValuePointers, value.Turret.BarrelSpinDownSoundValuePointers, nameof(value));
        EnsureSameCount(current.Turret.BarrelSpinDownSoundNames, value.Turret.BarrelSpinDownSoundNames, nameof(value));
        EnsureSameCount(current.NoteTrackMaps.SoundMapKeys, value.NoteTrackMaps.SoundMapKeys, nameof(value));
        EnsureSameCount(current.NoteTrackMaps.SoundMapValues, value.NoteTrackMaps.SoundMapValues, nameof(value));
        EnsureSameCount(current.NoteTrackMaps.RumbleMapKeys, value.NoteTrackMaps.RumbleMapKeys, nameof(value));
        EnsureSameCount(current.NoteTrackMaps.RumbleMapValues, value.NoteTrackMaps.RumbleMapValues, nameof(value));
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
        {
            string? text = string.IsNullOrEmpty(values[index].Text) ? null : values[index].Text;
            result[index] = StringEquals(current[index].Text, text)
                ? current[index]
                : new ScriptStringReference(0, text, ScriptStringHandle.Null, default);
        }
        return Array.AsReadOnly(result);
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
