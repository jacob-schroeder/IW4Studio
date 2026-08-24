using IW4.AssetExchange.SourceFormat.InfoString;
using IW4.Assets.Assets.Weapon;

namespace IW4.AssetExchange.SourceFormat.Weapon;

internal static partial class WeaponInfoStringSource
{
    private static void AddProjectileAndAim(
        InfoStringSourceWriter source,
        WeaponVariantDef variant,
        WeaponDef definition,
        string assetName)
    {
        WeaponProjectileFields projectile = definition.Projectile;
        AddBounceValues(
            source,
            "parallel",
            projectile.ParallelBouncePointer.Raw,
            projectile.ParallelBounce,
            $"Weapon '{assetName}' parallel bounce table");
        AddBounceValues(
            source,
            "perpendicular",
            projectile.PerpendicularBouncePointer.Raw,
            projectile.PerpendicularBounce,
            $"Weapon '{assetName}' perpendicular bounce table");

        source.AddString("projTrailEffect", Referenced(
            projectile.TrailEffectPointer.Raw,
            projectile.TrailEffect,
            $"Weapon '{assetName}' projectile trail effect"));
        source.AddString("projBeaconEffect", Referenced(
            projectile.BeaconEffectPointer.Raw,
            projectile.BeaconEffect,
            $"Weapon '{assetName}' projectile beacon effect"));
        source.AddFloat("projectileRed", projectile.ProjectileColor.X);
        source.AddFloat("projectileGreen", projectile.ProjectileColor.Y);
        source.AddFloat("projectileBlue", projectile.ProjectileColor.Z);
        source.AddEnum(
            "guidedMissileType",
            (int)projectile.GuidedMissileType,
            GuidedMissileNames,
            $"Weapon '{assetName}' guided missile type");
        source.AddFloat("maxSteeringAccel", projectile.MaxSteeringAcceleration);
        source.AddInt("projIgnitionDelay", projectile.IgnitionDelay);
        source.AddString("projIgnitionEffect", Referenced(
            projectile.IgnitionEffectPointer.Raw,
            projectile.IgnitionEffect,
            $"Weapon '{assetName}' projectile ignition effect"));
        source.AddString("projIgnitionSound", Materialized(
            Presence(projectile.IgnitionSoundPointer.Raw, projectile.IgnitionSoundValuePointer.Raw),
            projectile.IgnitionSound,
            $"Weapon '{assetName}' projectile ignition sound"));

        source.AddMilliseconds("adsTransInTime", variant.AdsTransitionInTime);
        source.AddMilliseconds("adsTransOutTime", variant.AdsTransitionOutTime);
        WeaponAdsViewAndSpreadFields ads = definition.AdsViewAndSpread;
        source.AddFloat("adsIdleAmount", ads.AdsIdleAmount);
        source.AddFloat("adsIdleSpeed", ads.AdsIdleSpeed);
        source.AddFloat("adsZoomFov", variant.AdsZoomFov);
        WeaponAimMovementTuningFields aim = definition.AimMovementTuning;
        source.AddFloat("adsZoomInFrac", aim.AdsZoomInFraction);
        source.AddFloat("adsZoomOutFrac", aim.AdsZoomOutFraction);

        WeaponOverlayFields overlay = definition.Overlay;
        source.AddString("adsOverlayShader", Referenced(
            overlay.MaterialPointer.Raw,
            overlay.Material,
            $"Weapon '{assetName}' ADS overlay material"));
        source.AddString("adsOverlayShaderLowRes", Referenced(
            overlay.MaterialLowResPointer.Raw,
            overlay.MaterialLowRes,
            $"Weapon '{assetName}' low-resolution ADS overlay material"));
        source.AddString("adsOverlayShaderEMP", Referenced(
            overlay.MaterialEmpPointer.Raw,
            overlay.MaterialEmp,
            $"Weapon '{assetName}' EMP ADS overlay material"));
        source.AddString("adsOverlayShaderEMPLowRes", Referenced(
            overlay.MaterialEmpLowResPointer.Raw,
            overlay.MaterialEmpLowRes,
            $"Weapon '{assetName}' low-resolution EMP ADS overlay material"));
        source.AddEnum(
            "adsOverlayReticle",
            (int)overlay.Reticle,
            OverlayReticleNames,
            $"Weapon '{assetName}' ADS overlay reticle");
        source.AddEnum(
            "adsOverlayInterface",
            (int)overlay.Interface,
            OverlayInterfaceNames,
            $"Weapon '{assetName}' ADS overlay interface");
        source.AddFloat("adsOverlayWidth", overlay.Width);
        source.AddFloat("adsOverlayHeight", overlay.Height);
        source.AddFloat("adsOverlayWidthSplitscreen", overlay.WidthSplitscreen);
        source.AddFloat("adsOverlayHeightSplitscreen", overlay.HeightSplitscreen);
        source.AddFloat("adsBobFactor", ads.AdsBobFactor);
        source.AddFloat("adsViewBobMult", ads.AdsViewBobMultiplier);
        source.AddFloat("adsAimPitch", projectile.AdsAimPitch);
        source.AddFloat("adsCrosshairInFrac", projectile.AdsCrosshairInFraction);
        source.AddFloat("adsCrosshairOutFrac", projectile.AdsCrosshairOutFraction);

        WeaponAccuracyFields accuracy = definition.Accuracy;
        source.AddMilliseconds("adsReloadTransTime", accuracy.PositionReloadTransitionTime);
        WeaponGunKickAndDistanceFields kick = projectile.GunKickAndDistance;
        RequireUnexposedZero(kick.AdsViewScatterMin, "ADS minimum view scatter", assetName);
        RequireUnexposedZero(kick.AdsViewScatterMax, "ADS maximum view scatter", assetName);
        RequireUnexposedZero(kick.HipViewScatterMin, "hip minimum view scatter", assetName);
        RequireUnexposedZero(kick.HipViewScatterMax, "hip maximum view scatter", assetName);
        source.AddInt("adsGunKickReducedKickBullets", kick.AdsGunKickReducedKickBullets);
        source.AddFloat("adsGunKickReducedKickPercent", kick.AdsGunKickReducedKickPercent);
        source.AddFloat("adsGunKickPitchMin", kick.AdsGunKickPitchMin);
        source.AddFloat("adsGunKickPitchMax", kick.AdsGunKickPitchMax);
        source.AddFloat("adsGunKickYawMin", kick.AdsGunKickYawMin);
        source.AddFloat("adsGunKickYawMax", kick.AdsGunKickYawMax);
        source.AddFloat("adsGunKickAccel", kick.AdsGunKickAcceleration);
        source.AddFloat("adsGunKickSpeedMax", kick.AdsGunKickSpeedMax);
        source.AddFloat("adsGunKickSpeedDecay", kick.AdsGunKickSpeedDecay);
        source.AddFloat("adsGunKickStaticDecay", kick.AdsGunKickStaticDecay);
        source.AddFloat("adsViewKickPitchMin", kick.AdsViewKickPitchMin);
        source.AddFloat("adsViewKickPitchMax", kick.AdsViewKickPitchMax);
        source.AddFloat("adsViewKickYawMin", kick.AdsViewKickYawMin);
        source.AddFloat("adsViewKickYawMax", kick.AdsViewKickYawMax);
        source.AddFloat("adsViewKickCenterSpeed", variant.AdsViewKickCenterSpeed);
        source.AddFloat("adsSpread", kick.AdsSpread);

        // OAT lists this key twice. InfoString keeps its first insertion
        // position and updates the value, so adding it again preserves that.
        source.AddEnum(
            "guidedMissileType",
            (int)projectile.GuidedMissileType,
            GuidedMissileNames,
            $"Weapon '{assetName}' guided missile type");
        source.AddFloat("hipSpreadStandMin", ads.HipSpreadStandMin);
        source.AddFloat("hipSpreadDuckedMin", ads.HipSpreadDuckedMin);
        source.AddFloat("hipSpreadProneMin", ads.HipSpreadProneMin);
        source.AddFloat("hipSpreadMax", ads.HipSpreadStandMax);
        source.AddFloat("hipSpreadDuckedMax", ads.HipSpreadDuckedMax);
        source.AddFloat("hipSpreadProneMax", ads.HipSpreadProneMax);
        source.AddFloat("hipSpreadDecayRate", ads.HipSpreadDecayRate);
        source.AddFloat("hipSpreadFireAdd", ads.HipSpreadFireAdd);
        source.AddFloat("hipSpreadTurnAdd", ads.HipSpreadTurnAdd);
        source.AddFloat("hipSpreadMoveAdd", ads.HipSpreadMoveAdd);
        source.AddFloat("hipSpreadDuckedDecay", ads.HipSpreadDuckedDecay);
        source.AddFloat("hipSpreadProneDecay", ads.HipSpreadProneDecay);
        source.AddFloat("hipReticleSidePos", ads.HipReticleSidePosition);
        source.AddFloat("hipIdleAmount", ads.HipIdleAmount);
        source.AddFloat("hipIdleSpeed", ads.HipIdleSpeed);
        source.AddInt("hipGunKickReducedKickBullets", kick.HipGunKickReducedKickBullets);
        source.AddFloat("hipGunKickReducedKickPercent", kick.HipGunKickReducedKickPercent);
        source.AddFloat("hipGunKickPitchMin", kick.HipGunKickPitchMin);
        source.AddFloat("hipGunKickPitchMax", kick.HipGunKickPitchMax);
        source.AddFloat("hipGunKickYawMin", kick.HipGunKickYawMin);
        source.AddFloat("hipGunKickYawMax", kick.HipGunKickYawMax);
        source.AddFloat("hipGunKickAccel", kick.HipGunKickAcceleration);
        source.AddFloat("hipGunKickSpeedMax", kick.HipGunKickSpeedMax);
        source.AddFloat("hipGunKickSpeedDecay", kick.HipGunKickSpeedDecay);
        source.AddFloat("hipGunKickStaticDecay", kick.HipGunKickStaticDecay);
        source.AddFloat("hipViewKickPitchMin", kick.HipViewKickPitchMin);
        source.AddFloat("hipViewKickPitchMax", kick.HipViewKickPitchMax);
        source.AddFloat("hipViewKickYawMin", kick.HipViewKickYawMin);
        source.AddFloat("hipViewKickYawMax", kick.HipViewKickYawMax);
        source.AddFloat("hipViewKickCenterSpeed", variant.HipViewKickCenterSpeed);
        source.AddFloat("leftArc", accuracy.LeftArc);
        source.AddFloat("rightArc", accuracy.RightArc);
        source.AddFloat("topArc", accuracy.TopArc);
        source.AddFloat("bottomArc", accuracy.BottomArc);
        source.AddFloat("accuracy", accuracy.Accuracy);
        source.AddFloat("aiSpread", accuracy.AiSpread);
        source.AddFloat("playerSpread", accuracy.PlayerSpread);

        WeaponTurnSpeedAndRangeFields turn = definition.TurnSpeedAndRange;
        source.AddFloat("maxVertTurnSpeed", turn.MaxVerticalTurnSpeed);
        source.AddFloat("maxHorTurnSpeed", turn.MaxHorizontalTurnSpeed);
        source.AddFloat("minVertTurnSpeed", turn.MinVerticalTurnSpeed);
        source.AddFloat("minHorTurnSpeed", turn.MinHorizontalTurnSpeed);
        source.AddFloat("pitchConvergenceTime", turn.PitchConvergenceTime);
        source.AddFloat("yawConvergenceTime", turn.YawConvergenceTime);
        source.AddFloat("suppressionTime", turn.SuppressionTime);
        source.AddFloat("maxRange", turn.MaxRange);
        source.AddFloat("animHorRotateInc", turn.AnimationHorizontalRotateIncrement);
        source.AddFloat("playerPositionDist", turn.PlayerPositionDistance);
        source.AddEnum(
            "stance",
            (int)definition.Stance,
            ["stand", "duck", "prone"],
            $"Weapon '{assetName}' stance");

        WeaponHintFields hints = definition.Hints;
        source.AddString("useHintString", Materialized(
            hints.UseHintStringPointer.Raw,
            hints.UseHintString,
            $"Weapon '{assetName}' use hint"));
        source.AddString("dropHintString", Materialized(
            hints.DropHintStringPointer.Raw,
            hints.DropHintString,
            $"Weapon '{assetName}' drop hint"));
        source.AddFloat("horizViewJitter", hints.HorizontalViewJitter);
        source.AddFloat("vertViewJitter", hints.VerticalViewJitter);
        source.AddFloat("scanSpeed", hints.ScanSpeed);
        source.AddFloat("scanAccel", hints.ScanAcceleration);
        source.AddMilliseconds("scanPauseTime", hints.ScanPauseTime);
        source.AddFloat("fightDist", kick.FightDistance);
        source.AddFloat("maxDist", kick.MaxDistance);
        source.AddString("aiVsAiAccuracyGraph", Materialized(
            accuracy.AiVsAiGraphNamePointer.Raw,
            accuracy.AiVsAiGraphName,
            $"Weapon '{assetName}' AI-vs-AI accuracy graph name"));
        source.AddString("aiVsPlayerAccuracyGraph", Materialized(
            accuracy.AiVsPlayerGraphNamePointer.Raw,
            accuracy.AiVsPlayerGraphName,
            $"Weapon '{assetName}' AI-vs-player accuracy graph name"));
    }

    private static void AddBounceValues(
        InfoStringSourceWriter source,
        string prefix,
        int pointerRaw,
        IReadOnlyList<float> values,
        string field)
    {
        RequireList(pointerRaw, values.Count, SurfaceNames.Length, field);
        for (int index = 0; index < SurfaceNames.Length; index++)
        {
            source.AddFloat(
                $"{prefix}{SurfaceNames[index]}Bounce",
                values.Count == 0 ? 0.0f : values[index]);
        }
    }

    private static void RequireUnexposedZero(
        float value,
        string field,
        string assetName)
    {
        if (!float.IsFinite(value) || value != 0.0f)
        {
            throw new InvalidDataException(
                $"Weapon '{assetName}' has {field} {value}, which the IW4 source format does not expose.");
        }
    }
}
