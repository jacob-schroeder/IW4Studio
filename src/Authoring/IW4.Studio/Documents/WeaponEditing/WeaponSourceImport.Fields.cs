using System.Globalization;
using IW4.Game.Assets;
using IW4.Game.Assets.Fx;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.Physics;
using IW4.Game.Assets.Tracer;
using IW4.Game.Assets.Weapon;
using IW4.Game.Assets.XModel;
using IW4.Game.Zone;
using IW4.Runtime.Assets;

namespace IW4.Studio.Documents;

internal static partial class WeaponSourceImport
{
    private static bool TryApplyField(
        WeaponDraft draft, string key, string value, XAssetPool pool)
    {
        switch (key)
        {
            case "AIOverlayDescription":
            {
                draft.SetDefinitionOverlayDescription(NullableText(value));
                return true;
            }
            case "accuracy":
            {
                draft.SetDefinitionAccuracyAccuracy(ParseFloat(value, key));
                return true;
            }
            case "activeReticleType":
            {
                draft.SetDefinitionReticleActiveType(ParseEnum<ActiveReticleType>(value, key));
                return true;
            }
            case "adsAimPitch":
            {
                draft.SetDefinitionProjectileAdsAimPitch(ParseFloat(value, key));
                return true;
            }
            case "adsBobFactor":
            {
                draft.SetDefinitionAdsViewAndSpreadAdsBobFactor(ParseFloat(value, key));
                return true;
            }
            case "adsCrosshairInFrac":
            {
                draft.SetDefinitionProjectileAdsCrosshairInFraction(ParseFloat(value, key));
                return true;
            }
            case "adsCrosshairOutFrac":
            {
                draft.SetDefinitionProjectileAdsCrosshairOutFraction(ParseFloat(value, key));
                return true;
            }
            case "adsDofEnd":
            {
                draft.SetVariantAdsDofEnd(ParseFloat(value, key));
                return true;
            }
            case "adsDofStart":
            {
                draft.SetVariantAdsDofStart(ParseFloat(value, key));
                return true;
            }
            case "adsFire":
            {
                draft.SetDefinitionTailFlagsAdsFireOnly(ParseBoolean(value, key));
                return true;
            }
            case "adsGunKickAccel":
            {
                draft.SetDefinitionProjectileGunKickAndDistanceAdsGunKickAcceleration(ParseFloat(value, key));
                return true;
            }
            case "adsGunKickPitchMax":
            {
                draft.SetDefinitionProjectileGunKickAndDistanceAdsGunKickPitchMax(ParseFloat(value, key));
                return true;
            }
            case "adsGunKickPitchMin":
            {
                draft.SetDefinitionProjectileGunKickAndDistanceAdsGunKickPitchMin(ParseFloat(value, key));
                return true;
            }
            case "adsGunKickReducedKickBullets":
            {
                draft.SetDefinitionProjectileGunKickAndDistanceAdsGunKickReducedKickBullets(ParseInt(value, key));
                return true;
            }
            case "adsGunKickReducedKickPercent":
            {
                draft.SetDefinitionProjectileGunKickAndDistanceAdsGunKickReducedKickPercent(ParseFloat(value, key));
                return true;
            }
            case "adsGunKickSpeedDecay":
            {
                draft.SetDefinitionProjectileGunKickAndDistanceAdsGunKickSpeedDecay(ParseFloat(value, key));
                return true;
            }
            case "adsGunKickSpeedMax":
            {
                draft.SetDefinitionProjectileGunKickAndDistanceAdsGunKickSpeedMax(ParseFloat(value, key));
                return true;
            }
            case "adsGunKickStaticDecay":
            {
                draft.SetDefinitionProjectileGunKickAndDistanceAdsGunKickStaticDecay(ParseFloat(value, key));
                return true;
            }
            case "adsGunKickYawMax":
            {
                draft.SetDefinitionProjectileGunKickAndDistanceAdsGunKickYawMax(ParseFloat(value, key));
                return true;
            }
            case "adsGunKickYawMin":
            {
                draft.SetDefinitionProjectileGunKickAndDistanceAdsGunKickYawMin(ParseFloat(value, key));
                return true;
            }
            case "adsIdleAmount":
            {
                draft.SetDefinitionAdsViewAndSpreadAdsIdleAmount(ParseFloat(value, key));
                return true;
            }
            case "adsIdleSpeed":
            {
                draft.SetDefinitionAdsViewAndSpreadAdsIdleSpeed(ParseFloat(value, key));
                return true;
            }
            case "adsMoveSpeedScale":
            {
                draft.SetDefinitionAimMovementTuningAdsMoveSpeedScale(ParseFloat(value, key));
                return true;
            }
            case "adsOverlayHeight":
            {
                draft.SetDefinitionOverlayHeight(ParseFloat(value, key));
                return true;
            }
            case "adsOverlayHeightSplitscreen":
            {
                draft.SetDefinitionOverlayHeightSplitscreen(ParseFloat(value, key));
                return true;
            }
            case "adsOverlayInterface":
            {
                draft.SetDefinitionOverlayInterface(ParseEnum<WeaponOverlayInterface>(value, key));
                return true;
            }
            case "adsOverlayReticle":
            {
                draft.SetDefinitionOverlayReticle(ParseEnum<WeaponOverlayReticle>(value, key));
                return true;
            }
            case "adsOverlayShader":
            {
                draft.SetDefinitionOverlayMaterial(ResolveAsset<MaterialAsset>(value, key, pool, XAssetType.Material));
                return true;
            }
            case "adsOverlayShaderEMP":
            {
                draft.SetDefinitionOverlayMaterialEmp(ResolveAsset<MaterialAsset>(value, key, pool, XAssetType.Material));
                return true;
            }
            case "adsOverlayShaderEMPLowRes":
            {
                draft.SetDefinitionOverlayMaterialEmpLowRes(ResolveAsset<MaterialAsset>(value, key, pool, XAssetType.Material));
                return true;
            }
            case "adsOverlayShaderLowRes":
            {
                draft.SetDefinitionOverlayMaterialLowRes(ResolveAsset<MaterialAsset>(value, key, pool, XAssetType.Material));
                return true;
            }
            case "adsOverlayWidth":
            {
                draft.SetDefinitionOverlayWidth(ParseFloat(value, key));
                return true;
            }
            case "adsOverlayWidthSplitscreen":
            {
                draft.SetDefinitionOverlayWidthSplitscreen(ParseFloat(value, key));
                return true;
            }
            case "adsReloadTransTime":
            {
                draft.SetDefinitionAccuracyPositionReloadTransitionTime(ParseMilliseconds(value, key));
                return true;
            }
            case "adsSpread":
            {
                draft.SetDefinitionProjectileGunKickAndDistanceAdsSpread(ParseFloat(value, key));
                return true;
            }
            case "adsSwayHorizScale":
            {
                draft.SetDefinitionAdsViewAndSpreadAdsSwayHorizontalScale(ParseFloat(value, key));
                return true;
            }
            case "adsSwayLerpSpeed":
            {
                draft.SetDefinitionAdsViewAndSpreadAdsSwayLerpSpeed(ParseFloat(value, key));
                return true;
            }
            case "adsSwayMaxAngle":
            {
                draft.SetDefinitionAdsViewAndSpreadAdsSwayMaxAngle(ParseFloat(value, key));
                return true;
            }
            case "adsSwayPitchScale":
            {
                draft.SetDefinitionAdsViewAndSpreadAdsSwayPitchScale(ParseFloat(value, key));
                return true;
            }
            case "adsSwayVertScale":
            {
                draft.SetDefinitionAdsViewAndSpreadAdsSwayVerticalScale(ParseFloat(value, key));
                return true;
            }
            case "adsSwayYawScale":
            {
                draft.SetDefinitionAdsViewAndSpreadAdsSwayYawScale(ParseFloat(value, key));
                return true;
            }
            case "adsTransInTime":
            {
                draft.SetVariantAdsTransitionInTime(ParseMilliseconds(value, key));
                return true;
            }
            case "adsTransOutTime":
            {
                draft.SetVariantAdsTransitionOutTime(ParseMilliseconds(value, key));
                return true;
            }
            case "adsViewBobMult":
            {
                draft.SetDefinitionAdsViewAndSpreadAdsViewBobMultiplier(ParseFloat(value, key));
                return true;
            }
            case "adsViewErrorMax":
            {
                draft.SetDefinitionAdsViewAndSpreadAdsViewErrorMax(ParseFloat(value, key));
                return true;
            }
            case "adsViewErrorMin":
            {
                draft.SetDefinitionAdsViewAndSpreadAdsViewErrorMin(ParseFloat(value, key));
                return true;
            }
            case "adsViewKickCenterSpeed":
            {
                draft.SetVariantAdsViewKickCenterSpeed(ParseFloat(value, key));
                return true;
            }
            case "adsViewKickPitchMax":
            {
                draft.SetDefinitionProjectileGunKickAndDistanceAdsViewKickPitchMax(ParseFloat(value, key));
                return true;
            }
            case "adsViewKickPitchMin":
            {
                draft.SetDefinitionProjectileGunKickAndDistanceAdsViewKickPitchMin(ParseFloat(value, key));
                return true;
            }
            case "adsViewKickYawMax":
            {
                draft.SetDefinitionProjectileGunKickAndDistanceAdsViewKickYawMax(ParseFloat(value, key));
                return true;
            }
            case "adsViewKickYawMin":
            {
                draft.SetDefinitionProjectileGunKickAndDistanceAdsViewKickYawMin(ParseFloat(value, key));
                return true;
            }
            case "adsZoomFov":
            {
                draft.SetVariantAdsZoomFov(ParseFloat(value, key));
                return true;
            }
            case "adsZoomInFrac":
            {
                draft.SetDefinitionAimMovementTuningAdsZoomInFraction(ParseFloat(value, key));
                return true;
            }
            case "adsZoomOutFrac":
            {
                draft.SetDefinitionAimMovementTuningAdsZoomOutFraction(ParseFloat(value, key));
                return true;
            }
            case "aiSpread":
            {
                draft.SetDefinitionAccuracyAiSpread(ParseFloat(value, key));
                return true;
            }
            case "aifuseTime":
            {
                draft.SetDefinitionTimingAiFuseTime(ParseMilliseconds(value, key));
                return true;
            }
            case "aimAssistRange":
            {
                draft.SetDefinitionAimMovementTuningAimAssistRange(ParseFloat(value, key));
                return true;
            }
            case "aimAssistRangeAds":
            {
                draft.SetDefinitionAimMovementTuningAimAssistRangeAds(ParseFloat(value, key));
                return true;
            }
            case "aimDownSight":
            {
                draft.SetDefinitionTailFlagsAimDownSight(ParseBoolean(value, key));
                return true;
            }
            case "aimPadding":
            {
                draft.SetDefinitionAimMovementTuningAimPadding(ParseFloat(value, key));
                return true;
            }
            case "altDropTime":
            {
                draft.SetDefinitionTimingAltDropTime(ParseMilliseconds(value, key));
                return true;
            }
            case "altModeSameWeapon":
            {
                draft.SetDefinitionTailFlagsAltModeSameWeapon(ParseBoolean(value, key));
                return true;
            }
            case "altRaiseTime":
            {
                draft.SetVariantAlternateRaiseTime(ParseMilliseconds(value, key));
                return true;
            }
            case "altWeapon":
            {
                draft.SetVariantAlternateWeaponName(NullableText(value));
                return true;
            }
            case "ammoCounterClip":
            {
                draft.SetDefinitionIconsAmmoCounterClip(ParseEnum<AmmoCounterClipType>(value, key));
                return true;
            }
            case "ammoCounterIcon":
            {
                draft.SetDefinitionIconsAmmoCounterIcon(ResolveAsset<MaterialAsset>(value, key, pool, XAssetType.Material));
                return true;
            }
            case "ammoCounterIconRatio":
            {
                draft.SetDefinitionIconsAmmoCounterIconRatio(ParseEnum<WeaponIconRatio>(value, key));
                return true;
            }
            case "ammoDropClipPercentMax":
            {
                draft.SetDefinitionPhysicsAmmoDropClipPercentMax(ParseFloatBits(value, key));
                return true;
            }
            case "ammoDropClipPercentMin":
            {
                draft.SetDefinitionPhysicsAmmoDropClipPercentMin(ParseFloatBits(value, key));
                return true;
            }
            case "ammoName":
            {
                draft.SetDefinitionAmmoAmmoName(NullableText(value));
                return true;
            }
            case "animHorRotateInc":
            {
                draft.SetDefinitionTurnSpeedAndRangeAnimationHorizontalRotateIncrement(ParseFloat(value, key));
                return true;
            }
            case "armorPiercing":
            {
                draft.SetDefinitionTailFlagsArmorPiercing(ParseBoolean(value, key));
                return true;
            }
            case "autoAimRange":
            {
                draft.SetDefinitionAimMovementTuningAutoAimRange(ParseFloat(value, key));
                return true;
            }
            case "avoidDropCleanup":
            {
                draft.SetDefinitionTailFlagsAvoidDropCleanup(ParseBoolean(value, key));
                return true;
            }
            case "bBulletExplosiveDamage":
            {
                draft.SetDefinitionTailFlagsBulletExplosiveDamage(ParseBoolean(value, key));
                return true;
            }
            case "bigExplosion":
            {
                draft.SetDefinitionTailFlagsBigExplosion(ParseBoolean(value, key));
                return true;
            }
            case "blocksProne":
            {
                draft.SetDefinitionTailFlagsBlocksProne(ParseBoolean(value, key));
                return true;
            }
            case "boltAction":
            {
                draft.SetDefinitionTailFlagsBoltAction(ParseBoolean(value, key));
                return true;
            }
            case "bottomArc":
            {
                draft.SetDefinitionAccuracyBottomArc(ParseFloat(value, key));
                return true;
            }
            case "breachRaiseTime":
            {
                draft.SetDefinitionTimingBreachRaiseTime(ParseMilliseconds(value, key));
                return true;
            }
            case "bulletExplDmgMult":
            {
                draft.SetDefinitionPhysicsBulletExplosionDamageMultiplier(ParseFloat(value, key));
                return true;
            }
            case "bulletExplRadiusMult":
            {
                draft.SetDefinitionPhysicsBulletExplosionRadiusMultiplier(ParseFloat(value, key));
                return true;
            }
            case "cancelAutoHolsterWhenEmpty":
            {
                draft.SetDefinitionTailFlagsCancelAutoHolsterWhenEmpty(ParseBoolean(value, key));
                return true;
            }
            case "clipName":
            {
                draft.SetDefinitionAmmoClipName(NullableText(value));
                return true;
            }
            case "clipOnly":
            {
                draft.SetDefinitionTailFlagsClipOnly(ParseBoolean(value, key));
                return true;
            }
            case "clipSize":
            {
                draft.SetVariantClipSize(ParseInt(value, key));
                return true;
            }
            case "cookOffHold":
            {
                draft.SetDefinitionTailFlagsCookOffHold(ParseBoolean(value, key));
                return true;
            }
            case "crosshairColorChange":
            {
                draft.SetDefinitionTailFlagsCrosshairColorChange(ParseBoolean(value, key));
                return true;
            }
            case "damage":
            {
                draft.SetDefinitionAmmoDamage(ParseInt(value, key));
                return true;
            }
            case "damageConeAngle":
            {
                draft.SetDefinitionPhysicsDamageConeAngle(ParseFloat(value, key));
                return true;
            }
            case "destabilizationCurvatureMax":
            {
                draft.SetDefinitionDestabilizationCurvatureMax(ParseFloat(value, key));
                return true;
            }
            case "destabilizationRateTime":
            {
                draft.SetDefinitionDestabilizationRateTime(ParseFloat(value, key));
                return true;
            }
            case "destabilizeDistance":
            {
                draft.SetDefinitionDestabilizeDistance(ParseInt(value, key));
                return true;
            }
            case "detonateDelay":
            {
                draft.SetDefinitionTimingDetonateDelay(ParseMilliseconds(value, key));
                return true;
            }
            case "detonateTime":
            {
                draft.SetDefinitionTimingDetonateTime(ParseMilliseconds(value, key));
                return true;
            }
            case "disableFiring":
            {
                draft.SetDefinitionTailFlagsDisableFiring(ParseBoolean(value, key));
                return true;
            }
            case "disableSwitchToWhenEmpty":
            {
                draft.SetDefinitionTailFlagsDisableSwitchToWhenEmpty(ParseBoolean(value, key));
                return true;
            }
            case "displayName":
            {
                draft.SetVariantDisplayName(NullableText(value));
                return true;
            }
            case "dpadIcon":
            {
                draft.SetVariantDpadIcon(ResolveAsset<MaterialAsset>(value, key, pool, XAssetType.Material));
                return true;
            }
            case "dpadIconRatio":
            {
                draft.SetVariantDpadIconRatio(ParseEnum<WeaponIconRatio>(value, key));
                return true;
            }
            case "dpadIconShowsAmmo":
            {
                draft.SetVariantDpadIconShowsAmmo(ParseBoolean(value, key));
                return true;
            }
            case "dropAmmoMax":
            {
                draft.SetVariantAmmoDropStockMax(ParseInt(value, key));
                return true;
            }
            case "dropAmmoMin":
            {
                draft.SetDefinitionPhysicsAmmoDropStockMin(ParseInt(value, key));
                return true;
            }
            case "dropHintString":
            {
                draft.SetDefinitionHintsDropHintString(NullableText(value));
                return true;
            }
            case "dropTime":
            {
                draft.SetDefinitionTimingDropTime(ParseMilliseconds(value, key));
                return true;
            }
            case "dualWieldViewModelOffset":
            {
                draft.SetDefinitionPhysicsDualWieldViewModelOffset(ParseFloat(value, key));
                return true;
            }
            case "duckedMoveF":
            {
                var vector = draft.Definition!.ViewMovement.DuckedMove;
                vector.X = ParseFloat(value, key);
                draft.SetDefinitionViewMovementDuckedMove(vector);
                return true;
            }
            case "duckedMoveMinSpeed":
            {
                draft.SetDefinitionPositionalMovementDuckedMoveMinSpeed(ParseFloat(value, key));
                return true;
            }
            case "duckedMoveR":
            {
                var vector = draft.Definition!.ViewMovement.DuckedMove;
                vector.Y = ParseFloat(value, key);
                draft.SetDefinitionViewMovementDuckedMove(vector);
                return true;
            }
            case "duckedMoveU":
            {
                var vector = draft.Definition!.ViewMovement.DuckedMove;
                vector.Z = ParseFloat(value, key);
                draft.SetDefinitionViewMovementDuckedMove(vector);
                return true;
            }
            case "duckedOfsF":
            {
                var vector = draft.Definition!.ViewMovement.DuckedOffset;
                vector.X = ParseFloat(value, key);
                draft.SetDefinitionViewMovementDuckedOffset(vector);
                return true;
            }
            case "duckedOfsR":
            {
                var vector = draft.Definition!.ViewMovement.DuckedOffset;
                vector.Y = ParseFloat(value, key);
                draft.SetDefinitionViewMovementDuckedOffset(vector);
                return true;
            }
            case "duckedOfsU":
            {
                var vector = draft.Definition!.ViewMovement.DuckedOffset;
                vector.Z = ParseFloat(value, key);
                draft.SetDefinitionViewMovementDuckedOffset(vector);
                return true;
            }
            case "duckedRotMinSpeed":
            {
                draft.SetDefinitionPositionalMovementDuckedRotationMinSpeed(ParseFloat(value, key));
                return true;
            }
            case "duckedRotP":
            {
                var vector = draft.Definition!.ViewMovement.DuckedRotation;
                vector.X = ParseFloat(value, key);
                draft.SetDefinitionViewMovementDuckedRotation(vector);
                return true;
            }
            case "duckedRotR":
            {
                var vector = draft.Definition!.ViewMovement.DuckedRotation;
                vector.Z = ParseFloat(value, key);
                draft.SetDefinitionViewMovementDuckedRotation(vector);
                return true;
            }
            case "duckedRotY":
            {
                var vector = draft.Definition!.ViewMovement.DuckedRotation;
                vector.Y = ParseFloat(value, key);
                draft.SetDefinitionViewMovementDuckedRotation(vector);
                return true;
            }
            case "emptyDropTime":
            {
                draft.SetDefinitionTimingEmptyDropTime(ParseMilliseconds(value, key));
                return true;
            }
            case "emptyRaiseTime":
            {
                draft.SetDefinitionTimingEmptyRaiseTime(ParseMilliseconds(value, key));
                return true;
            }
            case "enemyCrosshairRange":
            {
                draft.SetDefinitionAimMovementTuningEnemyCrosshairRange(ParseFloat(value, key));
                return true;
            }
            case "enhanced":
            {
                draft.SetVariantEnhanced(ParseBoolean(value, key));
                return true;
            }
            case "explosionInnerDamage":
            {
                draft.SetDefinitionPhysicsExplosionInnerDamage(ParseInt(value, key));
                return true;
            }
            case "explosionOuterDamage":
            {
                draft.SetDefinitionPhysicsExplosionOuterDamage(ParseInt(value, key));
                return true;
            }
            case "explosionRadius":
            {
                draft.SetDefinitionPhysicsExplosionRadius(ParseInt(value, key));
                return true;
            }
            case "explosionRadiusMin":
            {
                draft.SetDefinitionPhysicsExplosionRadiusMin(ParseInt(value, key));
                return true;
            }
            case "fightDist":
            {
                draft.SetDefinitionProjectileGunKickAndDistanceFightDistance(ParseFloat(value, key));
                return true;
            }
            case "fireDelay":
            {
                draft.SetDefinitionTimingFireDelay(ParseMilliseconds(value, key));
                return true;
            }
            case "fireRumble":
            {
                draft.SetDefinitionRumbleFireRumble(NullableText(value));
                return true;
            }
            case "fireTime":
            {
                draft.SetVariantFireTime(ParseMilliseconds(value, key));
                return true;
            }
            case "fireType":
            {
                draft.SetDefinitionFireType(ParseEnum<WeaponFireType>(value, key));
                return true;
            }
            case "firstRaiseTime":
            {
                draft.SetVariantFirstRaiseTime(ParseMilliseconds(value, key));
                return true;
            }
            case "flipKillIcon":
            {
                draft.SetDefinitionTailFlagsFlipKillIcon(ParseBoolean(value, key));
                return true;
            }
            case "freezeMovementWhenFiring":
            {
                draft.SetDefinitionTailFlagsFreezeMovementWhenFiring(ParseBoolean(value, key));
                return true;
            }
            case "fuseTime":
            {
                draft.SetDefinitionTimingFuseTime(ParseMilliseconds(value, key));
                return true;
            }
            case "guidedMissileType":
            {
                draft.SetDefinitionProjectileGuidedMissileType(ParseEnum<GuidedMissileType>(value, key));
                return true;
            }
            case "gunMaxPitch":
            {
                draft.SetDefinitionAdsViewAndSpreadGunMaxPitch(ParseFloat(value, key));
                return true;
            }
            case "gunMaxYaw":
            {
                draft.SetDefinitionAdsViewAndSpreadGunMaxYaw(ParseFloat(value, key));
                return true;
            }
            case "handModel":
            {
                draft.SetDefinitionHandModel(ResolveAsset<XModelAsset>(value, key, pool, XAssetType.XModel));
                return true;
            }
            case "hasDetonator":
            {
                draft.SetDefinitionTailFlagsHasDetonator(ParseBoolean(value, key));
                return true;
            }
            case "hipGunKickAccel":
            {
                draft.SetDefinitionProjectileGunKickAndDistanceHipGunKickAcceleration(ParseFloat(value, key));
                return true;
            }
            case "hipGunKickPitchMax":
            {
                draft.SetDefinitionProjectileGunKickAndDistanceHipGunKickPitchMax(ParseFloat(value, key));
                return true;
            }
            case "hipGunKickPitchMin":
            {
                draft.SetDefinitionProjectileGunKickAndDistanceHipGunKickPitchMin(ParseFloat(value, key));
                return true;
            }
            case "hipGunKickReducedKickBullets":
            {
                draft.SetDefinitionProjectileGunKickAndDistanceHipGunKickReducedKickBullets(ParseInt(value, key));
                return true;
            }
            case "hipGunKickReducedKickPercent":
            {
                draft.SetDefinitionProjectileGunKickAndDistanceHipGunKickReducedKickPercent(ParseFloat(value, key));
                return true;
            }
            case "hipGunKickSpeedDecay":
            {
                draft.SetDefinitionProjectileGunKickAndDistanceHipGunKickSpeedDecay(ParseFloat(value, key));
                return true;
            }
            case "hipGunKickSpeedMax":
            {
                draft.SetDefinitionProjectileGunKickAndDistanceHipGunKickSpeedMax(ParseFloat(value, key));
                return true;
            }
            case "hipGunKickStaticDecay":
            {
                draft.SetDefinitionProjectileGunKickAndDistanceHipGunKickStaticDecay(ParseFloat(value, key));
                return true;
            }
            case "hipGunKickYawMax":
            {
                draft.SetDefinitionProjectileGunKickAndDistanceHipGunKickYawMax(ParseFloat(value, key));
                return true;
            }
            case "hipGunKickYawMin":
            {
                draft.SetDefinitionProjectileGunKickAndDistanceHipGunKickYawMin(ParseFloat(value, key));
                return true;
            }
            case "hipIdleAmount":
            {
                draft.SetDefinitionAdsViewAndSpreadHipIdleAmount(ParseFloat(value, key));
                return true;
            }
            case "hipIdleSpeed":
            {
                draft.SetDefinitionAdsViewAndSpreadHipIdleSpeed(ParseFloat(value, key));
                return true;
            }
            case "hipReticleSidePos":
            {
                draft.SetDefinitionAdsViewAndSpreadHipReticleSidePosition(ParseFloat(value, key));
                return true;
            }
            case "hipSpreadDecayRate":
            {
                draft.SetDefinitionAdsViewAndSpreadHipSpreadDecayRate(ParseFloat(value, key));
                return true;
            }
            case "hipSpreadDuckedDecay":
            {
                draft.SetDefinitionAdsViewAndSpreadHipSpreadDuckedDecay(ParseFloat(value, key));
                return true;
            }
            case "hipSpreadDuckedMax":
            {
                draft.SetDefinitionAdsViewAndSpreadHipSpreadDuckedMax(ParseFloat(value, key));
                return true;
            }
            case "hipSpreadDuckedMin":
            {
                draft.SetDefinitionAdsViewAndSpreadHipSpreadDuckedMin(ParseFloat(value, key));
                return true;
            }
            case "hipSpreadFireAdd":
            {
                draft.SetDefinitionAdsViewAndSpreadHipSpreadFireAdd(ParseFloat(value, key));
                return true;
            }
            case "hipSpreadMax":
            {
                draft.SetDefinitionAdsViewAndSpreadHipSpreadStandMax(ParseFloat(value, key));
                return true;
            }
            case "hipSpreadMoveAdd":
            {
                draft.SetDefinitionAdsViewAndSpreadHipSpreadMoveAdd(ParseFloat(value, key));
                return true;
            }
            case "hipSpreadProneDecay":
            {
                draft.SetDefinitionAdsViewAndSpreadHipSpreadProneDecay(ParseFloat(value, key));
                return true;
            }
            case "hipSpreadProneMax":
            {
                draft.SetDefinitionAdsViewAndSpreadHipSpreadProneMax(ParseFloat(value, key));
                return true;
            }
            case "hipSpreadProneMin":
            {
                draft.SetDefinitionAdsViewAndSpreadHipSpreadProneMin(ParseFloat(value, key));
                return true;
            }
            case "hipSpreadStandMin":
            {
                draft.SetDefinitionAdsViewAndSpreadHipSpreadStandMin(ParseFloat(value, key));
                return true;
            }
            case "hipSpreadTurnAdd":
            {
                draft.SetDefinitionAdsViewAndSpreadHipSpreadTurnAdd(ParseFloat(value, key));
                return true;
            }
            case "hipViewKickCenterSpeed":
            {
                draft.SetVariantHipViewKickCenterSpeed(ParseFloat(value, key));
                return true;
            }
            case "hipViewKickPitchMax":
            {
                draft.SetDefinitionProjectileGunKickAndDistanceHipViewKickPitchMax(ParseFloat(value, key));
                return true;
            }
            case "hipViewKickPitchMin":
            {
                draft.SetDefinitionProjectileGunKickAndDistanceHipViewKickPitchMin(ParseFloat(value, key));
                return true;
            }
            case "hipViewKickYawMax":
            {
                draft.SetDefinitionProjectileGunKickAndDistanceHipViewKickYawMax(ParseFloat(value, key));
                return true;
            }
            case "hipViewKickYawMin":
            {
                draft.SetDefinitionProjectileGunKickAndDistanceHipViewKickYawMin(ParseFloat(value, key));
                return true;
            }
            case "holdButtonToThrow":
            {
                draft.SetDefinitionTailFlagsHoldButtonToThrow(ParseBoolean(value, key));
                return true;
            }
            case "holdFireTime":
            {
                draft.SetDefinitionTimingHoldFireTime(ParseMilliseconds(value, key));
                return true;
            }
            case "horizViewJitter":
            {
                draft.SetDefinitionHintsHorizontalViewJitter(ParseFloat(value, key));
                return true;
            }
            case "hudIcon":
            {
                draft.SetDefinitionIconsHudIcon(ResolveAsset<MaterialAsset>(value, key, pool, XAssetType.Material));
                return true;
            }
            case "hudIconRatio":
            {
                draft.SetDefinitionIconsHudIconRatio(ParseEnum<WeaponIconRatio>(value, key));
                return true;
            }
            case "idleCrouchFactor":
            {
                draft.SetDefinitionAdsViewAndSpreadIdleCrouchFactor(ParseFloat(value, key));
                return true;
            }
            case "idleProneFactor":
            {
                draft.SetDefinitionAdsViewAndSpreadIdleProneFactor(ParseFloat(value, key));
                return true;
            }
            case "impactType":
            {
                draft.SetVariantImpactType(ParseEnum<WeaponImpactType>(value, key));
                return true;
            }
            case "inheritsPerks":
            {
                draft.SetDefinitionTailFlagsInheritsPerks(ParseBoolean(value, key));
                return true;
            }
            case "inventoryType":
            {
                draft.SetDefinitionInventoryType(ParseEnum<WeaponInventoryType>(value, key));
                return true;
            }
            case "isRollingGrenade":
            {
                draft.SetDefinitionTailFlagsIsRollingGrenade(ParseBoolean(value, key));
                return true;
            }
            case "killIcon":
            {
                draft.SetVariantKillIcon(ResolveAsset<MaterialAsset>(value, key, pool, XAssetType.Material));
                return true;
            }
            case "killIconRatio":
            {
                draft.SetDefinitionPhysicsKillIconRatio(ParseEnum<WeaponIconRatio>(value, key));
                return true;
            }
            case "knifeModel":
            {
                draft.SetDefinitionKnifeModel(ResolveAsset<XModelAsset>(value, key, pool, XAssetType.XModel));
                return true;
            }
            case "laserSightDuringNightvision":
            {
                draft.SetDefinitionTailFlagsLaserSightDuringNightvision(ParseBoolean(value, key));
                return true;
            }
            case "leftArc":
            {
                draft.SetDefinitionAccuracyLeftArc(ParseFloat(value, key));
                return true;
            }
            case "lockonSupported":
            {
                draft.SetDefinitionTailFlagsLockonSupported(ParseBoolean(value, key));
                return true;
            }
            case "lowAmmoWarningThreshold":
            {
                draft.SetDefinitionProjectileLowAmmoWarningThreshold(ParseFloat(value, key));
                return true;
            }
            case "markableViewmodel":
            {
                draft.SetDefinitionTailFlagsMarkableViewmodel(ParseBoolean(value, key));
                return true;
            }
            case "maxAmmo":
            {
                draft.SetDefinitionAmmoMaxAmmo(ParseInt(value, key));
                return true;
            }
            case "maxDamageRange":
            {
                draft.SetDefinitionMaxDamageRange(ParseFloat(value, key));
                return true;
            }
            case "maxDist":
            {
                draft.SetDefinitionProjectileGunKickAndDistanceMaxDistance(ParseFloat(value, key));
                return true;
            }
            case "maxHorTurnSpeed":
            {
                draft.SetDefinitionTurnSpeedAndRangeMaxHorizontalTurnSpeed(ParseFloat(value, key));
                return true;
            }
            case "maxRange":
            {
                draft.SetDefinitionTurnSpeedAndRangeMaxRange(ParseFloat(value, key));
                return true;
            }
            case "maxSteeringAccel":
            {
                draft.SetDefinitionProjectileMaxSteeringAcceleration(ParseFloat(value, key));
                return true;
            }
            case "maxVertTurnSpeed":
            {
                draft.SetDefinitionTurnSpeedAndRangeMaxVerticalTurnSpeed(ParseFloat(value, key));
                return true;
            }
            case "meleeChargeDelay":
            {
                draft.SetDefinitionTimingMeleeChargeDelay(ParseMilliseconds(value, key));
                return true;
            }
            case "meleeChargeTime":
            {
                draft.SetDefinitionTimingMeleeChargeTime(ParseMilliseconds(value, key));
                return true;
            }
            case "meleeDamage":
            {
                draft.SetDefinitionAmmoMeleeDamage(ParseInt(value, key));
                return true;
            }
            case "meleeDelay":
            {
                draft.SetDefinitionTimingMeleeDelay(ParseMilliseconds(value, key));
                return true;
            }
            case "meleeImpactRumble":
            {
                draft.SetDefinitionRumbleMeleeImpactRumble(NullableText(value));
                return true;
            }
            case "meleeTime":
            {
                draft.SetDefinitionTimingMeleeTime(ParseMilliseconds(value, key));
                return true;
            }
            case "minDamage":
            {
                draft.SetDefinitionMinDamage(ParseInt(value, key));
                return true;
            }
            case "minDamageRange":
            {
                draft.SetDefinitionMinDamageRange(ParseFloat(value, key));
                return true;
            }
            case "minHorTurnSpeed":
            {
                draft.SetDefinitionTurnSpeedAndRangeMinHorizontalTurnSpeed(ParseFloat(value, key));
                return true;
            }
            case "minPlayerDamage":
            {
                draft.SetDefinitionMinPlayerDamage(ParseInt(value, key));
                return true;
            }
            case "minVertTurnSpeed":
            {
                draft.SetDefinitionTurnSpeedAndRangeMinVerticalTurnSpeed(ParseFloat(value, key));
                return true;
            }
            case "missileConeSoundAlias":
            {
                draft.SetDefinitionMissileConeSoundAlias(NullableText(value));
                return true;
            }
            case "missileConeSoundAliasAtBase":
            {
                draft.SetDefinitionMissileConeSoundAliasAtBase(NullableText(value));
                return true;
            }
            case "missileConeSoundCrossfadeBottomSize":
            {
                draft.SetDefinitionMissileConeSoundCrossfadeBottomSize(ParseFloat(value, key));
                return true;
            }
            case "missileConeSoundCrossfadeEnabled":
            {
                draft.SetDefinitionTailFlagsMissileConeSoundCrossfadeEnabled(ParseBoolean(value, key));
                return true;
            }
            case "missileConeSoundCrossfadeTopSize":
            {
                draft.SetDefinitionMissileConeSoundCrossfadeTopSize(ParseFloat(value, key));
                return true;
            }
            case "missileConeSoundEnabled":
            {
                draft.SetDefinitionTailFlagsMissileConeSoundEnabled(ParseBoolean(value, key));
                return true;
            }
            case "missileConeSoundHeight":
            {
                draft.SetDefinitionMissileConeSoundHeight(ParseFloat(value, key));
                return true;
            }
            case "missileConeSoundOriginOffset":
            {
                draft.SetDefinitionMissileConeSoundOriginOffset(ParseFloat(value, key));
                return true;
            }
            case "missileConeSoundPitchAtBottom":
            {
                draft.SetDefinitionMissileConeSoundPitchAtBottom(ParseFloat(value, key));
                return true;
            }
            case "missileConeSoundPitchAtTop":
            {
                draft.SetDefinitionMissileConeSoundPitchAtTop(ParseFloat(value, key));
                return true;
            }
            case "missileConeSoundPitchBottomSize":
            {
                draft.SetDefinitionMissileConeSoundPitchBottomSize(ParseFloat(value, key));
                return true;
            }
            case "missileConeSoundPitchTopSize":
            {
                draft.SetDefinitionMissileConeSoundPitchTopSize(ParseFloat(value, key));
                return true;
            }
            case "missileConeSoundPitchshiftEnabled":
            {
                draft.SetDefinitionTailFlagsMissileConeSoundPitchShiftEnabled(ParseBoolean(value, key));
                return true;
            }
            case "missileConeSoundRadiusAtBase":
            {
                draft.SetDefinitionMissileConeSoundRadiusAtBase(ParseFloat(value, key));
                return true;
            }
            case "missileConeSoundRadiusAtTop":
            {
                draft.SetDefinitionMissileConeSoundRadiusAtTop(ParseFloat(value, key));
                return true;
            }
            case "missileConeSoundVolumescaleAtCore":
            {
                draft.SetDefinitionMissileConeSoundVolumeScaleAtCore(ParseFloat(value, key));
                return true;
            }
            case "missileConeSoundVolumescaleAtEdge":
            {
                draft.SetDefinitionMissileConeSoundVolumeScaleAtEdge(ParseFloat(value, key));
                return true;
            }
            case "missileConeSoundVolumescaleCoreSize":
            {
                draft.SetDefinitionMissileConeSoundVolumeScaleCoreSize(ParseFloat(value, key));
                return true;
            }
            case "modeName":
            {
                draft.SetDefinitionModeName(NullableText(value));
                return true;
            }
            case "motionTracker":
            {
                draft.SetVariantMotionTracker(ParseBoolean(value, key));
                return true;
            }
            case "moveSpeedScale":
            {
                draft.SetDefinitionAimMovementTuningMoveSpeedScale(ParseFloat(value, key));
                return true;
            }
            case "nightVisionRemoveTime":
            {
                draft.SetDefinitionTimingNightVisionRemoveTime(ParseMilliseconds(value, key));
                return true;
            }
            case "nightVisionRemoveTimeFadeInStart":
            {
                draft.SetDefinitionTimingNightVisionRemoveTimeFadeInStart(ParseMilliseconds(value, key));
                return true;
            }
            case "nightVisionRemoveTimePowerDown":
            {
                draft.SetDefinitionTimingNightVisionRemoveTimePowerDown(ParseMilliseconds(value, key));
                return true;
            }
            case "nightVisionWearTime":
            {
                draft.SetDefinitionTimingNightVisionWearTime(ParseMilliseconds(value, key));
                return true;
            }
            case "nightVisionWearTimeFadeOutEnd":
            {
                draft.SetDefinitionTimingNightVisionWearTimeFadeOutEnd(ParseMilliseconds(value, key));
                return true;
            }
            case "nightVisionWearTimePowerUp":
            {
                draft.SetDefinitionTimingNightVisionWearTimePowerUp(ParseMilliseconds(value, key));
                return true;
            }
            case "noAdsWhenMagEmpty":
            {
                draft.SetDefinitionTailFlagsNoAdsWhenMagEmpty(ParseBoolean(value, key));
                return true;
            }
            case "noAmmoPickup":
            {
                draft.SetDefinitionTailFlagsNoAmmoPickup(ParseBoolean(value, key));
                return true;
            }
            case "noDualWield":
            {
                draft.SetDefinitionTailFlagsNoDualWield(ParseBoolean(value, key));
                return true;
            }
            case "noPartialReload":
            {
                draft.SetDefinitionTailFlagsNoPartialReload(ParseBoolean(value, key));
                return true;
            }
            case "offhandClass":
            {
                draft.SetDefinitionOffhandClass(ParseEnum<OffhandClass>(value, key));
                return true;
            }
            case "offhandHoldIsCancelable":
            {
                draft.SetDefinitionTailFlagsOffhandHoldIsCancelable(ParseBoolean(value, key));
                return true;
            }
            case "penetrateMultiplier":
            {
                draft.SetVariantPenetrateMultiplier(ParseFloat(value, key));
                return true;
            }
            case "penetrateType":
            {
                draft.SetDefinitionPenetrateType(ParseEnum<PenetrateType>(value, key));
                return true;
            }
            case "physCollmap":
            {
                draft.SetDefinitionPhysCollmap(ResolveAsset<PhysCollmapAsset>(value, key, pool, XAssetType.PhysCollmap));
                draft.SetDefinitionPhysCollmapName(NullableText(value));
                return true;
            }
            case "pickupIcon":
            {
                draft.SetDefinitionIconsPickupIcon(ResolveAsset<MaterialAsset>(value, key, pool, XAssetType.Material));
                return true;
            }
            case "pickupIconRatio":
            {
                draft.SetDefinitionIconsPickupIconRatio(ParseEnum<WeaponIconRatio>(value, key));
                return true;
            }
            case "pitchConvergenceTime":
            {
                draft.SetDefinitionTurnSpeedAndRangePitchConvergenceTime(ParseFloat(value, key));
                return true;
            }
            case "playerAnimType":
            {
                draft.SetDefinitionPlayerAnimType(ParseEnumIndex(value, key, 18));
                return true;
            }
            case "playerDamage":
            {
                draft.SetDefinitionAmmoPlayerDamage(ParseInt(value, key));
                return true;
            }
            case "playerPositionDist":
            {
                draft.SetDefinitionTurnSpeedAndRangePlayerPositionDistance(ParseFloat(value, key));
                return true;
            }
            case "playerSpread":
            {
                draft.SetDefinitionAccuracyPlayerSpread(ParseFloat(value, key));
                return true;
            }
            case "posMoveRate":
            {
                draft.SetDefinitionPositionalMovementPositionMoveRate(ParseFloat(value, key));
                return true;
            }
            case "posProneMoveRate":
            {
                draft.SetDefinitionPositionalMovementPositionProneMoveRate(ParseFloat(value, key));
                return true;
            }
            case "posProneRotRate":
            {
                draft.SetDefinitionPositionalMovementPositionProneRotationRate(ParseFloat(value, key));
                return true;
            }
            case "posRotRate":
            {
                draft.SetDefinitionPositionalMovementPositionRotationRate(ParseFloat(value, key));
                return true;
            }
            case "projBeaconEffect":
            {
                draft.SetDefinitionProjectileBeaconEffect(ResolveAsset<FxEffectDefAsset>(value, key, pool, XAssetType.Fx));
                return true;
            }
            case "projDudEffect":
            {
                draft.SetDefinitionProjectileDudEffect(ResolveAsset<FxEffectDefAsset>(value, key, pool, XAssetType.Fx));
                return true;
            }
            case "projDudSound":
            {
                draft.SetDefinitionProjectileDudSound(NullableText(value));
                return true;
            }
            case "projExplosionEffect":
            {
                draft.SetDefinitionProjectileExplosionEffect(ResolveAsset<FxEffectDefAsset>(value, key, pool, XAssetType.Fx));
                return true;
            }
            case "projExplosionEffectForceNormalUp":
            {
                draft.SetDefinitionTailFlagsProjectileExplosionEffectForceNormalUp(ParseBoolean(value, key));
                return true;
            }
            case "projExplosionSound":
            {
                draft.SetDefinitionProjectileExplosionSound(NullableText(value));
                return true;
            }
            case "projExplosionType":
            {
                draft.SetDefinitionProjectileExplosion(ParseEnum<WeaponProjectileExplosion>(value, key));
                return true;
            }
            case "projIgnitionDelay":
            {
                draft.SetDefinitionProjectileIgnitionDelay(ParseInt(value, key));
                return true;
            }
            case "projIgnitionEffect":
            {
                draft.SetDefinitionProjectileIgnitionEffect(ResolveAsset<FxEffectDefAsset>(value, key, pool, XAssetType.Fx));
                return true;
            }
            case "projIgnitionSound":
            {
                draft.SetDefinitionProjectileIgnitionSound(NullableText(value));
                return true;
            }
            case "projImpactExplode":
            {
                draft.SetDefinitionTailFlagsProjectileImpactExplode(ParseBoolean(value, key));
                return true;
            }
            case "projTrailEffect":
            {
                draft.SetDefinitionProjectileTrailEffect(ResolveAsset<FxEffectDefAsset>(value, key, pool, XAssetType.Fx));
                return true;
            }
            case "projectileActivateDist":
            {
                draft.SetDefinitionPhysicsProjectileActivateDistance(ParseInt(value, key));
                return true;
            }
            case "projectileBlue":
            {
                var color = draft.Definition!.Projectile.ProjectileColor;
                color.Z = ParseFloat(value, key);
                draft.SetDefinitionProjectileProjectileColor(color);
                return true;
            }
            case "projectileCurvature":
            {
                draft.SetDefinitionPhysicsProjectileCurvature(ParseFloat(value, key));
                return true;
            }
            case "projectileGreen":
            {
                var color = draft.Definition!.Projectile.ProjectileColor;
                color.Y = ParseFloat(value, key);
                draft.SetDefinitionProjectileProjectileColor(color);
                return true;
            }
            case "projectileLifetime":
            {
                draft.SetDefinitionPhysicsProjectileLifetime(ParseIntegerAsFloat(value, key));
                return true;
            }
            case "projectileModel":
            {
                draft.SetDefinitionProjectileModel(ResolveAsset<XModelAsset>(value, key, pool, XAssetType.XModel));
                return true;
            }
            case "projectileRed":
            {
                var color = draft.Definition!.Projectile.ProjectileColor;
                color.X = ParseFloat(value, key);
                draft.SetDefinitionProjectileProjectileColor(color);
                return true;
            }
            case "projectileSpeed":
            {
                draft.SetDefinitionPhysicsProjectileSpeed(ParseInt(value, key));
                return true;
            }
            case "projectileSpeedForward":
            {
                draft.SetDefinitionPhysicsProjectileSpeedForward(ParseInt(value, key));
                return true;
            }
            case "projectileSpeedUp":
            {
                draft.SetDefinitionPhysicsProjectileSpeedUp(ParseInt(value, key));
                return true;
            }
            case "proneMoveF":
            {
                var vector = draft.Definition!.ViewMovement.ProneMove;
                vector.X = ParseFloat(value, key);
                draft.SetDefinitionViewMovementProneMove(vector);
                return true;
            }
            case "proneMoveMinSpeed":
            {
                draft.SetDefinitionPositionalMovementProneMoveMinSpeed(ParseFloat(value, key));
                return true;
            }
            case "proneMoveR":
            {
                var vector = draft.Definition!.ViewMovement.ProneMove;
                vector.Y = ParseFloat(value, key);
                draft.SetDefinitionViewMovementProneMove(vector);
                return true;
            }
            case "proneMoveU":
            {
                var vector = draft.Definition!.ViewMovement.ProneMove;
                vector.Z = ParseFloat(value, key);
                draft.SetDefinitionViewMovementProneMove(vector);
                return true;
            }
            case "proneOfsF":
            {
                var vector = draft.Definition!.ViewMovement.ProneOffset;
                vector.X = ParseFloat(value, key);
                draft.SetDefinitionViewMovementProneOffset(vector);
                return true;
            }
            case "proneOfsR":
            {
                var vector = draft.Definition!.ViewMovement.ProneOffset;
                vector.Y = ParseFloat(value, key);
                draft.SetDefinitionViewMovementProneOffset(vector);
                return true;
            }
            case "proneOfsU":
            {
                var vector = draft.Definition!.ViewMovement.ProneOffset;
                vector.Z = ParseFloat(value, key);
                draft.SetDefinitionViewMovementProneOffset(vector);
                return true;
            }
            case "proneRotMinSpeed":
            {
                draft.SetDefinitionPositionalMovementProneRotationMinSpeed(ParseFloat(value, key));
                return true;
            }
            case "proneRotP":
            {
                var vector = draft.Definition!.ViewMovement.ProneRotation;
                vector.X = ParseFloat(value, key);
                draft.SetDefinitionViewMovementProneRotation(vector);
                return true;
            }
            case "proneRotR":
            {
                var vector = draft.Definition!.ViewMovement.ProneRotation;
                vector.Z = ParseFloat(value, key);
                draft.SetDefinitionViewMovementProneRotation(vector);
                return true;
            }
            case "proneRotY":
            {
                var vector = draft.Definition!.ViewMovement.ProneRotation;
                vector.Y = ParseFloat(value, key);
                draft.SetDefinitionViewMovementProneRotation(vector);
                return true;
            }
            case "quickDropTime":
            {
                draft.SetDefinitionTimingQuickDropTime(ParseMilliseconds(value, key));
                return true;
            }
            case "quickRaiseTime":
            {
                draft.SetDefinitionTimingQuickRaiseTime(ParseMilliseconds(value, key));
                return true;
            }
            case "raiseTime":
            {
                draft.SetDefinitionTimingRaiseTime(ParseMilliseconds(value, key));
                return true;
            }
            case "rechamberBoltTime":
            {
                draft.SetDefinitionTimingRechamberBoltTime(ParseMilliseconds(value, key));
                return true;
            }
            case "rechamberTime":
            {
                draft.SetDefinitionTimingRechamberTime(ParseMilliseconds(value, key));
                return true;
            }
            case "rechamberTimeOneHanded":
            {
                draft.SetDefinitionTimingRechamberTimeOneHanded(ParseMilliseconds(value, key));
                return true;
            }
            case "rechamberWhileAds":
            {
                draft.SetDefinitionTailFlagsRechamberWhileAds(ParseBoolean(value, key));
                return true;
            }
            case "reloadAddTime":
            {
                draft.SetDefinitionTimingReloadAddTime(ParseMilliseconds(value, key));
                return true;
            }
            case "reloadAmmoAdd":
            {
                draft.SetDefinitionPhysicsReloadAmmoAdd(ParseInt(value, key));
                return true;
            }
            case "reloadEmptyTime":
            {
                draft.SetDefinitionTimingReloadEmptyTime(ParseMilliseconds(value, key));
                return true;
            }
            case "reloadEndTime":
            {
                draft.SetDefinitionTimingReloadEndTime(ParseMilliseconds(value, key));
                return true;
            }
            case "reloadShowRocketTime":
            {
                draft.SetDefinitionTimingReloadShowRocketTime(ParseMilliseconds(value, key));
                return true;
            }
            case "reloadStartAdd":
            {
                draft.SetDefinitionPhysicsReloadStartAdd(ParseInt(value, key));
                return true;
            }
            case "reloadStartAddTime":
            {
                draft.SetDefinitionTimingReloadStartAddTime(ParseMilliseconds(value, key));
                return true;
            }
            case "reloadStartTime":
            {
                draft.SetDefinitionTimingReloadStartTime(ParseMilliseconds(value, key));
                return true;
            }
            case "reloadTime":
            {
                draft.SetDefinitionTimingReloadTime(ParseMilliseconds(value, key));
                return true;
            }
            case "requireLockonToFire":
            {
                draft.SetDefinitionTailFlagsRequireLockonToFire(ParseBoolean(value, key));
                return true;
            }
            case "reticleCenter":
            {
                draft.SetDefinitionReticleCenterMaterial(ResolveAsset<MaterialAsset>(value, key, pool, XAssetType.Material));
                return true;
            }
            case "reticleCenterSize":
            {
                draft.SetDefinitionReticleCenterSize(ParseInt(value, key));
                return true;
            }
            case "reticleMinOfs":
            {
                draft.SetDefinitionReticleMinOffset(ParseInt(value, key));
                return true;
            }
            case "reticleSide":
            {
                draft.SetDefinitionReticleSideMaterial(ResolveAsset<MaterialAsset>(value, key, pool, XAssetType.Material));
                return true;
            }
            case "reticleSideSize":
            {
                draft.SetDefinitionReticleSideSize(ParseInt(value, key));
                return true;
            }
            case "ricochetChance":
            {
                draft.SetDefinitionProjectileRicochetChance(ParseFloat(value, key));
                return true;
            }
            case "rifleBullet":
            {
                draft.SetDefinitionTailFlagsRifleBullet(ParseBoolean(value, key));
                return true;
            }
            case "rightArc":
            {
                draft.SetDefinitionAccuracyRightArc(ParseFloat(value, key));
                return true;
            }
            case "rocketModel":
            {
                draft.SetDefinitionRocketModel(ResolveAsset<XModelAsset>(value, key, pool, XAssetType.XModel));
                return true;
            }
            case "rotate":
            {
                draft.SetDefinitionTailFlagsRotate(ParseBoolean(value, key));
                return true;
            }
            case "scanAccel":
            {
                draft.SetDefinitionHintsScanAcceleration(ParseFloat(value, key));
                return true;
            }
            case "scanPauseTime":
            {
                draft.SetDefinitionHintsScanPauseTime(ParseMilliseconds(value, key));
                return true;
            }
            case "scanSpeed":
            {
                draft.SetDefinitionHintsScanSpeed(ParseFloat(value, key));
                return true;
            }
            case "script":
            {
                draft.SetDefinitionScriptName(NullableText(value));
                return true;
            }
            case "segmentedReload":
            {
                draft.SetDefinitionTailFlagsSegmentedReload(ParseBoolean(value, key));
                return true;
            }
            case "shareAmmo":
            {
                draft.SetDefinitionTailFlagsSharedAmmo(ParseBoolean(value, key));
                return true;
            }
            case "sharedAmmoCap":
            {
                draft.SetDefinitionAmmoSharedAmmoCap(ParseInt(value, key));
                return true;
            }
            case "sharedAmmoCapName":
            {
                draft.SetDefinitionAmmoSharedAmmoCapName(NullableText(value));
                return true;
            }
            case "shotCount":
            {
                draft.SetDefinitionAmmoShotCount(ParseInt(value, key));
                return true;
            }
            case "silenced":
            {
                draft.SetDefinitionTailFlagsSilenced(ParseBoolean(value, key));
                return true;
            }
            case "sprintDurationScale":
            {
                draft.SetDefinitionAimMovementTuningSprintDurationScale(ParseFloat(value, key));
                return true;
            }
            case "sprintInTime":
            {
                draft.SetDefinitionTimingSprintInTime(ParseMilliseconds(value, key));
                return true;
            }
            case "sprintLoopTime":
            {
                draft.SetDefinitionTimingSprintLoopTime(ParseMilliseconds(value, key));
                return true;
            }
            case "sprintOutTime":
            {
                draft.SetDefinitionTimingSprintOutTime(ParseMilliseconds(value, key));
                return true;
            }
            case "stance":
            {
                draft.SetDefinitionStance(ParseEnum<WeaponStance>(value, key));
                return true;
            }
            case "standMoveF":
            {
                var vector = draft.Definition!.ViewMovement.StandMove;
                vector.X = ParseFloat(value, key);
                draft.SetDefinitionViewMovementStandMove(vector);
                return true;
            }
            case "standMoveMinSpeed":
            {
                draft.SetDefinitionPositionalMovementStandMoveMinSpeed(ParseFloat(value, key));
                return true;
            }
            case "standMoveR":
            {
                var vector = draft.Definition!.ViewMovement.StandMove;
                vector.Y = ParseFloat(value, key);
                draft.SetDefinitionViewMovementStandMove(vector);
                return true;
            }
            case "standMoveU":
            {
                var vector = draft.Definition!.ViewMovement.StandMove;
                vector.Z = ParseFloat(value, key);
                draft.SetDefinitionViewMovementStandMove(vector);
                return true;
            }
            case "standRotMinSpeed":
            {
                draft.SetDefinitionPositionalMovementStandRotationMinSpeed(ParseFloat(value, key));
                return true;
            }
            case "standRotP":
            {
                var vector = draft.Definition!.ViewMovement.StandRotation;
                vector.X = ParseFloat(value, key);
                draft.SetDefinitionViewMovementStandRotation(vector);
                return true;
            }
            case "standRotR":
            {
                var vector = draft.Definition!.ViewMovement.StandRotation;
                vector.Z = ParseFloat(value, key);
                draft.SetDefinitionViewMovementStandRotation(vector);
                return true;
            }
            case "standRotY":
            {
                var vector = draft.Definition!.ViewMovement.StandRotation;
                vector.Y = ParseFloat(value, key);
                draft.SetDefinitionViewMovementStandRotation(vector);
                return true;
            }
            case "startAmmo":
            {
                draft.SetDefinitionIconsStartAmmo(ParseInt(value, key));
                return true;
            }
            case "stickToPlayers":
            {
                draft.SetDefinitionTailFlagsStickToPlayers(ParseBoolean(value, key));
                return true;
            }
            case "stickiness":
            {
                draft.SetDefinitionProjectileStickiness(ParseEnum<WeaponStickiness>(value, key));
                return true;
            }
            case "strafeMoveF":
            {
                var vector = draft.Definition!.ViewMovement.StrafeMove;
                vector.X = ParseFloat(value, key);
                draft.SetDefinitionViewMovementStrafeMove(vector);
                return true;
            }
            case "strafeMoveR":
            {
                var vector = draft.Definition!.ViewMovement.StrafeMove;
                vector.Y = ParseFloat(value, key);
                draft.SetDefinitionViewMovementStrafeMove(vector);
                return true;
            }
            case "strafeMoveU":
            {
                var vector = draft.Definition!.ViewMovement.StrafeMove;
                vector.Z = ParseFloat(value, key);
                draft.SetDefinitionViewMovementStrafeMove(vector);
                return true;
            }
            case "strafeRotP":
            {
                var vector = draft.Definition!.ViewMovement.StrafeRotation;
                vector.X = ParseFloat(value, key);
                draft.SetDefinitionViewMovementStrafeRotation(vector);
                return true;
            }
            case "strafeRotR":
            {
                var vector = draft.Definition!.ViewMovement.StrafeRotation;
                vector.Z = ParseFloat(value, key);
                draft.SetDefinitionViewMovementStrafeRotation(vector);
                return true;
            }
            case "strafeRotY":
            {
                var vector = draft.Definition!.ViewMovement.StrafeRotation;
                vector.Y = ParseFloat(value, key);
                draft.SetDefinitionViewMovementStrafeRotation(vector);
                return true;
            }
            case "stunnedTimeBegin":
            {
                draft.SetDefinitionTimingStunnedTimeBegin(ParseMilliseconds(value, key));
                return true;
            }
            case "stunnedTimeEnd":
            {
                draft.SetDefinitionTimingStunnedTimeEnd(ParseMilliseconds(value, key));
                return true;
            }
            case "stunnedTimeLoop":
            {
                draft.SetDefinitionTimingStunnedTimeLoop(ParseMilliseconds(value, key));
                return true;
            }
            case "suppressAmmoReserveDisplay":
            {
                draft.SetDefinitionTailFlagsSuppressAmmoReserveDisplay(ParseBoolean(value, key));
                return true;
            }
            case "suppressionTime":
            {
                draft.SetDefinitionTurnSpeedAndRangeSuppressionTime(ParseFloat(value, key));
                return true;
            }
            case "swayHorizScale":
            {
                draft.SetDefinitionAdsViewAndSpreadSwayHorizontalScale(ParseFloat(value, key));
                return true;
            }
            case "swayLerpSpeed":
            {
                draft.SetDefinitionAdsViewAndSpreadSwayLerpSpeed(ParseFloat(value, key));
                return true;
            }
            case "swayMaxAngle":
            {
                draft.SetDefinitionAdsViewAndSpreadSwayMaxAngle(ParseFloat(value, key));
                return true;
            }
            case "swayPitchScale":
            {
                draft.SetDefinitionAdsViewAndSpreadSwayPitchScale(ParseFloat(value, key));
                return true;
            }
            case "swayShellShockScale":
            {
                draft.SetDefinitionAdsViewAndSpreadSwayShellShockScale(ParseFloat(value, key));
                return true;
            }
            case "swayVertScale":
            {
                draft.SetDefinitionAdsViewAndSpreadSwayVerticalScale(ParseFloat(value, key));
                return true;
            }
            case "swayYawScale":
            {
                draft.SetDefinitionAdsViewAndSpreadSwayYawScale(ParseFloat(value, key));
                return true;
            }
            case "thermalScope":
            {
                draft.SetDefinitionTailFlagsThermalScope(ParseBoolean(value, key));
                return true;
            }
            case "timeToAccelerate":
            {
                draft.SetDefinitionPhysicsTimeToAccelerate(ParseIntegerAsFloat(value, key));
                return true;
            }
            case "timedDetonation":
            {
                draft.SetDefinitionTailFlagsTimedDetonation(ParseBoolean(value, key));
                return true;
            }
            case "topArc":
            {
                draft.SetDefinitionAccuracyTopArc(ParseFloat(value, key));
                return true;
            }
            case "tracerType":
            {
                draft.SetDefinitionTracer(ResolveAsset<TracerDefAsset>(value, key, pool, XAssetType.Tracer));
                return true;
            }
            case "turretBarrelSpinDownTime":
            {
                draft.SetDefinitionTurretBarrelSpinDownTime(ParseFloat(value, key));
                return true;
            }
            case "turretBarrelSpinEnabled":
            {
                draft.SetDefinitionTailFlagsTurretBarrelSpinEnabled(ParseBoolean(value, key));
                return true;
            }
            case "turretBarrelSpinMaxSnd":
            {
                draft.SetDefinitionTurretBarrelSpinMaxSound(NullableText(value));
                return true;
            }
            case "turretBarrelSpinRumble":
            {
                draft.SetDefinitionTurretBarrelSpinRumble(NullableText(value));
                return true;
            }
            case "turretBarrelSpinSpeed":
            {
                draft.SetDefinitionTurretBarrelSpinSpeed(ParseFloat(value, key));
                return true;
            }
            case "turretBarrelSpinUpTime":
            {
                draft.SetDefinitionTurretBarrelSpinUpTime(ParseFloat(value, key));
                return true;
            }
            case "turretOverheatDownRate":
            {
                draft.SetDefinitionTurretOverheatDownRate(ParseFloat(value, key));
                return true;
            }
            case "turretOverheatEffect":
            {
                draft.SetDefinitionTurretOverheatEffect(ResolveAsset<FxEffectDefAsset>(value, key, pool, XAssetType.Fx));
                return true;
            }
            case "turretOverheatPenalty":
            {
                draft.SetDefinitionTurretOverheatPenalty(ParseFloat(value, key));
                return true;
            }
            case "turretOverheatSound":
            {
                draft.SetDefinitionTurretOverheatSound(NullableText(value));
                return true;
            }
            case "turretOverheatUpRate":
            {
                draft.SetDefinitionTurretOverheatUpRate(ParseFloat(value, key));
                return true;
            }
            case "turretScopeZoomMax":
            {
                draft.SetDefinitionTurretScopeZoomMax(ParseFloat(value, key));
                return true;
            }
            case "turretScopeZoomMin":
            {
                draft.SetDefinitionTurretScopeZoomMin(ParseFloat(value, key));
                return true;
            }
            case "turretScopeZoomRate":
            {
                draft.SetDefinitionTurretScopeZoomRate(ParseFloat(value, key));
                return true;
            }
            case "useHintString":
            {
                draft.SetDefinitionHintsUseHintString(NullableText(value));
                return true;
            }
            case "vertViewJitter":
            {
                draft.SetDefinitionHintsVerticalViewJitter(ParseFloat(value, key));
                return true;
            }
            case "viewFlashEffect":
            {
                draft.SetDefinitionFlashEffectsView(ResolveAsset<FxEffectDefAsset>(value, key, pool, XAssetType.Fx));
                return true;
            }
            case "viewLastShotEjectEffect":
            {
                draft.SetDefinitionShellEjectEffectsViewLastShot(ResolveAsset<FxEffectDefAsset>(value, key, pool, XAssetType.Fx));
                return true;
            }
            case "viewShellEjectEffect":
            {
                draft.SetDefinitionShellEjectEffectsView(ResolveAsset<FxEffectDefAsset>(value, key, pool, XAssetType.Fx));
                return true;
            }
            case "weaponClass":
            {
                draft.SetDefinitionWeaponClass(ParseEnum<WeaponClass>(value, key));
                return true;
            }
            case "weaponType":
            {
                draft.SetDefinitionWeaponType(ParseEnum<WeaponType>(value, key));
                return true;
            }
            case "worldClipModel":
            {
                draft.SetDefinitionWorldClipModel(ResolveAsset<XModelAsset>(value, key, pool, XAssetType.XModel));
                return true;
            }
            case "worldFlashEffect":
            {
                draft.SetDefinitionFlashEffectsWorld(ResolveAsset<FxEffectDefAsset>(value, key, pool, XAssetType.Fx));
                return true;
            }
            case "worldKnifeModel":
            {
                draft.SetDefinitionWorldKnifeModel(ResolveAsset<XModelAsset>(value, key, pool, XAssetType.XModel));
                return true;
            }
            case "worldLastShotEjectEffect":
            {
                draft.SetDefinitionShellEjectEffectsWorldLastShot(ResolveAsset<FxEffectDefAsset>(value, key, pool, XAssetType.Fx));
                return true;
            }
            case "worldShellEjectEffect":
            {
                draft.SetDefinitionShellEjectEffectsWorld(ResolveAsset<FxEffectDefAsset>(value, key, pool, XAssetType.Fx));
                return true;
            }
            case "yawConvergenceTime":
            {
                draft.SetDefinitionTurnSpeedAndRangeYawConvergenceTime(ParseFloat(value, key));
                return true;
            }
            default:
                return false;
        }
    }

    private static string? NullableText(string value) =>
        value.Length == 0 ? null : value;

    private static int ParseInt(string value, string key) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : throw new InvalidDataException($"Weapon field '{key}' requires an integer.");

    private static float ParseFloat(string value, string key)
    {
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                out float parsed) || !float.IsFinite(parsed))
            throw new InvalidDataException($"Weapon field '{key}' requires a finite number.");
        return parsed;
    }

    private static int ParseMilliseconds(string value, string key)
    {
        double milliseconds = (double)ParseFloat(value, key) * 1000.0;
        if (milliseconds < 0 || milliseconds > int.MaxValue)
            throw new InvalidDataException($"Weapon field '{key}' is outside the millisecond range.");
        return checked((int)Math.Round(milliseconds, MidpointRounding.AwayFromZero));
    }

    private static int ParseIntegerAsFloat(string value, string key)
    {
        float parsed = ParseFloat(value, key);
        if ((double)parsed < int.MinValue || (double)parsed > int.MaxValue ||
            parsed != MathF.Truncate(parsed))
            throw new InvalidDataException($"Weapon field '{key}' requires an integer value.");
        return checked((int)parsed);
    }

    private static float ParseFloatBits(string value, string key)
    {
        float parsed = BitConverter.Int32BitsToSingle(ParseInt(value, key));
        if (!float.IsFinite(parsed))
            throw new InvalidDataException($"Weapon field '{key}' has non-finite float bits.");
        return parsed;
    }

    private static byte ParseBoolean(string value, string key) => value switch
    {
        "0" => 0,
        "1" => 1,
        _ => throw new InvalidDataException($"Weapon field '{key}' requires 0 or 1.")
    };

    private static int ParseEnumIndex(string value, string key, int count)
    {
        int index = ParseInt(value, key);
        if ((uint)index >= (uint)count)
            throw new InvalidDataException($"Weapon field '{key}' has an unknown enum value.");
        return index;
    }

    private static TEnum ParseEnum<TEnum>(string value, string key)
        where TEnum : struct, Enum
    {
        int index = ParseInt(value, key);
        if (!Enum.IsDefined(typeof(TEnum), index))
            throw new InvalidDataException($"Weapon field '{key}' has an unknown enum value.");
        return (TEnum)Enum.ToObject(typeof(TEnum), index);
    }

    private static TAsset? ResolveAsset<TAsset>(
        string value, string key, XAssetPool pool, XAssetType type)
        where TAsset : BaseAsset
    {
        if (value.Length == 0)
            return null;
        if (!pool.TryResolve(type, value, out TAsset? asset) || asset is null ||
            !pool.TryGetEntry(asset, out XAssetPoolEntry entry) || entry.IsReferencePlaceholder)
            throw new InvalidDataException(
                $"Weapon field '{key}' references missing or unresolved {type} '{value}'.");
        return asset;
    }
}
