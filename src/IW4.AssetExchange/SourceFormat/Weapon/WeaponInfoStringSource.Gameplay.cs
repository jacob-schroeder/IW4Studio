using IW4.AssetExchange.SourceFormat.InfoString;
using IW4.Assets.Assets.Weapon;

namespace IW4.AssetExchange.SourceFormat.Weapon;

internal static partial class WeaponInfoStringSource
{
    private static void AddAmmoTimingAndFlags(
        InfoStringSourceWriter source,
        WeaponVariantDef variant,
        WeaponDef definition,
        string assetName)
    {
        WeaponAmmoFields ammo = definition.Ammo;
        if (ammo.DamageType != 0)
        {
            throw new InvalidDataException(
                $"Weapon '{assetName}' has damage type {ammo.DamageType}, which the IW4 source format does not expose.");
        }

        source.AddInt("damage", ammo.Damage);
        source.AddInt("playerDamage", ammo.PlayerDamage);
        source.AddInt("meleeDamage", ammo.MeleeDamage);
        source.AddInt("minDamage", definition.MinDamage);
        source.AddInt("minPlayerDamage", definition.MinPlayerDamage);
        source.AddFloat("maxDamageRange", definition.MaxDamageRange);
        source.AddFloat("minDamageRange", definition.MinDamageRange);
        source.AddFloat("destabilizationRateTime", definition.DestabilizationRateTime);
        source.AddFloat("destabilizationCurvatureMax", definition.DestabilizationCurvatureMax);
        source.AddInt("destabilizeDistance", definition.DestabilizeDistance);

        WeaponTimingFields timing = definition.Timing;
        source.AddMilliseconds("fireDelay", timing.FireDelay);
        source.AddMilliseconds("meleeDelay", timing.MeleeDelay);
        source.AddMilliseconds("meleeChargeDelay", timing.MeleeChargeDelay);
        source.AddMilliseconds("fireTime", variant.FireTime);
        source.AddMilliseconds("rechamberTime", timing.RechamberTime);
        source.AddMilliseconds("rechamberTimeOneHanded", timing.RechamberTimeOneHanded);
        source.AddMilliseconds("rechamberBoltTime", timing.RechamberBoltTime);
        source.AddMilliseconds("holdFireTime", timing.HoldFireTime);
        source.AddMilliseconds("detonateTime", timing.DetonateTime);
        source.AddMilliseconds("detonateDelay", timing.DetonateDelay);
        source.AddMilliseconds("meleeTime", timing.MeleeTime);
        source.AddMilliseconds("meleeChargeTime", timing.MeleeChargeTime);
        source.AddMilliseconds("reloadTime", timing.ReloadTime);
        source.AddMilliseconds("reloadShowRocketTime", timing.ReloadShowRocketTime);
        source.AddMilliseconds("reloadEmptyTime", timing.ReloadEmptyTime);
        source.AddMilliseconds("reloadAddTime", timing.ReloadAddTime);
        source.AddMilliseconds("reloadStartTime", timing.ReloadStartTime);
        source.AddMilliseconds("reloadStartAddTime", timing.ReloadStartAddTime);
        source.AddMilliseconds("reloadEndTime", timing.ReloadEndTime);
        source.AddMilliseconds("dropTime", timing.DropTime);
        source.AddMilliseconds("raiseTime", timing.RaiseTime);
        source.AddMilliseconds("altDropTime", timing.AltDropTime);
        source.AddMilliseconds("altRaiseTime", variant.AlternateRaiseTime);
        source.AddMilliseconds("quickDropTime", timing.QuickDropTime);
        source.AddMilliseconds("quickRaiseTime", timing.QuickRaiseTime);
        source.AddMilliseconds("firstRaiseTime", variant.FirstRaiseTime);
        source.AddMilliseconds("breachRaiseTime", timing.BreachRaiseTime);
        source.AddMilliseconds("emptyRaiseTime", timing.EmptyRaiseTime);
        source.AddMilliseconds("emptyDropTime", timing.EmptyDropTime);
        source.AddMilliseconds("sprintInTime", timing.SprintInTime);
        source.AddMilliseconds("sprintLoopTime", timing.SprintLoopTime);
        source.AddMilliseconds("sprintOutTime", timing.SprintOutTime);
        source.AddMilliseconds("stunnedTimeBegin", timing.StunnedTimeBegin);
        source.AddMilliseconds("stunnedTimeLoop", timing.StunnedTimeLoop);
        source.AddMilliseconds("stunnedTimeEnd", timing.StunnedTimeEnd);
        source.AddMilliseconds("nightVisionWearTime", timing.NightVisionWearTime);
        source.AddMilliseconds("nightVisionWearTimeFadeOutEnd", timing.NightVisionWearTimeFadeOutEnd);
        source.AddMilliseconds("nightVisionWearTimePowerUp", timing.NightVisionWearTimePowerUp);
        source.AddMilliseconds("nightVisionRemoveTime", timing.NightVisionRemoveTime);
        source.AddMilliseconds("nightVisionRemoveTimePowerDown", timing.NightVisionRemoveTimePowerDown);
        source.AddMilliseconds("nightVisionRemoveTimeFadeInStart", timing.NightVisionRemoveTimeFadeInStart);
        source.AddMilliseconds("fuseTime", timing.FuseTime);
        source.AddMilliseconds("aifuseTime", timing.AiFuseTime);

        WeaponTailFlags flags = definition.TailFlags;
        source.AddBoolean("lockonSupported", flags.LockonSupported);
        source.AddBoolean("requireLockonToFire", flags.RequireLockonToFire);
        source.AddBoolean("bigExplosion", flags.BigExplosion);
        source.AddBoolean("noAdsWhenMagEmpty", flags.NoAdsWhenMagEmpty);
        source.AddBoolean("inheritsPerks", flags.InheritsPerks);
        source.AddBoolean("avoidDropCleanup", flags.AvoidDropCleanup);

        WeaponAimMovementTuningFields aim = definition.AimMovementTuning;
        source.AddFloat("autoAimRange", aim.AutoAimRange);
        source.AddFloat("aimAssistRange", aim.AimAssistRange);
        source.AddFloat("aimAssistRangeAds", aim.AimAssistRangeAds);
        source.AddFloat("aimPadding", aim.AimPadding);
        source.AddFloat("enemyCrosshairRange", aim.EnemyCrosshairRange);
        source.AddBoolean("crosshairColorChange", flags.CrosshairColorChange);
        source.AddFloat("moveSpeedScale", aim.MoveSpeedScale);
        source.AddFloat("adsMoveSpeedScale", aim.AdsMoveSpeedScale);
        source.AddFloat("sprintDurationScale", aim.SprintDurationScale);

        WeaponAdsViewAndSpreadFields ads = definition.AdsViewAndSpread;
        source.AddFloat("idleCrouchFactor", ads.IdleCrouchFactor);
        source.AddFloat("idleProneFactor", ads.IdleProneFactor);
        source.AddFloat("gunMaxPitch", ads.GunMaxPitch);
        source.AddFloat("gunMaxYaw", ads.GunMaxYaw);
        source.AddFloat("swayMaxAngle", ads.SwayMaxAngle);
        source.AddFloat("swayLerpSpeed", ads.SwayLerpSpeed);
        source.AddFloat("swayPitchScale", ads.SwayPitchScale);
        source.AddFloat("swayYawScale", ads.SwayYawScale);
        source.AddFloat("swayHorizScale", ads.SwayHorizontalScale);
        source.AddFloat("swayVertScale", ads.SwayVerticalScale);
        source.AddFloat("swayShellShockScale", ads.SwayShellShockScale);
        source.AddFloat("adsSwayMaxAngle", ads.AdsSwayMaxAngle);
        source.AddFloat("adsSwayLerpSpeed", ads.AdsSwayLerpSpeed);
        source.AddFloat("adsSwayPitchScale", ads.AdsSwayPitchScale);
        source.AddFloat("adsSwayYawScale", ads.AdsSwayYawScale);
        source.AddFloat("adsSwayHorizScale", ads.AdsSwayHorizontalScale);
        source.AddFloat("adsSwayVertScale", ads.AdsSwayVerticalScale);
        source.AddBoolean("rifleBullet", flags.RifleBullet);
        source.AddBoolean("armorPiercing", flags.ArmorPiercing);
        source.AddBoolean("boltAction", flags.BoltAction);
        source.AddBoolean("aimDownSight", flags.AimDownSight);
        source.AddBoolean("rechamberWhileAds", flags.RechamberWhileAds);
        source.AddBoolean("bBulletExplosiveDamage", flags.BulletExplosiveDamage);
        source.AddFloat("adsViewErrorMin", ads.AdsViewErrorMin);
        source.AddFloat("adsViewErrorMax", ads.AdsViewErrorMax);
        source.AddBoolean("clipOnly", flags.ClipOnly);
        source.AddBoolean("noAmmoPickup", flags.NoAmmoPickup);
        source.AddBoolean("cookOffHold", flags.CookOffHold);
        source.AddBoolean("adsFire", flags.AdsFireOnly);
        source.AddBoolean("cancelAutoHolsterWhenEmpty", flags.CancelAutoHolsterWhenEmpty);
        source.AddBoolean("disableSwitchToWhenEmpty", flags.DisableSwitchToWhenEmpty);
        source.AddBoolean("suppressAmmoReserveDisplay", flags.SuppressAmmoReserveDisplay);
        source.AddBoolean("enhanced", variant.Enhanced);
        source.AddBoolean("motionTracker", variant.MotionTracker);
        source.AddBoolean("laserSightDuringNightvision", flags.LaserSightDuringNightvision);
        source.AddBoolean("markableViewmodel", flags.MarkableViewmodel);
        source.AddString("physCollmap", ReferencedName(
            definition.PhysCollmapPointer.Raw,
            definition.PhysCollmap?.SerializedAssetName ?? definition.PhysCollmapName,
            $"Weapon '{assetName}' physics collision map"));
        source.AddBoolean("noDualWield", flags.NoDualWield);

        WeaponPhysicsFields physics = definition.Physics;
        source.AddFloat("dualWieldViewModelOffset", physics.DualWieldViewModelOffset);
        source.AddString("killIcon", Referenced(
            variant.KillIconPointer.Raw,
            variant.KillIcon,
            $"Weapon '{assetName}' kill icon"));
        source.AddEnum("killIconRatio", physics.KillIconRatio, IconRatioNames,
            $"Weapon '{assetName}' kill-icon ratio");
        source.AddBoolean("flipKillIcon", flags.FlipKillIcon);
        source.AddString("dpadIcon", Referenced(
            variant.DpadIconPointer.Raw,
            variant.DpadIcon,
            $"Weapon '{assetName}' d-pad icon"));
        source.AddEnum("dpadIconRatio", variant.DpadIconRatio, IconRatioNames,
            $"Weapon '{assetName}' d-pad icon ratio");
        source.AddBoolean("dpadIconShowsAmmo", variant.DpadIconShowsAmmo);
        source.AddBoolean("noPartialReload", flags.NoPartialReload);
        source.AddBoolean("segmentedReload", flags.SegmentedReload);
        source.AddInt("reloadAmmoAdd", physics.ReloadAmmoAdd);
        source.AddInt("reloadStartAdd", physics.ReloadStartAdd);
        source.AddString("altWeapon", Materialized(
            variant.AlternateWeaponNamePointer.Raw,
            variant.AlternateWeaponName,
            $"Weapon '{assetName}' alternate weapon"));
        source.AddInt("dropAmmoMin", physics.AmmoDropStockMin);
        source.AddInt("dropAmmoMax", variant.AmmoDropStockMax);
        // These PS3 integer cells are exposed as floats by the legacy asset
        // model, so preserve their serialized bits for the OAT integer fields.
        source.AddInt(
            "ammoDropClipPercentMin",
            BitConverter.SingleToInt32Bits(physics.AmmoDropClipPercentMin));
        source.AddInt(
            "ammoDropClipPercentMax",
            BitConverter.SingleToInt32Bits(physics.AmmoDropClipPercentMax));
        source.AddBoolean("blocksProne", flags.BlocksProne);
        source.AddBoolean("silenced", flags.Silenced);
        source.AddBoolean("isRollingGrenade", flags.IsRollingGrenade);
        source.AddInt("explosionRadius", physics.ExplosionRadius);
        source.AddInt("explosionRadiusMin", physics.ExplosionRadiusMin);
        source.AddInt("explosionInnerDamage", physics.ExplosionInnerDamage);
        source.AddInt("explosionOuterDamage", physics.ExplosionOuterDamage);
        source.AddFloat("damageConeAngle", physics.DamageConeAngle);
        source.AddFloat("bulletExplDmgMult", physics.BulletExplosionDamageMultiplier);
        source.AddFloat("bulletExplRadiusMult", physics.BulletExplosionRadiusMultiplier);
        source.AddInt("projectileSpeed", physics.ProjectileSpeed);
        source.AddInt("projectileSpeedUp", physics.ProjectileSpeedUp);
        source.AddInt("projectileSpeedForward", physics.ProjectileSpeedForward);
        source.AddInt("projectileActivateDist", physics.ProjectileActivateDistance);

        // These are numeric PS3 fields. They are converted semantically to the
        // OAT float source values; their integer storage is never reinterpreted.
        source.AddIntegerAsFloat(
            "projectileLifetime",
            physics.ProjectileLifetime,
            $"Weapon '{assetName}' projectile lifetime");
        source.AddIntegerAsFloat(
            "timeToAccelerate",
            physics.TimeToAccelerate,
            $"Weapon '{assetName}' projectile acceleration time");
        source.AddFloat("projectileCurvature", physics.ProjectileCurvature);

        WeaponProjectileFields projectile = definition.Projectile;
        source.AddString("projectileModel", Referenced(
            projectile.ModelPointer.Raw,
            projectile.Model,
            $"Weapon '{assetName}' projectile model"));
        source.AddEnum(
            "projExplosionType",
            (int)projectile.Explosion,
            ProjectileExplosionNames,
            $"Weapon '{assetName}' projectile explosion type");
        source.AddString("projExplosionEffect", Referenced(
            projectile.ExplosionEffectPointer.Raw,
            projectile.ExplosionEffect,
            $"Weapon '{assetName}' projectile explosion effect"));
        source.AddBoolean(
            "projExplosionEffectForceNormalUp",
            flags.ProjectileExplosionEffectForceNormalUp);
        source.AddString("projExplosionSound", Materialized(
            Presence(projectile.ExplosionSoundPointer.Raw, projectile.ExplosionSoundValuePointer.Raw),
            projectile.ExplosionSound,
            $"Weapon '{assetName}' projectile explosion sound"));
        source.AddString("projDudEffect", Referenced(
            projectile.DudEffectPointer.Raw,
            projectile.DudEffect,
            $"Weapon '{assetName}' projectile dud effect"));
        source.AddString("projDudSound", Materialized(
            Presence(projectile.DudSoundPointer.Raw, projectile.DudSoundValuePointer.Raw),
            projectile.DudSound,
            $"Weapon '{assetName}' projectile dud sound"));
        source.AddBoolean("projImpactExplode", flags.ProjectileImpactExplode);
        source.AddEnum(
            "stickiness",
            (int)projectile.Stickiness,
            StickinessNames,
            $"Weapon '{assetName}' projectile stickiness");
        source.AddBoolean("stickToPlayers", flags.StickToPlayers);
        source.AddBoolean("hasDetonator", flags.HasDetonator);
        source.AddBoolean("disableFiring", flags.DisableFiring);
        source.AddBoolean("timedDetonation", flags.TimedDetonation);
        source.AddBoolean("rotate", flags.Rotate);
        source.AddBoolean("holdButtonToThrow", flags.HoldButtonToThrow);
        source.AddBoolean("freezeMovementWhenFiring", flags.FreezeMovementWhenFiring);
        source.AddFloat("lowAmmoWarningThreshold", projectile.LowAmmoWarningThreshold);
        source.AddFloat("ricochetChance", projectile.RicochetChance);
        source.AddBoolean("offhandHoldIsCancelable", flags.OffhandHoldIsCancelable);
    }
}
