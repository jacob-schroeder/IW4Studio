using IW4.Assets.Assets.Weapon;
using IW4.Assets.Math;

namespace IW4.Studio.Documents;

internal static class WeaponNonFiniteValidation
{
    internal static void Append(List<AssetValidationIssue> issues, WeaponAsset asset)
    {
        Check(issues, asset.Variant, "weapon.variant");
        if (asset.Variant.Definition is not null)
            Check(issues, asset.Variant.Definition, "weapon.definition");
    }

    private static void Check(List<AssetValidationIssue> issues, WeaponAccuracyFields value, string path)
    {
        Check(issues, $"{path}.graphKnots", value.GraphKnots);
        Check(issues, $"{path}.originalGraphKnots", value.OriginalGraphKnots);
        Check(issues, $"{path}.leftArc", value.LeftArc);
        Check(issues, $"{path}.rightArc", value.RightArc);
        Check(issues, $"{path}.topArc", value.TopArc);
        Check(issues, $"{path}.bottomArc", value.BottomArc);
        Check(issues, $"{path}.accuracy", value.Accuracy);
        Check(issues, $"{path}.aiSpread", value.AiSpread);
        Check(issues, $"{path}.playerSpread", value.PlayerSpread);
    }

    private static void Check(List<AssetValidationIssue> issues, WeaponAdsViewAndSpreadFields value, string path)
    {
        Check(issues, $"{path}.adsBobFactor", value.AdsBobFactor);
        Check(issues, $"{path}.adsViewBobMultiplier", value.AdsViewBobMultiplier);
        Check(issues, $"{path}.hipSpreadStandMin", value.HipSpreadStandMin);
        Check(issues, $"{path}.hipSpreadDuckedMin", value.HipSpreadDuckedMin);
        Check(issues, $"{path}.hipSpreadProneMin", value.HipSpreadProneMin);
        Check(issues, $"{path}.hipSpreadStandMax", value.HipSpreadStandMax);
        Check(issues, $"{path}.hipSpreadDuckedMax", value.HipSpreadDuckedMax);
        Check(issues, $"{path}.hipSpreadProneMax", value.HipSpreadProneMax);
        Check(issues, $"{path}.hipSpreadDecayRate", value.HipSpreadDecayRate);
        Check(issues, $"{path}.hipSpreadFireAdd", value.HipSpreadFireAdd);
        Check(issues, $"{path}.hipSpreadTurnAdd", value.HipSpreadTurnAdd);
        Check(issues, $"{path}.hipSpreadMoveAdd", value.HipSpreadMoveAdd);
        Check(issues, $"{path}.hipSpreadDuckedDecay", value.HipSpreadDuckedDecay);
        Check(issues, $"{path}.hipSpreadProneDecay", value.HipSpreadProneDecay);
        Check(issues, $"{path}.hipReticleSidePosition", value.HipReticleSidePosition);
        Check(issues, $"{path}.adsIdleAmount", value.AdsIdleAmount);
        Check(issues, $"{path}.hipIdleAmount", value.HipIdleAmount);
        Check(issues, $"{path}.adsIdleSpeed", value.AdsIdleSpeed);
        Check(issues, $"{path}.hipIdleSpeed", value.HipIdleSpeed);
        Check(issues, $"{path}.idleCrouchFactor", value.IdleCrouchFactor);
        Check(issues, $"{path}.idleProneFactor", value.IdleProneFactor);
        Check(issues, $"{path}.gunMaxPitch", value.GunMaxPitch);
        Check(issues, $"{path}.gunMaxYaw", value.GunMaxYaw);
        Check(issues, $"{path}.swayMaxAngle", value.SwayMaxAngle);
        Check(issues, $"{path}.swayLerpSpeed", value.SwayLerpSpeed);
        Check(issues, $"{path}.swayPitchScale", value.SwayPitchScale);
        Check(issues, $"{path}.swayYawScale", value.SwayYawScale);
        Check(issues, $"{path}.swayHorizontalScale", value.SwayHorizontalScale);
        Check(issues, $"{path}.swayVerticalScale", value.SwayVerticalScale);
        Check(issues, $"{path}.swayShellShockScale", value.SwayShellShockScale);
        Check(issues, $"{path}.adsSwayMaxAngle", value.AdsSwayMaxAngle);
        Check(issues, $"{path}.adsSwayLerpSpeed", value.AdsSwayLerpSpeed);
        Check(issues, $"{path}.adsSwayPitchScale", value.AdsSwayPitchScale);
        Check(issues, $"{path}.adsSwayYawScale", value.AdsSwayYawScale);
        Check(issues, $"{path}.adsSwayHorizontalScale", value.AdsSwayHorizontalScale);
        Check(issues, $"{path}.adsSwayVerticalScale", value.AdsSwayVerticalScale);
        Check(issues, $"{path}.adsViewErrorMin", value.AdsViewErrorMin);
        Check(issues, $"{path}.adsViewErrorMax", value.AdsViewErrorMax);
    }

    private static void Check(List<AssetValidationIssue> issues, WeaponAimMovementTuningFields value, string path)
    {
        Check(issues, $"{path}.autoAimRange", value.AutoAimRange);
        Check(issues, $"{path}.aimAssistRange", value.AimAssistRange);
        Check(issues, $"{path}.aimAssistRangeAds", value.AimAssistRangeAds);
        Check(issues, $"{path}.aimPadding", value.AimPadding);
        Check(issues, $"{path}.enemyCrosshairRange", value.EnemyCrosshairRange);
        Check(issues, $"{path}.moveSpeedScale", value.MoveSpeedScale);
        Check(issues, $"{path}.adsMoveSpeedScale", value.AdsMoveSpeedScale);
        Check(issues, $"{path}.sprintDurationScale", value.SprintDurationScale);
        Check(issues, $"{path}.adsZoomInFraction", value.AdsZoomInFraction);
        Check(issues, $"{path}.adsZoomOutFraction", value.AdsZoomOutFraction);
    }

    private static void Check(List<AssetValidationIssue> issues, WeaponDef value, string path)
    {
        Check(issues, value.ViewMovement, $"{path}.viewMovement");
        Check(issues, value.PositionalMovement, $"{path}.positionalMovement");
        Check(issues, value.AimMovementTuning, $"{path}.aimMovementTuning");
        Check(issues, value.AdsViewAndSpread, $"{path}.adsViewAndSpread");
        Check(issues, value.Physics, $"{path}.physics");
        Check(issues, value.Projectile, $"{path}.projectile");
        Check(issues, value.Accuracy, $"{path}.accuracy");
        Check(issues, value.TurnSpeedAndRange, $"{path}.turnSpeedAndRange");
        Check(issues, value.Hints, $"{path}.hints");
        Check(issues, $"{path}.oOPosAnimLength", value.OOPosAnimLength);
        Check(issues, $"{path}.minDamage", value.MinDamage);
        Check(issues, $"{path}.maxDamageRange", value.MaxDamageRange);
        Check(issues, $"{path}.minDamageRange", value.MinDamageRange);
        Check(issues, $"{path}.destabilizationRateTime", value.DestabilizationRateTime);
        Check(issues, $"{path}.destabilizationCurvatureMax", value.DestabilizationCurvatureMax);
        Check(issues, $"{path}.destabilizeDistance", value.DestabilizeDistance);
        Check(issues, $"{path}.locationDamageMultipliers", value.LocationDamageMultipliers);
        Check(issues, $"{path}.turretScopeZoomRate", value.TurretScopeZoomRate);
        Check(issues, $"{path}.turretScopeZoomMin", value.TurretScopeZoomMin);
        Check(issues, $"{path}.turretScopeZoomMax", value.TurretScopeZoomMax);
        Check(issues, $"{path}.turretOverheatUpRate", value.TurretOverheatUpRate);
        Check(issues, $"{path}.turretOverheatDownRate", value.TurretOverheatDownRate);
        Check(issues, $"{path}.turretOverheatPenalty", value.TurretOverheatPenalty);
        Check(issues, value.Turret, $"{path}.turret");
        Check(issues, value.MissileConeSound, $"{path}.missileConeSound");
    }

    private static void Check(List<AssetValidationIssue> issues, WeaponGunKickAndDistanceFields value, string path)
    {
        Check(issues, $"{path}.adsGunKickReducedKickPercent", value.AdsGunKickReducedKickPercent);
        Check(issues, $"{path}.adsGunKickPitchMin", value.AdsGunKickPitchMin);
        Check(issues, $"{path}.adsGunKickPitchMax", value.AdsGunKickPitchMax);
        Check(issues, $"{path}.adsGunKickYawMin", value.AdsGunKickYawMin);
        Check(issues, $"{path}.adsGunKickYawMax", value.AdsGunKickYawMax);
        Check(issues, $"{path}.adsGunKickAcceleration", value.AdsGunKickAcceleration);
        Check(issues, $"{path}.adsGunKickSpeedMax", value.AdsGunKickSpeedMax);
        Check(issues, $"{path}.adsGunKickSpeedDecay", value.AdsGunKickSpeedDecay);
        Check(issues, $"{path}.adsGunKickStaticDecay", value.AdsGunKickStaticDecay);
        Check(issues, $"{path}.adsViewKickPitchMin", value.AdsViewKickPitchMin);
        Check(issues, $"{path}.adsViewKickPitchMax", value.AdsViewKickPitchMax);
        Check(issues, $"{path}.adsViewKickYawMin", value.AdsViewKickYawMin);
        Check(issues, $"{path}.adsViewKickYawMax", value.AdsViewKickYawMax);
        Check(issues, $"{path}.adsViewScatterMin", value.AdsViewScatterMin);
        Check(issues, $"{path}.adsViewScatterMax", value.AdsViewScatterMax);
        Check(issues, $"{path}.adsSpread", value.AdsSpread);
        Check(issues, $"{path}.hipGunKickReducedKickPercent", value.HipGunKickReducedKickPercent);
        Check(issues, $"{path}.hipGunKickPitchMin", value.HipGunKickPitchMin);
        Check(issues, $"{path}.hipGunKickPitchMax", value.HipGunKickPitchMax);
        Check(issues, $"{path}.hipGunKickYawMin", value.HipGunKickYawMin);
        Check(issues, $"{path}.hipGunKickYawMax", value.HipGunKickYawMax);
        Check(issues, $"{path}.hipGunKickAcceleration", value.HipGunKickAcceleration);
        Check(issues, $"{path}.hipGunKickSpeedMax", value.HipGunKickSpeedMax);
        Check(issues, $"{path}.hipGunKickSpeedDecay", value.HipGunKickSpeedDecay);
        Check(issues, $"{path}.hipGunKickStaticDecay", value.HipGunKickStaticDecay);
        Check(issues, $"{path}.hipViewKickPitchMin", value.HipViewKickPitchMin);
        Check(issues, $"{path}.hipViewKickPitchMax", value.HipViewKickPitchMax);
        Check(issues, $"{path}.hipViewKickYawMin", value.HipViewKickYawMin);
        Check(issues, $"{path}.hipViewKickYawMax", value.HipViewKickYawMax);
        Check(issues, $"{path}.hipViewScatterMin", value.HipViewScatterMin);
        Check(issues, $"{path}.hipViewScatterMax", value.HipViewScatterMax);
        Check(issues, $"{path}.fightDistance", value.FightDistance);
        Check(issues, $"{path}.maxDistance", value.MaxDistance);
    }

    private static void Check(List<AssetValidationIssue> issues, WeaponHintFields value, string path)
    {
        Check(issues, $"{path}.horizontalViewJitter", value.HorizontalViewJitter);
        Check(issues, $"{path}.verticalViewJitter", value.VerticalViewJitter);
        Check(issues, $"{path}.scanSpeed", value.ScanSpeed);
        Check(issues, $"{path}.scanAcceleration", value.ScanAcceleration);
    }

    private static void Check(List<AssetValidationIssue> issues, WeaponMissileConeSoundFields value, string path)
    {
        Check(issues, $"{path}.radiusAtTop", value.RadiusAtTop);
        Check(issues, $"{path}.radiusAtBase", value.RadiusAtBase);
        Check(issues, $"{path}.height", value.Height);
        Check(issues, $"{path}.originOffset", value.OriginOffset);
        Check(issues, $"{path}.volumeScaleAtCore", value.VolumeScaleAtCore);
        Check(issues, $"{path}.volumeScaleAtEdge", value.VolumeScaleAtEdge);
        Check(issues, $"{path}.volumeScaleCoreSize", value.VolumeScaleCoreSize);
        Check(issues, $"{path}.pitchAtTop", value.PitchAtTop);
        Check(issues, $"{path}.pitchAtBottom", value.PitchAtBottom);
        Check(issues, $"{path}.pitchTopSize", value.PitchTopSize);
        Check(issues, $"{path}.pitchBottomSize", value.PitchBottomSize);
        Check(issues, $"{path}.crossfadeTopSize", value.CrossfadeTopSize);
        Check(issues, $"{path}.crossfadeBottomSize", value.CrossfadeBottomSize);
    }

    private static void Check(List<AssetValidationIssue> issues, WeaponPhysicsFields value, string path)
    {
        Check(issues, $"{path}.dualWieldViewModelOffset", value.DualWieldViewModelOffset);
        Check(issues, $"{path}.ammoDropClipPercentMin", value.AmmoDropClipPercentMin);
        Check(issues, $"{path}.ammoDropClipPercentMax", value.AmmoDropClipPercentMax);
        Check(issues, $"{path}.damageConeAngle", value.DamageConeAngle);
        Check(issues, $"{path}.bulletExplosionDamageMultiplier", value.BulletExplosionDamageMultiplier);
        Check(issues, $"{path}.bulletExplosionRadiusMultiplier", value.BulletExplosionRadiusMultiplier);
        Check(issues, $"{path}.projectileCurvature", value.ProjectileCurvature);
    }

    private static void Check(List<AssetValidationIssue> issues, WeaponPositionalMovementFields value, string path)
    {
        Check(issues, $"{path}.positionMoveRate", value.PositionMoveRate);
        Check(issues, $"{path}.positionProneMoveRate", value.PositionProneMoveRate);
        Check(issues, $"{path}.standMoveMinSpeed", value.StandMoveMinSpeed);
        Check(issues, $"{path}.duckedMoveMinSpeed", value.DuckedMoveMinSpeed);
        Check(issues, $"{path}.proneMoveMinSpeed", value.ProneMoveMinSpeed);
        Check(issues, $"{path}.positionRotationRate", value.PositionRotationRate);
        Check(issues, $"{path}.positionProneRotationRate", value.PositionProneRotationRate);
        Check(issues, $"{path}.standRotationMinSpeed", value.StandRotationMinSpeed);
        Check(issues, $"{path}.duckedRotationMinSpeed", value.DuckedRotationMinSpeed);
        Check(issues, $"{path}.proneRotationMinSpeed", value.ProneRotationMinSpeed);
    }

    private static void Check(List<AssetValidationIssue> issues, WeaponProjectileFields value, string path)
    {
        Check(issues, $"{path}.ricochetChance", value.RicochetChance);
        Check(issues, $"{path}.parallelBounce", value.ParallelBounce);
        Check(issues, $"{path}.perpendicularBounce", value.PerpendicularBounce);
        Check(issues, $"{path}.projectileColor", value.ProjectileColor);
        Check(issues, $"{path}.maxSteeringAcceleration", value.MaxSteeringAcceleration);
        Check(issues, $"{path}.adsAimPitch", value.AdsAimPitch);
        Check(issues, $"{path}.adsCrosshairInFraction", value.AdsCrosshairInFraction);
        Check(issues, $"{path}.adsCrosshairOutFraction", value.AdsCrosshairOutFraction);
        Check(issues, value.GunKickAndDistance, $"{path}.gunKickAndDistance");
    }

    private static void Check(List<AssetValidationIssue> issues, WeaponTurnSpeedAndRangeFields value, string path)
    {
        Check(issues, $"{path}.minTurnSpeed", value.MinTurnSpeed);
        Check(issues, $"{path}.maxTurnSpeed", value.MaxTurnSpeed);
        Check(issues, $"{path}.pitchConvergenceTime", value.PitchConvergenceTime);
        Check(issues, $"{path}.yawConvergenceTime", value.YawConvergenceTime);
        Check(issues, $"{path}.suppressTime", value.SuppressTime);
        Check(issues, $"{path}.maxRange", value.MaxRange);
        Check(issues, $"{path}.animationHorizontalRotateIncrement", value.AnimationHorizontalRotateIncrement);
        Check(issues, $"{path}.playerPositionDistance", value.PlayerPositionDistance);
        Check(issues, $"{path}.scanSpeed", value.ScanSpeed);
        Check(issues, $"{path}.scanAcceleration", value.ScanAcceleration);
    }

    private static void Check(List<AssetValidationIssue> issues, WeaponTurretFields value, string path)
    {
        Check(issues, $"{path}.barrelSpinSpeed", value.BarrelSpinSpeed);
        Check(issues, $"{path}.barrelSpinUpTime", value.BarrelSpinUpTime);
        Check(issues, $"{path}.barrelSpinDownTime", value.BarrelSpinDownTime);
    }

    private static void Check(List<AssetValidationIssue> issues, WeaponVariantDef value, string path)
    {
        Check(issues, $"{path}.adsZoomFov", value.AdsZoomFov);
        Check(issues, $"{path}.penetrateMultiplier", value.PenetrateMultiplier);
        Check(issues, $"{path}.adsViewKickCenterSpeed", value.AdsViewKickCenterSpeed);
        Check(issues, $"{path}.hipViewKickCenterSpeed", value.HipViewKickCenterSpeed);
        Check(issues, $"{path}.adsDofStart", value.AdsDofStart);
        Check(issues, $"{path}.adsDofEnd", value.AdsDofEnd);
        Check(issues, $"{path}.accuracyGraphKnots", value.AccuracyGraphKnots);
        Check(issues, $"{path}.originalAccuracyGraphKnots", value.OriginalAccuracyGraphKnots);
    }

    private static void Check(List<AssetValidationIssue> issues, WeaponViewMovementFields value, string path)
    {
        Check(issues, $"{path}.standMove", value.StandMove);
        Check(issues, $"{path}.standRotation", value.StandRotation);
        Check(issues, $"{path}.strafeMove", value.StrafeMove);
        Check(issues, $"{path}.strafeRotation", value.StrafeRotation);
        Check(issues, $"{path}.duckedOffset", value.DuckedOffset);
        Check(issues, $"{path}.duckedMove", value.DuckedMove);
        Check(issues, $"{path}.duckedRotation", value.DuckedRotation);
        Check(issues, $"{path}.proneOffset", value.ProneOffset);
        Check(issues, $"{path}.proneMove", value.ProneMove);
        Check(issues, $"{path}.proneRotation", value.ProneRotation);
    }

    private static void Check(List<AssetValidationIssue> issues, string path, float value)
    {
        if (!float.IsFinite(value))
            issues.Add(new AssetValidationIssue(path,
                "Imported non-finite float is preserved; replace it with a finite value before editing this field.",
                AssetValidationSeverity.Warning));
    }

    private static void Check(List<AssetValidationIssue> issues, string path, Vec2 value)
    {
        Check(issues, $"{path}.x", value.a);
        Check(issues, $"{path}.y", value.b);
    }

    private static void Check(List<AssetValidationIssue> issues, string path, Vec3 value)
    {
        Check(issues, $"{path}.x", value.X);
        Check(issues, $"{path}.y", value.Y);
        Check(issues, $"{path}.z", value.Z);
    }

    private static void Check(List<AssetValidationIssue> issues, string path, IReadOnlyList<float> values)
    {
        for (int index = 0; index < values.Count; index++)
            Check(issues, $"{path}[{index}]", values[index]);
    }

    private static void Check(List<AssetValidationIssue> issues, string path, IReadOnlyList<Vec2> values)
    {
        for (int index = 0; index < values.Count; index++)
            Check(issues, $"{path}[{index}]", values[index]);
    }

}
