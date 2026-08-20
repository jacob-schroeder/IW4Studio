using IW4.Assets.Assets.Weapon;
using IW4.Assets.Math;

namespace IW4.Studio.Documents;

internal static class WeaponFiniteMutation
{
    internal static void Ensure(WeaponAccuracyFields previous, WeaponAccuracyFields value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(value);
        Ensure(previous.GraphKnots, value.GraphKnots, parameterName);
        Ensure(previous.OriginalGraphKnots, value.OriginalGraphKnots, parameterName);
        Ensure(previous.LeftArc, value.LeftArc, parameterName);
        Ensure(previous.RightArc, value.RightArc, parameterName);
        Ensure(previous.TopArc, value.TopArc, parameterName);
        Ensure(previous.BottomArc, value.BottomArc, parameterName);
        Ensure(previous.Accuracy, value.Accuracy, parameterName);
        Ensure(previous.AiSpread, value.AiSpread, parameterName);
        Ensure(previous.PlayerSpread, value.PlayerSpread, parameterName);
    }

    internal static void Ensure(WeaponAdsViewAndSpreadFields previous, WeaponAdsViewAndSpreadFields value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(value);
        Ensure(previous.AdsBobFactor, value.AdsBobFactor, parameterName);
        Ensure(previous.AdsViewBobMultiplier, value.AdsViewBobMultiplier, parameterName);
        Ensure(previous.HipSpreadStandMin, value.HipSpreadStandMin, parameterName);
        Ensure(previous.HipSpreadDuckedMin, value.HipSpreadDuckedMin, parameterName);
        Ensure(previous.HipSpreadProneMin, value.HipSpreadProneMin, parameterName);
        Ensure(previous.HipSpreadStandMax, value.HipSpreadStandMax, parameterName);
        Ensure(previous.HipSpreadDuckedMax, value.HipSpreadDuckedMax, parameterName);
        Ensure(previous.HipSpreadProneMax, value.HipSpreadProneMax, parameterName);
        Ensure(previous.HipSpreadDecayRate, value.HipSpreadDecayRate, parameterName);
        Ensure(previous.HipSpreadFireAdd, value.HipSpreadFireAdd, parameterName);
        Ensure(previous.HipSpreadTurnAdd, value.HipSpreadTurnAdd, parameterName);
        Ensure(previous.HipSpreadMoveAdd, value.HipSpreadMoveAdd, parameterName);
        Ensure(previous.HipSpreadDuckedDecay, value.HipSpreadDuckedDecay, parameterName);
        Ensure(previous.HipSpreadProneDecay, value.HipSpreadProneDecay, parameterName);
        Ensure(previous.HipReticleSidePosition, value.HipReticleSidePosition, parameterName);
        Ensure(previous.AdsIdleAmount, value.AdsIdleAmount, parameterName);
        Ensure(previous.HipIdleAmount, value.HipIdleAmount, parameterName);
        Ensure(previous.AdsIdleSpeed, value.AdsIdleSpeed, parameterName);
        Ensure(previous.HipIdleSpeed, value.HipIdleSpeed, parameterName);
        Ensure(previous.IdleCrouchFactor, value.IdleCrouchFactor, parameterName);
        Ensure(previous.IdleProneFactor, value.IdleProneFactor, parameterName);
        Ensure(previous.GunMaxPitch, value.GunMaxPitch, parameterName);
        Ensure(previous.GunMaxYaw, value.GunMaxYaw, parameterName);
        Ensure(previous.SwayMaxAngle, value.SwayMaxAngle, parameterName);
        Ensure(previous.SwayLerpSpeed, value.SwayLerpSpeed, parameterName);
        Ensure(previous.SwayPitchScale, value.SwayPitchScale, parameterName);
        Ensure(previous.SwayYawScale, value.SwayYawScale, parameterName);
        Ensure(previous.SwayHorizontalScale, value.SwayHorizontalScale, parameterName);
        Ensure(previous.SwayVerticalScale, value.SwayVerticalScale, parameterName);
        Ensure(previous.SwayShellShockScale, value.SwayShellShockScale, parameterName);
        Ensure(previous.AdsSwayMaxAngle, value.AdsSwayMaxAngle, parameterName);
        Ensure(previous.AdsSwayLerpSpeed, value.AdsSwayLerpSpeed, parameterName);
        Ensure(previous.AdsSwayPitchScale, value.AdsSwayPitchScale, parameterName);
        Ensure(previous.AdsSwayYawScale, value.AdsSwayYawScale, parameterName);
        Ensure(previous.AdsSwayHorizontalScale, value.AdsSwayHorizontalScale, parameterName);
        Ensure(previous.AdsSwayVerticalScale, value.AdsSwayVerticalScale, parameterName);
        Ensure(previous.AdsViewErrorMin, value.AdsViewErrorMin, parameterName);
        Ensure(previous.AdsViewErrorMax, value.AdsViewErrorMax, parameterName);
    }

    internal static void Ensure(WeaponAimMovementTuningFields previous, WeaponAimMovementTuningFields value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(value);
        Ensure(previous.AutoAimRange, value.AutoAimRange, parameterName);
        Ensure(previous.AimAssistRange, value.AimAssistRange, parameterName);
        Ensure(previous.AimAssistRangeAds, value.AimAssistRangeAds, parameterName);
        Ensure(previous.AimPadding, value.AimPadding, parameterName);
        Ensure(previous.EnemyCrosshairRange, value.EnemyCrosshairRange, parameterName);
        Ensure(previous.MoveSpeedScale, value.MoveSpeedScale, parameterName);
        Ensure(previous.AdsMoveSpeedScale, value.AdsMoveSpeedScale, parameterName);
        Ensure(previous.SprintDurationScale, value.SprintDurationScale, parameterName);
        Ensure(previous.AdsZoomInFraction, value.AdsZoomInFraction, parameterName);
        Ensure(previous.AdsZoomOutFraction, value.AdsZoomOutFraction, parameterName);
    }

    internal static void Ensure(WeaponDef previous, WeaponDef value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(value);
        Ensure(previous.ViewMovement, value.ViewMovement, parameterName);
        Ensure(previous.PositionalMovement, value.PositionalMovement, parameterName);
        Ensure(previous.AimMovementTuning, value.AimMovementTuning, parameterName);
        Ensure(previous.AdsViewAndSpread, value.AdsViewAndSpread, parameterName);
        Ensure(previous.Physics, value.Physics, parameterName);
        Ensure(previous.Projectile, value.Projectile, parameterName);
        Ensure(previous.Accuracy, value.Accuracy, parameterName);
        Ensure(previous.TurnSpeedAndRange, value.TurnSpeedAndRange, parameterName);
        Ensure(previous.Hints, value.Hints, parameterName);
        Ensure(previous.OOPosAnimLength, value.OOPosAnimLength, parameterName);
        Ensure(previous.MinDamage, value.MinDamage, parameterName);
        Ensure(previous.MaxDamageRange, value.MaxDamageRange, parameterName);
        Ensure(previous.MinDamageRange, value.MinDamageRange, parameterName);
        Ensure(previous.DestabilizationRateTime, value.DestabilizationRateTime, parameterName);
        Ensure(previous.DestabilizationCurvatureMax, value.DestabilizationCurvatureMax, parameterName);
        Ensure(previous.DestabilizeDistance, value.DestabilizeDistance, parameterName);
        Ensure(previous.LocationDamageMultipliers, value.LocationDamageMultipliers, parameterName);
        Ensure(previous.TurretScopeZoomRate, value.TurretScopeZoomRate, parameterName);
        Ensure(previous.TurretScopeZoomMin, value.TurretScopeZoomMin, parameterName);
        Ensure(previous.TurretScopeZoomMax, value.TurretScopeZoomMax, parameterName);
        Ensure(previous.TurretOverheatUpRate, value.TurretOverheatUpRate, parameterName);
        Ensure(previous.TurretOverheatDownRate, value.TurretOverheatDownRate, parameterName);
        Ensure(previous.TurretOverheatPenalty, value.TurretOverheatPenalty, parameterName);
        Ensure(previous.Turret, value.Turret, parameterName);
        Ensure(previous.MissileConeSound, value.MissileConeSound, parameterName);
    }

    internal static void Ensure(WeaponGunKickAndDistanceFields previous, WeaponGunKickAndDistanceFields value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(value);
        Ensure(previous.AdsGunKickReducedKickPercent, value.AdsGunKickReducedKickPercent, parameterName);
        Ensure(previous.AdsGunKickPitchMin, value.AdsGunKickPitchMin, parameterName);
        Ensure(previous.AdsGunKickPitchMax, value.AdsGunKickPitchMax, parameterName);
        Ensure(previous.AdsGunKickYawMin, value.AdsGunKickYawMin, parameterName);
        Ensure(previous.AdsGunKickYawMax, value.AdsGunKickYawMax, parameterName);
        Ensure(previous.AdsGunKickAcceleration, value.AdsGunKickAcceleration, parameterName);
        Ensure(previous.AdsGunKickSpeedMax, value.AdsGunKickSpeedMax, parameterName);
        Ensure(previous.AdsGunKickSpeedDecay, value.AdsGunKickSpeedDecay, parameterName);
        Ensure(previous.AdsGunKickStaticDecay, value.AdsGunKickStaticDecay, parameterName);
        Ensure(previous.AdsViewKickPitchMin, value.AdsViewKickPitchMin, parameterName);
        Ensure(previous.AdsViewKickPitchMax, value.AdsViewKickPitchMax, parameterName);
        Ensure(previous.AdsViewKickYawMin, value.AdsViewKickYawMin, parameterName);
        Ensure(previous.AdsViewKickYawMax, value.AdsViewKickYawMax, parameterName);
        Ensure(previous.AdsViewScatterMin, value.AdsViewScatterMin, parameterName);
        Ensure(previous.AdsViewScatterMax, value.AdsViewScatterMax, parameterName);
        Ensure(previous.AdsSpread, value.AdsSpread, parameterName);
        Ensure(previous.HipGunKickReducedKickPercent, value.HipGunKickReducedKickPercent, parameterName);
        Ensure(previous.HipGunKickPitchMin, value.HipGunKickPitchMin, parameterName);
        Ensure(previous.HipGunKickPitchMax, value.HipGunKickPitchMax, parameterName);
        Ensure(previous.HipGunKickYawMin, value.HipGunKickYawMin, parameterName);
        Ensure(previous.HipGunKickYawMax, value.HipGunKickYawMax, parameterName);
        Ensure(previous.HipGunKickAcceleration, value.HipGunKickAcceleration, parameterName);
        Ensure(previous.HipGunKickSpeedMax, value.HipGunKickSpeedMax, parameterName);
        Ensure(previous.HipGunKickSpeedDecay, value.HipGunKickSpeedDecay, parameterName);
        Ensure(previous.HipGunKickStaticDecay, value.HipGunKickStaticDecay, parameterName);
        Ensure(previous.HipViewKickPitchMin, value.HipViewKickPitchMin, parameterName);
        Ensure(previous.HipViewKickPitchMax, value.HipViewKickPitchMax, parameterName);
        Ensure(previous.HipViewKickYawMin, value.HipViewKickYawMin, parameterName);
        Ensure(previous.HipViewKickYawMax, value.HipViewKickYawMax, parameterName);
        Ensure(previous.HipViewScatterMin, value.HipViewScatterMin, parameterName);
        Ensure(previous.HipViewScatterMax, value.HipViewScatterMax, parameterName);
        Ensure(previous.FightDistance, value.FightDistance, parameterName);
        Ensure(previous.MaxDistance, value.MaxDistance, parameterName);
    }

    internal static void Ensure(WeaponHintFields previous, WeaponHintFields value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(value);
        Ensure(previous.HorizontalViewJitter, value.HorizontalViewJitter, parameterName);
        Ensure(previous.VerticalViewJitter, value.VerticalViewJitter, parameterName);
        Ensure(previous.ScanSpeed, value.ScanSpeed, parameterName);
        Ensure(previous.ScanAcceleration, value.ScanAcceleration, parameterName);
    }

    internal static void Ensure(WeaponMissileConeSoundFields previous, WeaponMissileConeSoundFields value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(value);
        Ensure(previous.RadiusAtTop, value.RadiusAtTop, parameterName);
        Ensure(previous.RadiusAtBase, value.RadiusAtBase, parameterName);
        Ensure(previous.Height, value.Height, parameterName);
        Ensure(previous.OriginOffset, value.OriginOffset, parameterName);
        Ensure(previous.VolumeScaleAtCore, value.VolumeScaleAtCore, parameterName);
        Ensure(previous.VolumeScaleAtEdge, value.VolumeScaleAtEdge, parameterName);
        Ensure(previous.VolumeScaleCoreSize, value.VolumeScaleCoreSize, parameterName);
        Ensure(previous.PitchAtTop, value.PitchAtTop, parameterName);
        Ensure(previous.PitchAtBottom, value.PitchAtBottom, parameterName);
        Ensure(previous.PitchTopSize, value.PitchTopSize, parameterName);
        Ensure(previous.PitchBottomSize, value.PitchBottomSize, parameterName);
        Ensure(previous.CrossfadeTopSize, value.CrossfadeTopSize, parameterName);
        Ensure(previous.CrossfadeBottomSize, value.CrossfadeBottomSize, parameterName);
    }

    internal static void Ensure(WeaponPhysicsFields previous, WeaponPhysicsFields value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(value);
        Ensure(previous.DualWieldViewModelOffset, value.DualWieldViewModelOffset, parameterName);
        Ensure(previous.AmmoDropClipPercentMin, value.AmmoDropClipPercentMin, parameterName);
        Ensure(previous.AmmoDropClipPercentMax, value.AmmoDropClipPercentMax, parameterName);
        Ensure(previous.DamageConeAngle, value.DamageConeAngle, parameterName);
        Ensure(previous.BulletExplosionDamageMultiplier, value.BulletExplosionDamageMultiplier, parameterName);
        Ensure(previous.BulletExplosionRadiusMultiplier, value.BulletExplosionRadiusMultiplier, parameterName);
        Ensure(previous.ProjectileCurvature, value.ProjectileCurvature, parameterName);
    }

    internal static void Ensure(WeaponPositionalMovementFields previous, WeaponPositionalMovementFields value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(value);
        Ensure(previous.PositionMoveRate, value.PositionMoveRate, parameterName);
        Ensure(previous.PositionProneMoveRate, value.PositionProneMoveRate, parameterName);
        Ensure(previous.StandMoveMinSpeed, value.StandMoveMinSpeed, parameterName);
        Ensure(previous.DuckedMoveMinSpeed, value.DuckedMoveMinSpeed, parameterName);
        Ensure(previous.ProneMoveMinSpeed, value.ProneMoveMinSpeed, parameterName);
        Ensure(previous.PositionRotationRate, value.PositionRotationRate, parameterName);
        Ensure(previous.PositionProneRotationRate, value.PositionProneRotationRate, parameterName);
        Ensure(previous.StandRotationMinSpeed, value.StandRotationMinSpeed, parameterName);
        Ensure(previous.DuckedRotationMinSpeed, value.DuckedRotationMinSpeed, parameterName);
        Ensure(previous.ProneRotationMinSpeed, value.ProneRotationMinSpeed, parameterName);
    }

    internal static void Ensure(WeaponProjectileFields previous, WeaponProjectileFields value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(value);
        Ensure(previous.RicochetChance, value.RicochetChance, parameterName);
        Ensure(previous.ParallelBounce, value.ParallelBounce, parameterName);
        Ensure(previous.PerpendicularBounce, value.PerpendicularBounce, parameterName);
        Ensure(previous.ProjectileColor, value.ProjectileColor, parameterName);
        Ensure(previous.MaxSteeringAcceleration, value.MaxSteeringAcceleration, parameterName);
        Ensure(previous.AdsAimPitch, value.AdsAimPitch, parameterName);
        Ensure(previous.AdsCrosshairInFraction, value.AdsCrosshairInFraction, parameterName);
        Ensure(previous.AdsCrosshairOutFraction, value.AdsCrosshairOutFraction, parameterName);
        Ensure(previous.GunKickAndDistance, value.GunKickAndDistance, parameterName);
    }

    internal static void Ensure(WeaponTurnSpeedAndRangeFields previous, WeaponTurnSpeedAndRangeFields value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(value);
        Ensure(previous.MinTurnSpeed, value.MinTurnSpeed, parameterName);
        Ensure(previous.MaxTurnSpeed, value.MaxTurnSpeed, parameterName);
        Ensure(previous.PitchConvergenceTime, value.PitchConvergenceTime, parameterName);
        Ensure(previous.YawConvergenceTime, value.YawConvergenceTime, parameterName);
        Ensure(previous.SuppressTime, value.SuppressTime, parameterName);
        Ensure(previous.MaxRange, value.MaxRange, parameterName);
        Ensure(previous.AnimationHorizontalRotateIncrement, value.AnimationHorizontalRotateIncrement, parameterName);
        Ensure(previous.PlayerPositionDistance, value.PlayerPositionDistance, parameterName);
        Ensure(previous.ScanSpeed, value.ScanSpeed, parameterName);
        Ensure(previous.ScanAcceleration, value.ScanAcceleration, parameterName);
    }

    internal static void Ensure(WeaponTurretFields previous, WeaponTurretFields value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(value);
        Ensure(previous.BarrelSpinSpeed, value.BarrelSpinSpeed, parameterName);
        Ensure(previous.BarrelSpinUpTime, value.BarrelSpinUpTime, parameterName);
        Ensure(previous.BarrelSpinDownTime, value.BarrelSpinDownTime, parameterName);
    }

    internal static void Ensure(WeaponVariantDef previous, WeaponVariantDef value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(value);
        if (previous.Definition is not null && value.Definition is not null)
            Ensure(previous.Definition, value.Definition, parameterName);
        Ensure(previous.AdsZoomFov, value.AdsZoomFov, parameterName);
        Ensure(previous.PenetrateMultiplier, value.PenetrateMultiplier, parameterName);
        Ensure(previous.AdsViewKickCenterSpeed, value.AdsViewKickCenterSpeed, parameterName);
        Ensure(previous.HipViewKickCenterSpeed, value.HipViewKickCenterSpeed, parameterName);
        Ensure(previous.AdsDofStart, value.AdsDofStart, parameterName);
        Ensure(previous.AdsDofEnd, value.AdsDofEnd, parameterName);
        Ensure(previous.AccuracyGraphKnots, value.AccuracyGraphKnots, parameterName);
        Ensure(previous.OriginalAccuracyGraphKnots, value.OriginalAccuracyGraphKnots, parameterName);
    }

    internal static void Ensure(WeaponViewMovementFields previous, WeaponViewMovementFields value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(value);
        Ensure(previous.StandMove, value.StandMove, parameterName);
        Ensure(previous.StandRotation, value.StandRotation, parameterName);
        Ensure(previous.StrafeMove, value.StrafeMove, parameterName);
        Ensure(previous.StrafeRotation, value.StrafeRotation, parameterName);
        Ensure(previous.DuckedOffset, value.DuckedOffset, parameterName);
        Ensure(previous.DuckedMove, value.DuckedMove, parameterName);
        Ensure(previous.DuckedRotation, value.DuckedRotation, parameterName);
        Ensure(previous.ProneOffset, value.ProneOffset, parameterName);
        Ensure(previous.ProneMove, value.ProneMove, parameterName);
        Ensure(previous.ProneRotation, value.ProneRotation, parameterName);
    }

    private static void Ensure(float previous, float value, string parameterName)
    {
        if (BitConverter.SingleToInt32Bits(previous) != BitConverter.SingleToInt32Bits(value) &&
            !float.IsFinite(value))
            throw new ArgumentOutOfRangeException(parameterName, "New Weapon float values must be finite.");
    }

    private static void Ensure(Vec2 previous, Vec2 value, string parameterName)
    {
        Ensure(previous.a, value.a, parameterName);
        Ensure(previous.b, value.b, parameterName);
    }

    private static void Ensure(Vec3 previous, Vec3 value, string parameterName)
    {
        Ensure(previous.X, value.X, parameterName);
        Ensure(previous.Y, value.Y, parameterName);
        Ensure(previous.Z, value.Z, parameterName);
    }

    private static void Ensure(IReadOnlyList<float> previous, IReadOnlyList<float> value, string parameterName)
    {
        int shared = Math.Min(previous.Count, value.Count);
        for (int index = 0; index < shared; index++)
            Ensure(previous[index], value[index], parameterName);
        for (int index = shared; index < value.Count; index++)
        {
            if (!float.IsFinite(value[index]))
                throw new ArgumentOutOfRangeException(parameterName, "New Weapon float values must be finite.");
        }
    }

    private static void Ensure(IReadOnlyList<Vec2> previous, IReadOnlyList<Vec2> value, string parameterName)
    {
        int shared = Math.Min(previous.Count, value.Count);
        for (int index = 0; index < shared; index++)
            Ensure(previous[index], value[index], parameterName);
        for (int index = shared; index < value.Count; index++)
            Ensure(default, value[index], parameterName);
    }

}
