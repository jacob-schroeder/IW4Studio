using IW4.Assets.Assets;
using IW4.Assets.Assets.Weapon;
using IW4.Assets.Math;

namespace IW4.Studio.Documents;

internal static class WeaponGraph
{
    internal static WeaponAsset Copy(WeaponAsset source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new WeaponAsset
        {
            Variant = Copy(source.Variant)
        };
    }

    internal static WeaponVariantDef Copy(WeaponVariantDef source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new WeaponVariantDef
        {
            Offset = source.Offset,
            InternalNamePointer = source.InternalNamePointer,
            InternalName = source.InternalName,
            DefinitionPointer = source.DefinitionPointer,
            Definition = source.Definition is null ? null : Copy(source.Definition),
            DisplayNamePointer = source.DisplayNamePointer,
            DisplayName = source.DisplayName,
            HideTagsPointer = source.HideTagsPointer,
            HideTags = CopyList(source.HideTags),
            AnimationNamesPointer = source.AnimationNamesPointer,
            AnimationNamePointers = CopyList(source.AnimationNamePointers),
            AnimationNames = CopyList(source.AnimationNames),
            AdsZoomFov = source.AdsZoomFov,
            AdsTransitionInTime = source.AdsTransitionInTime,
            AdsTransitionOutTime = source.AdsTransitionOutTime,
            ClipSize = source.ClipSize,
            ImpactType = source.ImpactType,
            FireTime = source.FireTime,
            DpadIconRatio = source.DpadIconRatio,
            PenetrateMultiplier = source.PenetrateMultiplier,
            AdsViewKickCenterSpeed = source.AdsViewKickCenterSpeed,
            HipViewKickCenterSpeed = source.HipViewKickCenterSpeed,
            AlternateWeaponNamePointer = source.AlternateWeaponNamePointer,
            AlternateWeaponName = source.AlternateWeaponName,
            AlternateWeaponIndex = source.AlternateWeaponIndex,
            AlternateRaiseTime = source.AlternateRaiseTime,
            KillIconPointer = source.KillIconPointer,
            DpadIconPointer = source.DpadIconPointer,
            KillIcon = source.KillIcon,
            DpadIcon = source.DpadIcon,
            DropAmmoMin = source.DropAmmoMin,
            FirstRaiseTime = source.FirstRaiseTime,
            DropAmmoMax = source.DropAmmoMax,
            AdsDofStart = source.AdsDofStart,
            AdsDofEnd = source.AdsDofEnd,
            AccuracyGraphKnotCount = source.AccuracyGraphKnotCount,
            OriginalAccuracyGraphKnotCount = source.OriginalAccuracyGraphKnotCount,
            AccuracyGraphKnotsPointer = source.AccuracyGraphKnotsPointer,
            AccuracyGraphKnots = CopyList(source.AccuracyGraphKnots),
            OriginalAccuracyGraphKnotsPointer = source.OriginalAccuracyGraphKnotsPointer,
            OriginalAccuracyGraphKnots = CopyList(source.OriginalAccuracyGraphKnots),
            MotionTracker = source.MotionTracker,
            Enhanced = source.Enhanced,
            DpadIconShowsAmmo = source.DpadIconShowsAmmo,
            Padding73 = source.Padding73
        };
    }

    internal static WeaponDef Copy(WeaponDef source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new WeaponDef
        {
            Offset = source.Offset,
            InternalNamePointer = source.InternalNamePointer,
            InternalName = source.InternalName,
            GunModelsPointer = source.GunModelsPointer,
            GunModelPointers = CopyList(source.GunModelPointers),
            GunModels = CopyList(source.GunModels),
            HandModelPointer = source.HandModelPointer,
            HandModel = source.HandModel,
            RightHandAnimationNamesPointer = source.RightHandAnimationNamesPointer,
            RightHandAnimationNamePointers = CopyList(source.RightHandAnimationNamePointers),
            RightHandAnimationNames = CopyList(source.RightHandAnimationNames),
            LeftHandAnimationNamesPointer = source.LeftHandAnimationNamesPointer,
            LeftHandAnimationNamePointers = CopyList(source.LeftHandAnimationNamePointers),
            LeftHandAnimationNames = CopyList(source.LeftHandAnimationNames),
            ModeNamePointer = source.ModeNamePointer,
            ModeName = source.ModeName,
            NoteTrackMaps = Copy(source.NoteTrackMaps),
            PlayerAnimType = source.PlayerAnimType,
            WeaponType = source.WeaponType,
            WeaponClass = source.WeaponClass,
            PenetrateType = source.PenetrateType,
            InventoryType = source.InventoryType,
            FireType = source.FireType,
            OffhandClass = source.OffhandClass,
            Stance = source.Stance,
            FlashEffectPointers = CopyList(source.FlashEffectPointers),
            FlashEffects = CopyList(source.FlashEffects),
            SoundAliasPointers = CopyList(source.SoundAliasPointers),
            SoundAliasValuePointers = CopyList(source.SoundAliasValuePointers),
            SoundAliasNames = CopyList(source.SoundAliasNames),
            BounceSoundPointer = source.BounceSoundPointer,
            BounceSoundPointers = CopyList(source.BounceSoundPointers),
            BounceSoundValuePointers = CopyList(source.BounceSoundValuePointers),
            BounceSoundNames = CopyList(source.BounceSoundNames),
            EffectPointers = CopyList(source.EffectPointers),
            Effects = CopyList(source.Effects),
            MaterialPointers = CopyList(source.MaterialPointers),
            Materials = CopyList(source.Materials),
            Reticle = Copy(source.Reticle),
            ViewMovement = Copy(source.ViewMovement),
            PositionalMovement = Copy(source.PositionalMovement),
            WorldGunModelsPointer = source.WorldGunModelsPointer,
            WorldGunModelPointers = CopyList(source.WorldGunModelPointers),
            WorldGunModels = CopyList(source.WorldGunModels),
            WorldModelPointers = CopyList(source.WorldModelPointers),
            WorldModels = CopyList(source.WorldModels),
            Icons = Copy(source.Icons),
            IconMaterials = CopyList(source.IconMaterials),
            Ammo = Copy(source.Ammo),
            Overlay = Copy(source.Overlay),
            OverlayMaterials = CopyList(source.OverlayMaterials),
            Timing = Copy(source.Timing),
            AimMovementTuning = Copy(source.AimMovementTuning),
            AdsViewAndSpread = Copy(source.AdsViewAndSpread),
            PhysCollmapPointer = source.PhysCollmapPointer,
            PhysCollmap = source.PhysCollmap,
            PhysCollmapName = source.PhysCollmapName,
            Physics = Copy(source.Physics),
            Projectile = Copy(source.Projectile),
            ProjectileEffects = CopyList(source.ProjectileEffects),
            ImpactEffects = CopyList(source.ImpactEffects),
            ViewShellEjectEffect = source.ViewShellEjectEffect,
            Accuracy = Copy(source.Accuracy),
            TurnSpeedAndRange = Copy(source.TurnSpeedAndRange),
            Hints = Copy(source.Hints),
            ScriptNamePointer = source.ScriptNamePointer,
            ScriptName = source.ScriptName,
            OOPosAnimLength = source.OOPosAnimLength,
            MinDamage = source.MinDamage,
            MinPlayerDamage = source.MinPlayerDamage,
            MaxDamageRange = source.MaxDamageRange,
            MinDamageRange = source.MinDamageRange,
            DestabilizationRateTime = source.DestabilizationRateTime,
            DestabilizationCurvatureMax = source.DestabilizationCurvatureMax,
            DestabilizeDistance = source.DestabilizeDistance,
            DestabilizeDistanceToTimeScale = source.DestabilizeDistanceToTimeScale,
            LocationDamageMultipliersPointer = source.LocationDamageMultipliersPointer,
            LocationDamageMultipliers = CopyList(source.LocationDamageMultipliers),
            Rumble = Copy(source.Rumble),
            TracerPointer = source.TracerPointer,
            Tracer = source.Tracer,
            TurretScopeZoomRate = source.TurretScopeZoomRate,
            TurretScopeZoomMin = source.TurretScopeZoomMin,
            TurretScopeZoomMax = source.TurretScopeZoomMax,
            TurretOverheatUpRate = source.TurretOverheatUpRate,
            TurretOverheatDownRate = source.TurretOverheatDownRate,
            TurretOverheatPenalty = source.TurretOverheatPenalty,
            Turret = Copy(source.Turret),
            TurretOverheatEffect = source.TurretOverheatEffect,
            MissileConeSound = Copy(source.MissileConeSound),
            TailFlags = Copy(source.TailFlags)
        };
    }

    internal static WeaponAccuracyFields Copy(WeaponAccuracyFields source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new WeaponAccuracyFields
        {
            GraphName0Pointer = source.GraphName0Pointer,
            GraphName0 = source.GraphName0,
            GraphName1Pointer = source.GraphName1Pointer,
            GraphName1 = source.GraphName1,
            GraphKnotsPointer = source.GraphKnotsPointer,
            GraphKnots = CopyList(source.GraphKnots),
            OriginalGraphKnotsPointer = source.OriginalGraphKnotsPointer,
            OriginalGraphKnots = CopyList(source.OriginalGraphKnots),
            LocalGraphKnotCount = source.LocalGraphKnotCount,
            LocalOriginalGraphKnotCount = source.LocalOriginalGraphKnotCount,
            AnimationNotifyComparison = source.AnimationNotifyComparison,
            LeftArc = source.LeftArc,
            RightArc = source.RightArc,
            TopArc = source.TopArc,
            BottomArc = source.BottomArc,
            Accuracy = source.Accuracy,
            AiSpread = source.AiSpread,
            PlayerSpread = source.PlayerSpread
        };
    }

    internal static WeaponAdsViewAndSpreadFields Copy(WeaponAdsViewAndSpreadFields source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new WeaponAdsViewAndSpreadFields
        {
            AdsBobFactor = source.AdsBobFactor,
            AdsViewBobMultiplier = source.AdsViewBobMultiplier,
            HipSpreadStandMin = source.HipSpreadStandMin,
            HipSpreadDuckedMin = source.HipSpreadDuckedMin,
            HipSpreadProneMin = source.HipSpreadProneMin,
            HipSpreadStandMax = source.HipSpreadStandMax,
            HipSpreadDuckedMax = source.HipSpreadDuckedMax,
            HipSpreadProneMax = source.HipSpreadProneMax,
            HipSpreadDecayRate = source.HipSpreadDecayRate,
            HipSpreadFireAdd = source.HipSpreadFireAdd,
            HipSpreadTurnAdd = source.HipSpreadTurnAdd,
            HipSpreadMoveAdd = source.HipSpreadMoveAdd,
            HipSpreadDuckedDecay = source.HipSpreadDuckedDecay,
            HipSpreadProneDecay = source.HipSpreadProneDecay,
            HipReticleSidePosition = source.HipReticleSidePosition,
            AdsIdleAmount = source.AdsIdleAmount,
            HipIdleAmount = source.HipIdleAmount,
            AdsIdleSpeed = source.AdsIdleSpeed,
            HipIdleSpeed = source.HipIdleSpeed,
            IdleCrouchFactor = source.IdleCrouchFactor,
            IdleProneFactor = source.IdleProneFactor,
            GunMaxPitch = source.GunMaxPitch,
            GunMaxYaw = source.GunMaxYaw,
            SwayMaxAngle = source.SwayMaxAngle,
            SwayLerpSpeed = source.SwayLerpSpeed,
            SwayPitchScale = source.SwayPitchScale,
            SwayYawScale = source.SwayYawScale,
            SwayHorizontalScale = source.SwayHorizontalScale,
            SwayVerticalScale = source.SwayVerticalScale,
            SwayShellShockScale = source.SwayShellShockScale,
            AdsSwayMaxAngle = source.AdsSwayMaxAngle,
            AdsSwayLerpSpeed = source.AdsSwayLerpSpeed,
            AdsSwayPitchScale = source.AdsSwayPitchScale,
            AdsSwayYawScale = source.AdsSwayYawScale,
            AdsSwayHorizontalScale = source.AdsSwayHorizontalScale,
            AdsSwayVerticalScale = source.AdsSwayVerticalScale,
            AdsViewErrorMin = source.AdsViewErrorMin,
            AdsViewErrorMax = source.AdsViewErrorMax
        };
    }

    internal static WeaponAimMovementTuningFields Copy(WeaponAimMovementTuningFields source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new WeaponAimMovementTuningFields
        {
            AutoAimRange = source.AutoAimRange,
            AimAssistRange = source.AimAssistRange,
            AimAssistRangeAds = source.AimAssistRangeAds,
            AimPadding = source.AimPadding,
            EnemyCrosshairRange = source.EnemyCrosshairRange,
            MoveSpeedScale = source.MoveSpeedScale,
            AdsMoveSpeedScale = source.AdsMoveSpeedScale,
            SprintDurationScale = source.SprintDurationScale,
            AdsZoomInFraction = source.AdsZoomInFraction,
            AdsZoomOutFraction = source.AdsZoomOutFraction
        };
    }

    internal static WeaponAmmoFields Copy(WeaponAmmoFields source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new WeaponAmmoFields
        {
            AmmoNamePointer = source.AmmoNamePointer,
            AmmoName = source.AmmoName,
            AmmoIndex = source.AmmoIndex,
            ClipNamePointer = source.ClipNamePointer,
            ClipName = source.ClipName,
            ClipIndex = source.ClipIndex,
            MaxAmmo = source.MaxAmmo,
            ShotCount = source.ShotCount,
            SharedAmmoCapNamePointer = source.SharedAmmoCapNamePointer,
            SharedAmmoCapName = source.SharedAmmoCapName,
            SharedAmmoCapIndex = source.SharedAmmoCapIndex,
            SharedAmmoCap = source.SharedAmmoCap,
            Damage = source.Damage,
            PlayerDamage = source.PlayerDamage,
            MeleeDamage = source.MeleeDamage,
            DamageType = source.DamageType
        };
    }

    internal static WeaponGunKickAndDistanceFields Copy(WeaponGunKickAndDistanceFields source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new WeaponGunKickAndDistanceFields
        {
            AdsGunKickReducedKickBullets = source.AdsGunKickReducedKickBullets,
            AdsGunKickReducedKickPercent = source.AdsGunKickReducedKickPercent,
            AdsGunKickPitchMin = source.AdsGunKickPitchMin,
            AdsGunKickPitchMax = source.AdsGunKickPitchMax,
            AdsGunKickYawMin = source.AdsGunKickYawMin,
            AdsGunKickYawMax = source.AdsGunKickYawMax,
            AdsGunKickAcceleration = source.AdsGunKickAcceleration,
            AdsGunKickSpeedMax = source.AdsGunKickSpeedMax,
            AdsGunKickSpeedDecay = source.AdsGunKickSpeedDecay,
            AdsGunKickStaticDecay = source.AdsGunKickStaticDecay,
            AdsViewKickPitchMin = source.AdsViewKickPitchMin,
            AdsViewKickPitchMax = source.AdsViewKickPitchMax,
            AdsViewKickYawMin = source.AdsViewKickYawMin,
            AdsViewKickYawMax = source.AdsViewKickYawMax,
            AdsViewScatterMin = source.AdsViewScatterMin,
            AdsViewScatterMax = source.AdsViewScatterMax,
            AdsSpread = source.AdsSpread,
            HipGunKickReducedKickBullets = source.HipGunKickReducedKickBullets,
            HipGunKickReducedKickPercent = source.HipGunKickReducedKickPercent,
            HipGunKickPitchMin = source.HipGunKickPitchMin,
            HipGunKickPitchMax = source.HipGunKickPitchMax,
            HipGunKickYawMin = source.HipGunKickYawMin,
            HipGunKickYawMax = source.HipGunKickYawMax,
            HipGunKickAcceleration = source.HipGunKickAcceleration,
            HipGunKickSpeedMax = source.HipGunKickSpeedMax,
            HipGunKickSpeedDecay = source.HipGunKickSpeedDecay,
            HipGunKickStaticDecay = source.HipGunKickStaticDecay,
            HipViewKickPitchMin = source.HipViewKickPitchMin,
            HipViewKickPitchMax = source.HipViewKickPitchMax,
            HipViewKickYawMin = source.HipViewKickYawMin,
            HipViewKickYawMax = source.HipViewKickYawMax,
            HipViewScatterMin = source.HipViewScatterMin,
            HipViewScatterMax = source.HipViewScatterMax,
            FightDistance = source.FightDistance,
            MaxDistance = source.MaxDistance
        };
    }

    internal static WeaponHintFields Copy(WeaponHintFields source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new WeaponHintFields
        {
            UseHintStringPointer = source.UseHintStringPointer,
            UseHintString = source.UseHintString,
            DropHintStringPointer = source.DropHintStringPointer,
            DropHintString = source.DropHintString,
            UseHintStringIndex = source.UseHintStringIndex,
            DropHintStringIndex = source.DropHintStringIndex,
            HorizontalViewJitter = source.HorizontalViewJitter,
            VerticalViewJitter = source.VerticalViewJitter,
            ScanSpeed = source.ScanSpeed,
            ScanAcceleration = source.ScanAcceleration,
            ScanPauseTime = source.ScanPauseTime
        };
    }

    internal static WeaponIconPointers Copy(WeaponIconPointers source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new WeaponIconPointers
        {
            HudIconPointer = source.HudIconPointer,
            HudIconRatio = source.HudIconRatio,
            PickupIconPointer = source.PickupIconPointer,
            PickupIconRatio = source.PickupIconRatio,
            AmmoCounterIconPointer = source.AmmoCounterIconPointer,
            AmmoCounterIconRatio = source.AmmoCounterIconRatio,
            AmmoCounterClip = source.AmmoCounterClip,
            StartAmmo = source.StartAmmo
        };
    }

    internal static WeaponMissileConeSoundFields Copy(WeaponMissileConeSoundFields source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new WeaponMissileConeSoundFields
        {
            AliasPointer = source.AliasPointer,
            AliasValuePointer = source.AliasValuePointer,
            Alias = source.Alias,
            AliasAtBasePointer = source.AliasAtBasePointer,
            AliasAtBaseValuePointer = source.AliasAtBaseValuePointer,
            AliasAtBase = source.AliasAtBase,
            RadiusAtTop = source.RadiusAtTop,
            RadiusAtBase = source.RadiusAtBase,
            Height = source.Height,
            OriginOffset = source.OriginOffset,
            VolumeScaleAtCore = source.VolumeScaleAtCore,
            VolumeScaleAtEdge = source.VolumeScaleAtEdge,
            VolumeScaleCoreSize = source.VolumeScaleCoreSize,
            PitchAtTop = source.PitchAtTop,
            PitchAtBottom = source.PitchAtBottom,
            PitchTopSize = source.PitchTopSize,
            PitchBottomSize = source.PitchBottomSize,
            CrossfadeTopSize = source.CrossfadeTopSize,
            CrossfadeBottomSize = source.CrossfadeBottomSize
        };
    }

    internal static WeaponNoteTrackMaps Copy(WeaponNoteTrackMaps source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new WeaponNoteTrackMaps
        {
            SoundMapKeysPointer = source.SoundMapKeysPointer,
            SoundMapKeys = CopyList(source.SoundMapKeys),
            SoundMapValuesPointer = source.SoundMapValuesPointer,
            SoundMapValues = CopyList(source.SoundMapValues),
            RumbleMapKeysPointer = source.RumbleMapKeysPointer,
            RumbleMapKeys = CopyList(source.RumbleMapKeys),
            RumbleMapValuesPointer = source.RumbleMapValuesPointer,
            RumbleMapValues = CopyList(source.RumbleMapValues)
        };
    }

    internal static WeaponOverlayFields Copy(WeaponOverlayFields source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new WeaponOverlayFields
        {
            OverlayMaterials = CopyList(source.OverlayMaterials),
            Reticle = source.Reticle,
            Interface = source.Interface,
            Width = source.Width,
            Height = source.Height,
            WidthSplitscreen = source.WidthSplitscreen,
            HeightSplitscreen = source.HeightSplitscreen
        };
    }

    internal static WeaponPhysicsFields Copy(WeaponPhysicsFields source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new WeaponPhysicsFields
        {
            DualWieldViewModelOffset = source.DualWieldViewModelOffset,
            KillIconRatio = source.KillIconRatio,
            ReloadAmmoAdd = source.ReloadAmmoAdd,
            ReloadStartAdd = source.ReloadStartAdd,
            AmmoDropStockMin = source.AmmoDropStockMin,
            AmmoDropClipPercentMin = source.AmmoDropClipPercentMin,
            AmmoDropClipPercentMax = source.AmmoDropClipPercentMax,
            ExplosionRadius = source.ExplosionRadius,
            ExplosionRadiusMin = source.ExplosionRadiusMin,
            ExplosionInnerDamage = source.ExplosionInnerDamage,
            ExplosionOuterDamage = source.ExplosionOuterDamage,
            DamageConeAngle = source.DamageConeAngle,
            BulletExplosionDamageMultiplier = source.BulletExplosionDamageMultiplier,
            BulletExplosionRadiusMultiplier = source.BulletExplosionRadiusMultiplier,
            ProjectileSpeed = source.ProjectileSpeed,
            ProjectileSpeedUp = source.ProjectileSpeedUp,
            ProjectileSpeedForward = source.ProjectileSpeedForward,
            ProjectileActivateDistance = source.ProjectileActivateDistance,
            ProjectileLifetime = source.ProjectileLifetime,
            TimeToAccelerate = source.TimeToAccelerate,
            ProjectileCurvature = source.ProjectileCurvature
        };
    }

    internal static WeaponPositionalMovementFields Copy(WeaponPositionalMovementFields source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new WeaponPositionalMovementFields
        {
            PositionMoveRate = source.PositionMoveRate,
            PositionProneMoveRate = source.PositionProneMoveRate,
            StandMoveMinSpeed = source.StandMoveMinSpeed,
            DuckedMoveMinSpeed = source.DuckedMoveMinSpeed,
            ProneMoveMinSpeed = source.ProneMoveMinSpeed,
            PositionRotationRate = source.PositionRotationRate,
            PositionProneRotationRate = source.PositionProneRotationRate,
            StandRotationMinSpeed = source.StandRotationMinSpeed,
            DuckedRotationMinSpeed = source.DuckedRotationMinSpeed,
            ProneRotationMinSpeed = source.ProneRotationMinSpeed
        };
    }

    internal static WeaponProjectileFields Copy(WeaponProjectileFields source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new WeaponProjectileFields
        {
            ModelPointer = source.ModelPointer,
            Model = source.Model,
            Explosion = source.Explosion,
            ExplosionEffectPointer = source.ExplosionEffectPointer,
            DudEffectPointer = source.DudEffectPointer,
            ExplosionSoundPointer = source.ExplosionSoundPointer,
            ExplosionSoundValuePointer = source.ExplosionSoundValuePointer,
            ExplosionSound = source.ExplosionSound,
            DudSoundPointer = source.DudSoundPointer,
            DudSoundValuePointer = source.DudSoundValuePointer,
            DudSound = source.DudSound,
            Stickiness = source.Stickiness,
            LowAmmoWarningThreshold = source.LowAmmoWarningThreshold,
            RicochetChance = source.RicochetChance,
            ParallelBouncePointer = source.ParallelBouncePointer,
            ParallelBounce = CopyList(source.ParallelBounce),
            PerpendicularBouncePointer = source.PerpendicularBouncePointer,
            PerpendicularBounce = CopyList(source.PerpendicularBounce),
            TrailEffectPointer = source.TrailEffectPointer,
            BeaconEffectPointer = source.BeaconEffectPointer,
            ProjectileColor = source.ProjectileColor,
            GuidedMissileType = source.GuidedMissileType,
            MaxSteeringAcceleration = source.MaxSteeringAcceleration,
            IgnitionDelay = source.IgnitionDelay,
            IgnitionEffectPointer = source.IgnitionEffectPointer,
            IgnitionSoundPointer = source.IgnitionSoundPointer,
            IgnitionSoundValuePointer = source.IgnitionSoundValuePointer,
            IgnitionSound = source.IgnitionSound,
            AdsAimPitch = source.AdsAimPitch,
            AdsCrosshairInFraction = source.AdsCrosshairInFraction,
            AdsCrosshairOutFraction = source.AdsCrosshairOutFraction,
            GunKickAndDistance = Copy(source.GunKickAndDistance)
        };
    }

    internal static WeaponReticleFields Copy(WeaponReticleFields source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new WeaponReticleFields
        {
            CenterSize = source.CenterSize,
            SideSize = source.SideSize,
            MinOffset = source.MinOffset,
            ActiveType = source.ActiveType
        };
    }

    internal static WeaponRumbleFields Copy(WeaponRumbleFields source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new WeaponRumbleFields
        {
            FireRumblePointer = source.FireRumblePointer,
            FireRumble = source.FireRumble,
            MeleeImpactRumblePointer = source.MeleeImpactRumblePointer,
            MeleeImpactRumble = source.MeleeImpactRumble
        };
    }

    internal static WeaponTailFlags Copy(WeaponTailFlags source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new WeaponTailFlags
        {
            SharedAmmo = source.SharedAmmo,
            LockonSupported = source.LockonSupported,
            RequireLockonToFire = source.RequireLockonToFire,
            BigExplosion = source.BigExplosion,
            NoAdsWhenMagEmpty = source.NoAdsWhenMagEmpty,
            AvoidDropCleanup = source.AvoidDropCleanup,
            InheritsPerks = source.InheritsPerks,
            CrosshairColorChange = source.CrosshairColorChange,
            RifleBullet = source.RifleBullet,
            ArmorPiercing = source.ArmorPiercing,
            BoltAction = source.BoltAction,
            AimDownSight = source.AimDownSight,
            RechamberWhileAds = source.RechamberWhileAds,
            BulletExplosiveDamage = source.BulletExplosiveDamage,
            CookOffHold = source.CookOffHold,
            ClipOnly = source.ClipOnly,
            NoAmmoPickup = source.NoAmmoPickup,
            AdsFireOnly = source.AdsFireOnly,
            CancelAutoHolsterWhenEmpty = source.CancelAutoHolsterWhenEmpty,
            DisableSwitchToWhenEmpty = source.DisableSwitchToWhenEmpty,
            SuppressAmmoReserveDisplay = source.SuppressAmmoReserveDisplay,
            LaserSightDuringNightvision = source.LaserSightDuringNightvision,
            MarkableViewmodel = source.MarkableViewmodel,
            NoDualWield = source.NoDualWield,
            FlipKillIcon = source.FlipKillIcon,
            NoPartialReload = source.NoPartialReload,
            SegmentedReload = source.SegmentedReload,
            BlocksProne = source.BlocksProne,
            Silenced = source.Silenced,
            IsRollingGrenade = source.IsRollingGrenade,
            ProjectileExplosionEffectForceNormalUp = source.ProjectileExplosionEffectForceNormalUp,
            ProjectileImpactExplode = source.ProjectileImpactExplode,
            StickToPlayers = source.StickToPlayers,
            HasDetonator = source.HasDetonator,
            DisableFiring = source.DisableFiring,
            TimedDetonation = source.TimedDetonation,
            Rotate = source.Rotate,
            HoldButtonToThrow = source.HoldButtonToThrow,
            FreezeMovementWhenFiring = source.FreezeMovementWhenFiring,
            ThermalScope = source.ThermalScope,
            AltModeSameWeapon = source.AltModeSameWeapon,
            TurretBarrelSpinEnabled = source.TurretBarrelSpinEnabled,
            MissileConeSoundEnabled = source.MissileConeSoundEnabled,
            MissileConeSoundPitchShiftEnabled = source.MissileConeSoundPitchShiftEnabled,
            MissileConeSoundCrossfadeEnabled = source.MissileConeSoundCrossfadeEnabled,
            OffhandHoldIsCancelable = source.OffhandHoldIsCancelable,
            ReservedPadding = source.ReservedPadding
        };
    }

    internal static WeaponTimingFields Copy(WeaponTimingFields source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new WeaponTimingFields
        {
            FireDelay = source.FireDelay,
            MeleeDelay = source.MeleeDelay,
            MeleeChargeDelay = source.MeleeChargeDelay,
            DetonateDelay = source.DetonateDelay,
            RechamberTime = source.RechamberTime,
            RechamberTimeOneHanded = source.RechamberTimeOneHanded,
            RechamberBoltTime = source.RechamberBoltTime,
            HoldFireTime = source.HoldFireTime,
            DetonateTime = source.DetonateTime,
            MeleeTime = source.MeleeTime,
            MeleeChargeTime = source.MeleeChargeTime,
            ReloadTime = source.ReloadTime,
            ReloadShowRocketTime = source.ReloadShowRocketTime,
            ReloadEmptyTime = source.ReloadEmptyTime,
            ReloadAddTime = source.ReloadAddTime,
            ReloadStartTime = source.ReloadStartTime,
            ReloadStartAddTime = source.ReloadStartAddTime,
            ReloadEndTime = source.ReloadEndTime,
            DropTime = source.DropTime,
            RaiseTime = source.RaiseTime,
            AltDropTime = source.AltDropTime,
            QuickDropTime = source.QuickDropTime,
            QuickRaiseTime = source.QuickRaiseTime,
            BreachRaiseTime = source.BreachRaiseTime,
            EmptyRaiseTime = source.EmptyRaiseTime,
            EmptyDropTime = source.EmptyDropTime,
            SprintInTime = source.SprintInTime,
            SprintLoopTime = source.SprintLoopTime,
            SprintOutTime = source.SprintOutTime,
            StunnedTimeBegin = source.StunnedTimeBegin,
            StunnedTimeLoop = source.StunnedTimeLoop,
            StunnedTimeEnd = source.StunnedTimeEnd,
            NightVisionWearTime = source.NightVisionWearTime,
            NightVisionWearTimeFadeOutEnd = source.NightVisionWearTimeFadeOutEnd,
            NightVisionWearTimePowerUp = source.NightVisionWearTimePowerUp,
            NightVisionRemoveTime = source.NightVisionRemoveTime,
            NightVisionRemoveTimePowerDown = source.NightVisionRemoveTimePowerDown,
            NightVisionRemoveTimeFadeInStart = source.NightVisionRemoveTimeFadeInStart,
            FuseTime = source.FuseTime,
            AiFuseTime = source.AiFuseTime
        };
    }

    internal static WeaponTurnSpeedAndRangeFields Copy(WeaponTurnSpeedAndRangeFields source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new WeaponTurnSpeedAndRangeFields
        {
            MinTurnSpeed = source.MinTurnSpeed,
            MaxTurnSpeed = source.MaxTurnSpeed,
            PitchConvergenceTime = source.PitchConvergenceTime,
            YawConvergenceTime = source.YawConvergenceTime,
            SuppressTime = source.SuppressTime,
            MaxRange = source.MaxRange,
            AnimationHorizontalRotateIncrement = source.AnimationHorizontalRotateIncrement,
            PlayerPositionDistance = source.PlayerPositionDistance,
            ScanSpeed = source.ScanSpeed,
            ScanAcceleration = source.ScanAcceleration
        };
    }

    internal static WeaponTurretFields Copy(WeaponTurretFields source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new WeaponTurretFields
        {
            OverheatSoundPointer = source.OverheatSoundPointer,
            OverheatSoundValuePointer = source.OverheatSoundValuePointer,
            OverheatSound = source.OverheatSound,
            OverheatEffectPointer = source.OverheatEffectPointer,
            BarrelSpinRumblePointer = source.BarrelSpinRumblePointer,
            BarrelSpinRumble = source.BarrelSpinRumble,
            BarrelSpinSpeed = source.BarrelSpinSpeed,
            BarrelSpinUpTime = source.BarrelSpinUpTime,
            BarrelSpinDownTime = source.BarrelSpinDownTime,
            BarrelSpinMaxSoundPointer = source.BarrelSpinMaxSoundPointer,
            BarrelSpinMaxSoundValuePointer = source.BarrelSpinMaxSoundValuePointer,
            BarrelSpinMaxSound = source.BarrelSpinMaxSound,
            BarrelSpinUpSoundPointers = CopyList(source.BarrelSpinUpSoundPointers),
            BarrelSpinUpSoundValuePointers = CopyList(source.BarrelSpinUpSoundValuePointers),
            BarrelSpinUpSoundNames = CopyList(source.BarrelSpinUpSoundNames),
            BarrelSpinDownSoundPointers = CopyList(source.BarrelSpinDownSoundPointers),
            BarrelSpinDownSoundValuePointers = CopyList(source.BarrelSpinDownSoundValuePointers),
            BarrelSpinDownSoundNames = CopyList(source.BarrelSpinDownSoundNames)
        };
    }

    internal static WeaponViewMovementFields Copy(WeaponViewMovementFields source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new WeaponViewMovementFields
        {
            StandMove = source.StandMove,
            StandRotation = source.StandRotation,
            StrafeMove = source.StrafeMove,
            StrafeRotation = source.StrafeRotation,
            DuckedOffset = source.DuckedOffset,
            DuckedMove = source.DuckedMove,
            DuckedRotation = source.DuckedRotation,
            ProneOffset = source.ProneOffset,
            ProneMove = source.ProneMove,
            ProneRotation = source.ProneRotation
        };
    }

    internal static bool Equal(WeaponAsset? left, WeaponAsset? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null &&
        Equal(left.Variant, right.Variant);

    internal static bool Equal(WeaponVariantDef? left, WeaponVariantDef? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null &&
        StringEquals(left.InternalName, right.InternalName) &&
        Equal(left.Definition, right.Definition) &&
        StringEquals(left.DisplayName, right.DisplayName) &&
        ListsEqual(left.HideTags, right.HideTags, ScriptStringEquals) &&
        ListsEqual(left.AnimationNames, right.AnimationNames, StringEquals) &&
        FloatEquals(left.AdsZoomFov, right.AdsZoomFov) &&
        EqualityComparer<int>.Default.Equals(left.AdsTransitionInTime, right.AdsTransitionInTime) &&
        EqualityComparer<int>.Default.Equals(left.AdsTransitionOutTime, right.AdsTransitionOutTime) &&
        EqualityComparer<int>.Default.Equals(left.ClipSize, right.ClipSize) &&
        EqualityComparer<int>.Default.Equals(left.ImpactType, right.ImpactType) &&
        EqualityComparer<int>.Default.Equals(left.FireTime, right.FireTime) &&
        EqualityComparer<int>.Default.Equals(left.DpadIconRatio, right.DpadIconRatio) &&
        FloatEquals(left.PenetrateMultiplier, right.PenetrateMultiplier) &&
        FloatEquals(left.AdsViewKickCenterSpeed, right.AdsViewKickCenterSpeed) &&
        FloatEquals(left.HipViewKickCenterSpeed, right.HipViewKickCenterSpeed) &&
        StringEquals(left.AlternateWeaponName, right.AlternateWeaponName) &&
        EqualityComparer<uint>.Default.Equals(left.AlternateWeaponIndex, right.AlternateWeaponIndex) &&
        EqualityComparer<int>.Default.Equals(left.AlternateRaiseTime, right.AlternateRaiseTime) &&
        ProviderEquals(left.KillIcon, right.KillIcon) &&
        ProviderEquals(left.DpadIcon, right.DpadIcon) &&
        EqualityComparer<int>.Default.Equals(left.DropAmmoMin, right.DropAmmoMin) &&
        EqualityComparer<int>.Default.Equals(left.FirstRaiseTime, right.FirstRaiseTime) &&
        EqualityComparer<int>.Default.Equals(left.DropAmmoMax, right.DropAmmoMax) &&
        FloatEquals(left.AdsDofStart, right.AdsDofStart) &&
        FloatEquals(left.AdsDofEnd, right.AdsDofEnd) &&
        EqualityComparer<ushort>.Default.Equals(left.AccuracyGraphKnotCount, right.AccuracyGraphKnotCount) &&
        EqualityComparer<ushort>.Default.Equals(left.OriginalAccuracyGraphKnotCount, right.OriginalAccuracyGraphKnotCount) &&
        ListsEqual(left.AccuracyGraphKnots, right.AccuracyGraphKnots, Vec2Equals) &&
        ListsEqual(left.OriginalAccuracyGraphKnots, right.OriginalAccuracyGraphKnots, Vec2Equals) &&
        EqualityComparer<byte>.Default.Equals(left.MotionTracker, right.MotionTracker) &&
        EqualityComparer<byte>.Default.Equals(left.Enhanced, right.Enhanced) &&
        EqualityComparer<byte>.Default.Equals(left.DpadIconShowsAmmo, right.DpadIconShowsAmmo) &&
        EqualityComparer<byte>.Default.Equals(left.Padding73, right.Padding73);

    internal static bool Equal(WeaponDef? left, WeaponDef? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null &&
        StringEquals(left.InternalName, right.InternalName) &&
        ListsEqual(left.GunModels, right.GunModels, ProviderEquals) &&
        ProviderEquals(left.HandModel, right.HandModel) &&
        ListsEqual(left.RightHandAnimationNames, right.RightHandAnimationNames, StringEquals) &&
        ListsEqual(left.LeftHandAnimationNames, right.LeftHandAnimationNames, StringEquals) &&
        StringEquals(left.ModeName, right.ModeName) &&
        Equal(left.NoteTrackMaps, right.NoteTrackMaps) &&
        EqualityComparer<int>.Default.Equals(left.PlayerAnimType, right.PlayerAnimType) &&
        EqualityComparer<WeaponType>.Default.Equals(left.WeaponType, right.WeaponType) &&
        EqualityComparer<WeaponClass>.Default.Equals(left.WeaponClass, right.WeaponClass) &&
        EqualityComparer<PenetrateType>.Default.Equals(left.PenetrateType, right.PenetrateType) &&
        EqualityComparer<WeaponInventoryType>.Default.Equals(left.InventoryType, right.InventoryType) &&
        EqualityComparer<WeaponFireType>.Default.Equals(left.FireType, right.FireType) &&
        EqualityComparer<OffhandClass>.Default.Equals(left.OffhandClass, right.OffhandClass) &&
        EqualityComparer<WeaponStance>.Default.Equals(left.Stance, right.Stance) &&
        ListsEqual(left.FlashEffects, right.FlashEffects, ProviderEquals) &&
        ListsEqual(left.SoundAliasNames, right.SoundAliasNames, StringEquals) &&
        ListsEqual(left.BounceSoundNames, right.BounceSoundNames, StringEquals) &&
        ListsEqual(left.Effects, right.Effects, ProviderEquals) &&
        ListsEqual(left.Materials, right.Materials, ProviderEquals) &&
        Equal(left.Reticle, right.Reticle) &&
        Equal(left.ViewMovement, right.ViewMovement) &&
        Equal(left.PositionalMovement, right.PositionalMovement) &&
        ListsEqual(left.WorldGunModels, right.WorldGunModels, ProviderEquals) &&
        ListsEqual(left.WorldModels, right.WorldModels, ProviderEquals) &&
        Equal(left.Icons, right.Icons) &&
        ListsEqual(left.IconMaterials, right.IconMaterials, ProviderEquals) &&
        Equal(left.Ammo, right.Ammo) &&
        Equal(left.Overlay, right.Overlay) &&
        ListsEqual(left.OverlayMaterials, right.OverlayMaterials, ProviderEquals) &&
        Equal(left.Timing, right.Timing) &&
        Equal(left.AimMovementTuning, right.AimMovementTuning) &&
        Equal(left.AdsViewAndSpread, right.AdsViewAndSpread) &&
        ProviderEquals(left.PhysCollmap, right.PhysCollmap) &&
        StringEquals(left.PhysCollmapName, right.PhysCollmapName) &&
        Equal(left.Physics, right.Physics) &&
        Equal(left.Projectile, right.Projectile) &&
        ListsEqual(left.ProjectileEffects, right.ProjectileEffects, ProviderEquals) &&
        ListsEqual(left.ImpactEffects, right.ImpactEffects, ProviderEquals) &&
        ProviderEquals(left.ViewShellEjectEffect, right.ViewShellEjectEffect) &&
        Equal(left.Accuracy, right.Accuracy) &&
        Equal(left.TurnSpeedAndRange, right.TurnSpeedAndRange) &&
        Equal(left.Hints, right.Hints) &&
        StringEquals(left.ScriptName, right.ScriptName) &&
        FloatEquals(left.OOPosAnimLength, right.OOPosAnimLength) &&
        FloatEquals(left.MinDamage, right.MinDamage) &&
        EqualityComparer<int>.Default.Equals(left.MinPlayerDamage, right.MinPlayerDamage) &&
        FloatEquals(left.MaxDamageRange, right.MaxDamageRange) &&
        FloatEquals(left.MinDamageRange, right.MinDamageRange) &&
        FloatEquals(left.DestabilizationRateTime, right.DestabilizationRateTime) &&
        FloatEquals(left.DestabilizationCurvatureMax, right.DestabilizationCurvatureMax) &&
        FloatEquals(left.DestabilizeDistance, right.DestabilizeDistance) &&
        EqualityComparer<int>.Default.Equals(left.DestabilizeDistanceToTimeScale, right.DestabilizeDistanceToTimeScale) &&
        ListsEqual(left.LocationDamageMultipliers, right.LocationDamageMultipliers, FloatEquals) &&
        Equal(left.Rumble, right.Rumble) &&
        ProviderEquals(left.Tracer, right.Tracer) &&
        FloatEquals(left.TurretScopeZoomRate, right.TurretScopeZoomRate) &&
        FloatEquals(left.TurretScopeZoomMin, right.TurretScopeZoomMin) &&
        FloatEquals(left.TurretScopeZoomMax, right.TurretScopeZoomMax) &&
        FloatEquals(left.TurretOverheatUpRate, right.TurretOverheatUpRate) &&
        FloatEquals(left.TurretOverheatDownRate, right.TurretOverheatDownRate) &&
        FloatEquals(left.TurretOverheatPenalty, right.TurretOverheatPenalty) &&
        Equal(left.Turret, right.Turret) &&
        ProviderEquals(left.TurretOverheatEffect, right.TurretOverheatEffect) &&
        Equal(left.MissileConeSound, right.MissileConeSound) &&
        Equal(left.TailFlags, right.TailFlags);

    internal static bool Equal(WeaponAccuracyFields? left, WeaponAccuracyFields? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null &&
        StringEquals(left.GraphName0, right.GraphName0) &&
        StringEquals(left.GraphName1, right.GraphName1) &&
        ListsEqual(left.GraphKnots, right.GraphKnots, Vec2Equals) &&
        ListsEqual(left.OriginalGraphKnots, right.OriginalGraphKnots, Vec2Equals) &&
        EqualityComparer<ushort>.Default.Equals(left.LocalGraphKnotCount, right.LocalGraphKnotCount) &&
        EqualityComparer<ushort>.Default.Equals(left.LocalOriginalGraphKnotCount, right.LocalOriginalGraphKnotCount) &&
        EqualityComparer<int>.Default.Equals(left.AnimationNotifyComparison, right.AnimationNotifyComparison) &&
        FloatEquals(left.LeftArc, right.LeftArc) &&
        FloatEquals(left.RightArc, right.RightArc) &&
        FloatEquals(left.TopArc, right.TopArc) &&
        FloatEquals(left.BottomArc, right.BottomArc) &&
        FloatEquals(left.Accuracy, right.Accuracy) &&
        FloatEquals(left.AiSpread, right.AiSpread) &&
        FloatEquals(left.PlayerSpread, right.PlayerSpread);

    internal static bool Equal(WeaponAdsViewAndSpreadFields? left, WeaponAdsViewAndSpreadFields? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null &&
        FloatEquals(left.AdsBobFactor, right.AdsBobFactor) &&
        FloatEquals(left.AdsViewBobMultiplier, right.AdsViewBobMultiplier) &&
        FloatEquals(left.HipSpreadStandMin, right.HipSpreadStandMin) &&
        FloatEquals(left.HipSpreadDuckedMin, right.HipSpreadDuckedMin) &&
        FloatEquals(left.HipSpreadProneMin, right.HipSpreadProneMin) &&
        FloatEquals(left.HipSpreadStandMax, right.HipSpreadStandMax) &&
        FloatEquals(left.HipSpreadDuckedMax, right.HipSpreadDuckedMax) &&
        FloatEquals(left.HipSpreadProneMax, right.HipSpreadProneMax) &&
        FloatEquals(left.HipSpreadDecayRate, right.HipSpreadDecayRate) &&
        FloatEquals(left.HipSpreadFireAdd, right.HipSpreadFireAdd) &&
        FloatEquals(left.HipSpreadTurnAdd, right.HipSpreadTurnAdd) &&
        FloatEquals(left.HipSpreadMoveAdd, right.HipSpreadMoveAdd) &&
        FloatEquals(left.HipSpreadDuckedDecay, right.HipSpreadDuckedDecay) &&
        FloatEquals(left.HipSpreadProneDecay, right.HipSpreadProneDecay) &&
        FloatEquals(left.HipReticleSidePosition, right.HipReticleSidePosition) &&
        FloatEquals(left.AdsIdleAmount, right.AdsIdleAmount) &&
        FloatEquals(left.HipIdleAmount, right.HipIdleAmount) &&
        FloatEquals(left.AdsIdleSpeed, right.AdsIdleSpeed) &&
        FloatEquals(left.HipIdleSpeed, right.HipIdleSpeed) &&
        FloatEquals(left.IdleCrouchFactor, right.IdleCrouchFactor) &&
        FloatEquals(left.IdleProneFactor, right.IdleProneFactor) &&
        FloatEquals(left.GunMaxPitch, right.GunMaxPitch) &&
        FloatEquals(left.GunMaxYaw, right.GunMaxYaw) &&
        FloatEquals(left.SwayMaxAngle, right.SwayMaxAngle) &&
        FloatEquals(left.SwayLerpSpeed, right.SwayLerpSpeed) &&
        FloatEquals(left.SwayPitchScale, right.SwayPitchScale) &&
        FloatEquals(left.SwayYawScale, right.SwayYawScale) &&
        FloatEquals(left.SwayHorizontalScale, right.SwayHorizontalScale) &&
        FloatEquals(left.SwayVerticalScale, right.SwayVerticalScale) &&
        FloatEquals(left.SwayShellShockScale, right.SwayShellShockScale) &&
        FloatEquals(left.AdsSwayMaxAngle, right.AdsSwayMaxAngle) &&
        FloatEquals(left.AdsSwayLerpSpeed, right.AdsSwayLerpSpeed) &&
        FloatEquals(left.AdsSwayPitchScale, right.AdsSwayPitchScale) &&
        FloatEquals(left.AdsSwayYawScale, right.AdsSwayYawScale) &&
        FloatEquals(left.AdsSwayHorizontalScale, right.AdsSwayHorizontalScale) &&
        FloatEquals(left.AdsSwayVerticalScale, right.AdsSwayVerticalScale) &&
        FloatEquals(left.AdsViewErrorMin, right.AdsViewErrorMin) &&
        FloatEquals(left.AdsViewErrorMax, right.AdsViewErrorMax);

    internal static bool Equal(WeaponAimMovementTuningFields? left, WeaponAimMovementTuningFields? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null &&
        FloatEquals(left.AutoAimRange, right.AutoAimRange) &&
        FloatEquals(left.AimAssistRange, right.AimAssistRange) &&
        FloatEquals(left.AimAssistRangeAds, right.AimAssistRangeAds) &&
        FloatEquals(left.AimPadding, right.AimPadding) &&
        FloatEquals(left.EnemyCrosshairRange, right.EnemyCrosshairRange) &&
        FloatEquals(left.MoveSpeedScale, right.MoveSpeedScale) &&
        FloatEquals(left.AdsMoveSpeedScale, right.AdsMoveSpeedScale) &&
        FloatEquals(left.SprintDurationScale, right.SprintDurationScale) &&
        FloatEquals(left.AdsZoomInFraction, right.AdsZoomInFraction) &&
        FloatEquals(left.AdsZoomOutFraction, right.AdsZoomOutFraction);

    internal static bool Equal(WeaponAmmoFields? left, WeaponAmmoFields? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null &&
        StringEquals(left.AmmoName, right.AmmoName) &&
        EqualityComparer<int>.Default.Equals(left.AmmoIndex, right.AmmoIndex) &&
        StringEquals(left.ClipName, right.ClipName) &&
        EqualityComparer<int>.Default.Equals(left.ClipIndex, right.ClipIndex) &&
        EqualityComparer<int>.Default.Equals(left.MaxAmmo, right.MaxAmmo) &&
        EqualityComparer<int>.Default.Equals(left.ShotCount, right.ShotCount) &&
        StringEquals(left.SharedAmmoCapName, right.SharedAmmoCapName) &&
        EqualityComparer<int>.Default.Equals(left.SharedAmmoCapIndex, right.SharedAmmoCapIndex) &&
        EqualityComparer<int>.Default.Equals(left.SharedAmmoCap, right.SharedAmmoCap) &&
        EqualityComparer<int>.Default.Equals(left.Damage, right.Damage) &&
        EqualityComparer<int>.Default.Equals(left.PlayerDamage, right.PlayerDamage) &&
        EqualityComparer<int>.Default.Equals(left.MeleeDamage, right.MeleeDamage) &&
        EqualityComparer<int>.Default.Equals(left.DamageType, right.DamageType);

    internal static bool Equal(WeaponGunKickAndDistanceFields? left, WeaponGunKickAndDistanceFields? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null &&
        EqualityComparer<int>.Default.Equals(left.AdsGunKickReducedKickBullets, right.AdsGunKickReducedKickBullets) &&
        FloatEquals(left.AdsGunKickReducedKickPercent, right.AdsGunKickReducedKickPercent) &&
        FloatEquals(left.AdsGunKickPitchMin, right.AdsGunKickPitchMin) &&
        FloatEquals(left.AdsGunKickPitchMax, right.AdsGunKickPitchMax) &&
        FloatEquals(left.AdsGunKickYawMin, right.AdsGunKickYawMin) &&
        FloatEquals(left.AdsGunKickYawMax, right.AdsGunKickYawMax) &&
        FloatEquals(left.AdsGunKickAcceleration, right.AdsGunKickAcceleration) &&
        FloatEquals(left.AdsGunKickSpeedMax, right.AdsGunKickSpeedMax) &&
        FloatEquals(left.AdsGunKickSpeedDecay, right.AdsGunKickSpeedDecay) &&
        FloatEquals(left.AdsGunKickStaticDecay, right.AdsGunKickStaticDecay) &&
        FloatEquals(left.AdsViewKickPitchMin, right.AdsViewKickPitchMin) &&
        FloatEquals(left.AdsViewKickPitchMax, right.AdsViewKickPitchMax) &&
        FloatEquals(left.AdsViewKickYawMin, right.AdsViewKickYawMin) &&
        FloatEquals(left.AdsViewKickYawMax, right.AdsViewKickYawMax) &&
        FloatEquals(left.AdsViewScatterMin, right.AdsViewScatterMin) &&
        FloatEquals(left.AdsViewScatterMax, right.AdsViewScatterMax) &&
        FloatEquals(left.AdsSpread, right.AdsSpread) &&
        EqualityComparer<int>.Default.Equals(left.HipGunKickReducedKickBullets, right.HipGunKickReducedKickBullets) &&
        FloatEquals(left.HipGunKickReducedKickPercent, right.HipGunKickReducedKickPercent) &&
        FloatEquals(left.HipGunKickPitchMin, right.HipGunKickPitchMin) &&
        FloatEquals(left.HipGunKickPitchMax, right.HipGunKickPitchMax) &&
        FloatEquals(left.HipGunKickYawMin, right.HipGunKickYawMin) &&
        FloatEquals(left.HipGunKickYawMax, right.HipGunKickYawMax) &&
        FloatEquals(left.HipGunKickAcceleration, right.HipGunKickAcceleration) &&
        FloatEquals(left.HipGunKickSpeedMax, right.HipGunKickSpeedMax) &&
        FloatEquals(left.HipGunKickSpeedDecay, right.HipGunKickSpeedDecay) &&
        FloatEquals(left.HipGunKickStaticDecay, right.HipGunKickStaticDecay) &&
        FloatEquals(left.HipViewKickPitchMin, right.HipViewKickPitchMin) &&
        FloatEquals(left.HipViewKickPitchMax, right.HipViewKickPitchMax) &&
        FloatEquals(left.HipViewKickYawMin, right.HipViewKickYawMin) &&
        FloatEquals(left.HipViewKickYawMax, right.HipViewKickYawMax) &&
        FloatEquals(left.HipViewScatterMin, right.HipViewScatterMin) &&
        FloatEquals(left.HipViewScatterMax, right.HipViewScatterMax) &&
        FloatEquals(left.FightDistance, right.FightDistance) &&
        FloatEquals(left.MaxDistance, right.MaxDistance);

    internal static bool Equal(WeaponHintFields? left, WeaponHintFields? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null &&
        StringEquals(left.UseHintString, right.UseHintString) &&
        StringEquals(left.DropHintString, right.DropHintString) &&
        EqualityComparer<int>.Default.Equals(left.UseHintStringIndex, right.UseHintStringIndex) &&
        EqualityComparer<int>.Default.Equals(left.DropHintStringIndex, right.DropHintStringIndex) &&
        FloatEquals(left.HorizontalViewJitter, right.HorizontalViewJitter) &&
        FloatEquals(left.VerticalViewJitter, right.VerticalViewJitter) &&
        FloatEquals(left.ScanSpeed, right.ScanSpeed) &&
        FloatEquals(left.ScanAcceleration, right.ScanAcceleration) &&
        EqualityComparer<int>.Default.Equals(left.ScanPauseTime, right.ScanPauseTime);

    internal static bool Equal(WeaponIconPointers? left, WeaponIconPointers? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null &&
        EqualityComparer<int>.Default.Equals(left.HudIconRatio, right.HudIconRatio) &&
        EqualityComparer<int>.Default.Equals(left.PickupIconRatio, right.PickupIconRatio) &&
        EqualityComparer<int>.Default.Equals(left.AmmoCounterIconRatio, right.AmmoCounterIconRatio) &&
        EqualityComparer<AmmoCounterClipType>.Default.Equals(left.AmmoCounterClip, right.AmmoCounterClip) &&
        EqualityComparer<int>.Default.Equals(left.StartAmmo, right.StartAmmo);

    internal static bool Equal(WeaponMissileConeSoundFields? left, WeaponMissileConeSoundFields? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null &&
        StringEquals(left.Alias, right.Alias) &&
        StringEquals(left.AliasAtBase, right.AliasAtBase) &&
        FloatEquals(left.RadiusAtTop, right.RadiusAtTop) &&
        FloatEquals(left.RadiusAtBase, right.RadiusAtBase) &&
        FloatEquals(left.Height, right.Height) &&
        FloatEquals(left.OriginOffset, right.OriginOffset) &&
        FloatEquals(left.VolumeScaleAtCore, right.VolumeScaleAtCore) &&
        FloatEquals(left.VolumeScaleAtEdge, right.VolumeScaleAtEdge) &&
        FloatEquals(left.VolumeScaleCoreSize, right.VolumeScaleCoreSize) &&
        FloatEquals(left.PitchAtTop, right.PitchAtTop) &&
        FloatEquals(left.PitchAtBottom, right.PitchAtBottom) &&
        FloatEquals(left.PitchTopSize, right.PitchTopSize) &&
        FloatEquals(left.PitchBottomSize, right.PitchBottomSize) &&
        FloatEquals(left.CrossfadeTopSize, right.CrossfadeTopSize) &&
        FloatEquals(left.CrossfadeBottomSize, right.CrossfadeBottomSize);

    internal static bool Equal(WeaponNoteTrackMaps? left, WeaponNoteTrackMaps? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null &&
        ListsEqual(left.SoundMapKeys, right.SoundMapKeys, ScriptStringEquals) &&
        ListsEqual(left.SoundMapValues, right.SoundMapValues, ScriptStringEquals) &&
        ListsEqual(left.RumbleMapKeys, right.RumbleMapKeys, ScriptStringEquals) &&
        ListsEqual(left.RumbleMapValues, right.RumbleMapValues, ScriptStringEquals);

    internal static bool Equal(WeaponOverlayFields? left, WeaponOverlayFields? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null &&
        EqualityComparer<WeaponOverlayReticle>.Default.Equals(left.Reticle, right.Reticle) &&
        EqualityComparer<WeaponOverlayInterface>.Default.Equals(left.Interface, right.Interface) &&
        EqualityComparer<int>.Default.Equals(left.Width, right.Width) &&
        EqualityComparer<int>.Default.Equals(left.Height, right.Height) &&
        EqualityComparer<int>.Default.Equals(left.WidthSplitscreen, right.WidthSplitscreen) &&
        EqualityComparer<int>.Default.Equals(left.HeightSplitscreen, right.HeightSplitscreen);

    internal static bool Equal(WeaponPhysicsFields? left, WeaponPhysicsFields? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null &&
        FloatEquals(left.DualWieldViewModelOffset, right.DualWieldViewModelOffset) &&
        EqualityComparer<int>.Default.Equals(left.KillIconRatio, right.KillIconRatio) &&
        EqualityComparer<int>.Default.Equals(left.ReloadAmmoAdd, right.ReloadAmmoAdd) &&
        EqualityComparer<int>.Default.Equals(left.ReloadStartAdd, right.ReloadStartAdd) &&
        EqualityComparer<int>.Default.Equals(left.AmmoDropStockMin, right.AmmoDropStockMin) &&
        FloatEquals(left.AmmoDropClipPercentMin, right.AmmoDropClipPercentMin) &&
        FloatEquals(left.AmmoDropClipPercentMax, right.AmmoDropClipPercentMax) &&
        EqualityComparer<int>.Default.Equals(left.ExplosionRadius, right.ExplosionRadius) &&
        EqualityComparer<int>.Default.Equals(left.ExplosionRadiusMin, right.ExplosionRadiusMin) &&
        EqualityComparer<int>.Default.Equals(left.ExplosionInnerDamage, right.ExplosionInnerDamage) &&
        EqualityComparer<int>.Default.Equals(left.ExplosionOuterDamage, right.ExplosionOuterDamage) &&
        FloatEquals(left.DamageConeAngle, right.DamageConeAngle) &&
        FloatEquals(left.BulletExplosionDamageMultiplier, right.BulletExplosionDamageMultiplier) &&
        FloatEquals(left.BulletExplosionRadiusMultiplier, right.BulletExplosionRadiusMultiplier) &&
        EqualityComparer<int>.Default.Equals(left.ProjectileSpeed, right.ProjectileSpeed) &&
        EqualityComparer<int>.Default.Equals(left.ProjectileSpeedUp, right.ProjectileSpeedUp) &&
        EqualityComparer<int>.Default.Equals(left.ProjectileSpeedForward, right.ProjectileSpeedForward) &&
        EqualityComparer<int>.Default.Equals(left.ProjectileActivateDistance, right.ProjectileActivateDistance) &&
        EqualityComparer<int>.Default.Equals(left.ProjectileLifetime, right.ProjectileLifetime) &&
        EqualityComparer<int>.Default.Equals(left.TimeToAccelerate, right.TimeToAccelerate) &&
        FloatEquals(left.ProjectileCurvature, right.ProjectileCurvature);

    internal static bool Equal(WeaponPositionalMovementFields? left, WeaponPositionalMovementFields? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null &&
        FloatEquals(left.PositionMoveRate, right.PositionMoveRate) &&
        FloatEquals(left.PositionProneMoveRate, right.PositionProneMoveRate) &&
        FloatEquals(left.StandMoveMinSpeed, right.StandMoveMinSpeed) &&
        FloatEquals(left.DuckedMoveMinSpeed, right.DuckedMoveMinSpeed) &&
        FloatEquals(left.ProneMoveMinSpeed, right.ProneMoveMinSpeed) &&
        FloatEquals(left.PositionRotationRate, right.PositionRotationRate) &&
        FloatEquals(left.PositionProneRotationRate, right.PositionProneRotationRate) &&
        FloatEquals(left.StandRotationMinSpeed, right.StandRotationMinSpeed) &&
        FloatEquals(left.DuckedRotationMinSpeed, right.DuckedRotationMinSpeed) &&
        FloatEquals(left.ProneRotationMinSpeed, right.ProneRotationMinSpeed);

    internal static bool Equal(WeaponProjectileFields? left, WeaponProjectileFields? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null &&
        ProviderEquals(left.Model, right.Model) &&
        EqualityComparer<WeaponProjectileExplosion>.Default.Equals(left.Explosion, right.Explosion) &&
        StringEquals(left.ExplosionSound, right.ExplosionSound) &&
        StringEquals(left.DudSound, right.DudSound) &&
        EqualityComparer<WeaponStickiness>.Default.Equals(left.Stickiness, right.Stickiness) &&
        EqualityComparer<int>.Default.Equals(left.LowAmmoWarningThreshold, right.LowAmmoWarningThreshold) &&
        FloatEquals(left.RicochetChance, right.RicochetChance) &&
        ListsEqual(left.ParallelBounce, right.ParallelBounce, FloatEquals) &&
        ListsEqual(left.PerpendicularBounce, right.PerpendicularBounce, FloatEquals) &&
        Vec3Equals(left.ProjectileColor, right.ProjectileColor) &&
        EqualityComparer<GuidedMissileType>.Default.Equals(left.GuidedMissileType, right.GuidedMissileType) &&
        FloatEquals(left.MaxSteeringAcceleration, right.MaxSteeringAcceleration) &&
        EqualityComparer<int>.Default.Equals(left.IgnitionDelay, right.IgnitionDelay) &&
        StringEquals(left.IgnitionSound, right.IgnitionSound) &&
        FloatEquals(left.AdsAimPitch, right.AdsAimPitch) &&
        FloatEquals(left.AdsCrosshairInFraction, right.AdsCrosshairInFraction) &&
        FloatEquals(left.AdsCrosshairOutFraction, right.AdsCrosshairOutFraction) &&
        Equal(left.GunKickAndDistance, right.GunKickAndDistance);

    internal static bool Equal(WeaponReticleFields? left, WeaponReticleFields? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null &&
        EqualityComparer<int>.Default.Equals(left.CenterSize, right.CenterSize) &&
        EqualityComparer<int>.Default.Equals(left.SideSize, right.SideSize) &&
        EqualityComparer<int>.Default.Equals(left.MinOffset, right.MinOffset) &&
        EqualityComparer<ActiveReticleType>.Default.Equals(left.ActiveType, right.ActiveType);

    internal static bool Equal(WeaponRumbleFields? left, WeaponRumbleFields? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null &&
        StringEquals(left.FireRumble, right.FireRumble) &&
        StringEquals(left.MeleeImpactRumble, right.MeleeImpactRumble);

    internal static bool Equal(WeaponTailFlags? left, WeaponTailFlags? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null &&
        EqualityComparer<byte>.Default.Equals(left.SharedAmmo, right.SharedAmmo) &&
        EqualityComparer<byte>.Default.Equals(left.LockonSupported, right.LockonSupported) &&
        EqualityComparer<byte>.Default.Equals(left.RequireLockonToFire, right.RequireLockonToFire) &&
        EqualityComparer<byte>.Default.Equals(left.BigExplosion, right.BigExplosion) &&
        EqualityComparer<byte>.Default.Equals(left.NoAdsWhenMagEmpty, right.NoAdsWhenMagEmpty) &&
        EqualityComparer<byte>.Default.Equals(left.AvoidDropCleanup, right.AvoidDropCleanup) &&
        EqualityComparer<byte>.Default.Equals(left.InheritsPerks, right.InheritsPerks) &&
        EqualityComparer<byte>.Default.Equals(left.CrosshairColorChange, right.CrosshairColorChange) &&
        EqualityComparer<byte>.Default.Equals(left.RifleBullet, right.RifleBullet) &&
        EqualityComparer<byte>.Default.Equals(left.ArmorPiercing, right.ArmorPiercing) &&
        EqualityComparer<byte>.Default.Equals(left.BoltAction, right.BoltAction) &&
        EqualityComparer<byte>.Default.Equals(left.AimDownSight, right.AimDownSight) &&
        EqualityComparer<byte>.Default.Equals(left.RechamberWhileAds, right.RechamberWhileAds) &&
        EqualityComparer<byte>.Default.Equals(left.BulletExplosiveDamage, right.BulletExplosiveDamage) &&
        EqualityComparer<byte>.Default.Equals(left.CookOffHold, right.CookOffHold) &&
        EqualityComparer<byte>.Default.Equals(left.ClipOnly, right.ClipOnly) &&
        EqualityComparer<byte>.Default.Equals(left.NoAmmoPickup, right.NoAmmoPickup) &&
        EqualityComparer<byte>.Default.Equals(left.AdsFireOnly, right.AdsFireOnly) &&
        EqualityComparer<byte>.Default.Equals(left.CancelAutoHolsterWhenEmpty, right.CancelAutoHolsterWhenEmpty) &&
        EqualityComparer<byte>.Default.Equals(left.DisableSwitchToWhenEmpty, right.DisableSwitchToWhenEmpty) &&
        EqualityComparer<byte>.Default.Equals(left.SuppressAmmoReserveDisplay, right.SuppressAmmoReserveDisplay) &&
        EqualityComparer<byte>.Default.Equals(left.LaserSightDuringNightvision, right.LaserSightDuringNightvision) &&
        EqualityComparer<byte>.Default.Equals(left.MarkableViewmodel, right.MarkableViewmodel) &&
        EqualityComparer<byte>.Default.Equals(left.NoDualWield, right.NoDualWield) &&
        EqualityComparer<byte>.Default.Equals(left.FlipKillIcon, right.FlipKillIcon) &&
        EqualityComparer<byte>.Default.Equals(left.NoPartialReload, right.NoPartialReload) &&
        EqualityComparer<byte>.Default.Equals(left.SegmentedReload, right.SegmentedReload) &&
        EqualityComparer<byte>.Default.Equals(left.BlocksProne, right.BlocksProne) &&
        EqualityComparer<byte>.Default.Equals(left.Silenced, right.Silenced) &&
        EqualityComparer<byte>.Default.Equals(left.IsRollingGrenade, right.IsRollingGrenade) &&
        EqualityComparer<byte>.Default.Equals(left.ProjectileExplosionEffectForceNormalUp, right.ProjectileExplosionEffectForceNormalUp) &&
        EqualityComparer<byte>.Default.Equals(left.ProjectileImpactExplode, right.ProjectileImpactExplode) &&
        EqualityComparer<byte>.Default.Equals(left.StickToPlayers, right.StickToPlayers) &&
        EqualityComparer<byte>.Default.Equals(left.HasDetonator, right.HasDetonator) &&
        EqualityComparer<byte>.Default.Equals(left.DisableFiring, right.DisableFiring) &&
        EqualityComparer<byte>.Default.Equals(left.TimedDetonation, right.TimedDetonation) &&
        EqualityComparer<byte>.Default.Equals(left.Rotate, right.Rotate) &&
        EqualityComparer<byte>.Default.Equals(left.HoldButtonToThrow, right.HoldButtonToThrow) &&
        EqualityComparer<byte>.Default.Equals(left.FreezeMovementWhenFiring, right.FreezeMovementWhenFiring) &&
        EqualityComparer<byte>.Default.Equals(left.ThermalScope, right.ThermalScope) &&
        EqualityComparer<byte>.Default.Equals(left.AltModeSameWeapon, right.AltModeSameWeapon) &&
        EqualityComparer<byte>.Default.Equals(left.TurretBarrelSpinEnabled, right.TurretBarrelSpinEnabled) &&
        EqualityComparer<byte>.Default.Equals(left.MissileConeSoundEnabled, right.MissileConeSoundEnabled) &&
        EqualityComparer<byte>.Default.Equals(left.MissileConeSoundPitchShiftEnabled, right.MissileConeSoundPitchShiftEnabled) &&
        EqualityComparer<byte>.Default.Equals(left.MissileConeSoundCrossfadeEnabled, right.MissileConeSoundCrossfadeEnabled) &&
        EqualityComparer<byte>.Default.Equals(left.OffhandHoldIsCancelable, right.OffhandHoldIsCancelable) &&
        EqualityComparer<ushort>.Default.Equals(left.ReservedPadding, right.ReservedPadding);

    internal static bool Equal(WeaponTimingFields? left, WeaponTimingFields? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null &&
        EqualityComparer<int>.Default.Equals(left.FireDelay, right.FireDelay) &&
        EqualityComparer<int>.Default.Equals(left.MeleeDelay, right.MeleeDelay) &&
        EqualityComparer<int>.Default.Equals(left.MeleeChargeDelay, right.MeleeChargeDelay) &&
        EqualityComparer<int>.Default.Equals(left.DetonateDelay, right.DetonateDelay) &&
        EqualityComparer<int>.Default.Equals(left.RechamberTime, right.RechamberTime) &&
        EqualityComparer<int>.Default.Equals(left.RechamberTimeOneHanded, right.RechamberTimeOneHanded) &&
        EqualityComparer<int>.Default.Equals(left.RechamberBoltTime, right.RechamberBoltTime) &&
        EqualityComparer<int>.Default.Equals(left.HoldFireTime, right.HoldFireTime) &&
        EqualityComparer<int>.Default.Equals(left.DetonateTime, right.DetonateTime) &&
        EqualityComparer<int>.Default.Equals(left.MeleeTime, right.MeleeTime) &&
        EqualityComparer<int>.Default.Equals(left.MeleeChargeTime, right.MeleeChargeTime) &&
        EqualityComparer<int>.Default.Equals(left.ReloadTime, right.ReloadTime) &&
        EqualityComparer<int>.Default.Equals(left.ReloadShowRocketTime, right.ReloadShowRocketTime) &&
        EqualityComparer<int>.Default.Equals(left.ReloadEmptyTime, right.ReloadEmptyTime) &&
        EqualityComparer<int>.Default.Equals(left.ReloadAddTime, right.ReloadAddTime) &&
        EqualityComparer<int>.Default.Equals(left.ReloadStartTime, right.ReloadStartTime) &&
        EqualityComparer<int>.Default.Equals(left.ReloadStartAddTime, right.ReloadStartAddTime) &&
        EqualityComparer<int>.Default.Equals(left.ReloadEndTime, right.ReloadEndTime) &&
        EqualityComparer<int>.Default.Equals(left.DropTime, right.DropTime) &&
        EqualityComparer<int>.Default.Equals(left.RaiseTime, right.RaiseTime) &&
        EqualityComparer<int>.Default.Equals(left.AltDropTime, right.AltDropTime) &&
        EqualityComparer<int>.Default.Equals(left.QuickDropTime, right.QuickDropTime) &&
        EqualityComparer<int>.Default.Equals(left.QuickRaiseTime, right.QuickRaiseTime) &&
        EqualityComparer<int>.Default.Equals(left.BreachRaiseTime, right.BreachRaiseTime) &&
        EqualityComparer<int>.Default.Equals(left.EmptyRaiseTime, right.EmptyRaiseTime) &&
        EqualityComparer<int>.Default.Equals(left.EmptyDropTime, right.EmptyDropTime) &&
        EqualityComparer<int>.Default.Equals(left.SprintInTime, right.SprintInTime) &&
        EqualityComparer<int>.Default.Equals(left.SprintLoopTime, right.SprintLoopTime) &&
        EqualityComparer<int>.Default.Equals(left.SprintOutTime, right.SprintOutTime) &&
        EqualityComparer<int>.Default.Equals(left.StunnedTimeBegin, right.StunnedTimeBegin) &&
        EqualityComparer<int>.Default.Equals(left.StunnedTimeLoop, right.StunnedTimeLoop) &&
        EqualityComparer<int>.Default.Equals(left.StunnedTimeEnd, right.StunnedTimeEnd) &&
        EqualityComparer<int>.Default.Equals(left.NightVisionWearTime, right.NightVisionWearTime) &&
        EqualityComparer<int>.Default.Equals(left.NightVisionWearTimeFadeOutEnd, right.NightVisionWearTimeFadeOutEnd) &&
        EqualityComparer<int>.Default.Equals(left.NightVisionWearTimePowerUp, right.NightVisionWearTimePowerUp) &&
        EqualityComparer<int>.Default.Equals(left.NightVisionRemoveTime, right.NightVisionRemoveTime) &&
        EqualityComparer<int>.Default.Equals(left.NightVisionRemoveTimePowerDown, right.NightVisionRemoveTimePowerDown) &&
        EqualityComparer<int>.Default.Equals(left.NightVisionRemoveTimeFadeInStart, right.NightVisionRemoveTimeFadeInStart) &&
        EqualityComparer<int>.Default.Equals(left.FuseTime, right.FuseTime) &&
        EqualityComparer<int>.Default.Equals(left.AiFuseTime, right.AiFuseTime);

    internal static bool Equal(WeaponTurnSpeedAndRangeFields? left, WeaponTurnSpeedAndRangeFields? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null &&
        FloatEquals(left.MinTurnSpeed, right.MinTurnSpeed) &&
        FloatEquals(left.MaxTurnSpeed, right.MaxTurnSpeed) &&
        FloatEquals(left.PitchConvergenceTime, right.PitchConvergenceTime) &&
        FloatEquals(left.YawConvergenceTime, right.YawConvergenceTime) &&
        FloatEquals(left.SuppressTime, right.SuppressTime) &&
        FloatEquals(left.MaxRange, right.MaxRange) &&
        FloatEquals(left.AnimationHorizontalRotateIncrement, right.AnimationHorizontalRotateIncrement) &&
        FloatEquals(left.PlayerPositionDistance, right.PlayerPositionDistance) &&
        FloatEquals(left.ScanSpeed, right.ScanSpeed) &&
        FloatEquals(left.ScanAcceleration, right.ScanAcceleration);

    internal static bool Equal(WeaponTurretFields? left, WeaponTurretFields? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null &&
        StringEquals(left.OverheatSound, right.OverheatSound) &&
        StringEquals(left.BarrelSpinRumble, right.BarrelSpinRumble) &&
        FloatEquals(left.BarrelSpinSpeed, right.BarrelSpinSpeed) &&
        FloatEquals(left.BarrelSpinUpTime, right.BarrelSpinUpTime) &&
        FloatEquals(left.BarrelSpinDownTime, right.BarrelSpinDownTime) &&
        StringEquals(left.BarrelSpinMaxSound, right.BarrelSpinMaxSound) &&
        ListsEqual(left.BarrelSpinUpSoundNames, right.BarrelSpinUpSoundNames, StringEquals) &&
        ListsEqual(left.BarrelSpinDownSoundNames, right.BarrelSpinDownSoundNames, StringEquals);

    internal static bool Equal(WeaponViewMovementFields? left, WeaponViewMovementFields? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null &&
        Vec3Equals(left.StandMove, right.StandMove) &&
        Vec3Equals(left.StandRotation, right.StandRotation) &&
        Vec3Equals(left.StrafeMove, right.StrafeMove) &&
        Vec3Equals(left.StrafeRotation, right.StrafeRotation) &&
        Vec3Equals(left.DuckedOffset, right.DuckedOffset) &&
        Vec3Equals(left.DuckedMove, right.DuckedMove) &&
        Vec3Equals(left.DuckedRotation, right.DuckedRotation) &&
        Vec3Equals(left.ProneOffset, right.ProneOffset) &&
        Vec3Equals(left.ProneMove, right.ProneMove) &&
        Vec3Equals(left.ProneRotation, right.ProneRotation);

    private static IReadOnlyList<T> CopyList<T>(IReadOnlyList<T> source) =>
        Array.AsReadOnly(source.ToArray());

    private static bool ListsEqual<T>(
        IReadOnlyList<T> left,
        IReadOnlyList<T> right,
        Func<T, T, bool>? equals = null)
    {
        if (left.Count != right.Count)
            return false;
        equals ??= EqualityComparer<T>.Default.Equals;
        for (int index = 0; index < left.Count; index++)
        {
            if (!equals(left[index], right[index]))
                return false;
        }
        return true;
    }

    private static bool FloatEquals(float left, float right) =>
        BitConverter.SingleToInt32Bits(left) == BitConverter.SingleToInt32Bits(right);

    private static bool Vec2Equals(Vec2 left, Vec2 right) =>
        FloatEquals(left.a, right.a) && FloatEquals(left.b, right.b);

    private static bool Vec3Equals(Vec3 left, Vec3 right) =>
        FloatEquals(left.X, right.X) && FloatEquals(left.Y, right.Y) &&
        FloatEquals(left.Z, right.Z);

    private static bool ScriptStringEquals(
        IW4.FastFiles.Strings.ScriptStringReference left,
        IW4.FastFiles.Strings.ScriptStringReference right) =>
        StringEquals(left.Text, right.Text);

    private static bool ProviderEquals(BaseAsset? left, BaseAsset? right) =>
        ReferenceEquals(left, right) ||
        left is not null && right is not null &&
        left.SerializedAssetType == right.SerializedAssetType &&
        StringEquals(NormalizeName(left.SerializedAssetName),
            NormalizeName(right.SerializedAssetName));

    private static string? NormalizeName(string? value) =>
        value?.Replace('\\', '/').ToLowerInvariant();

    private static bool StringEquals(string? left, string? right) =>
        string.Equals(left, right, StringComparison.Ordinal);
}
