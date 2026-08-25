using IW4.AssetExchange.SourceFormat.InfoString;
using IW4.Assets.Assets.Vehicle;

namespace IW4.AssetExchange.SourceFormat.Vehicle;

/// <summary>Writes an IW4 vehicle in the native VEHICLEFILE info-string format.</summary>
public sealed class VehicleExchange
{
    private static readonly string[] VehicleTypeNames =
        ["4 wheel", "tank", "plane", "boat", "artillery", "helicopter", "snowmobile"];
    private static readonly string[] AxleTypeNames = ["front", "rear", "all"];

    public IReadOnlyList<string> Unlink(
        string sourceDirectory,
        VehicleDefAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string assetName = SourceOutput.NormalizeOwnedAssetName(
            asset.Name,
            "Vehicle");
        InfoStringSourceWriter source = CreateSource(asset, assetName);
        return new SourceOutput(sourceDirectory).WriteTextBatch([
            ($"vehicles/{assetName}", source.Write)
        ]);
    }

    private static InfoStringSourceWriter CreateSource(
        VehicleDefAsset asset,
        string assetName)
    {
        var source = new InfoStringSourceWriter("VEHICLEFILE");
        VehicleFakeBodyTuning fakeBody = asset.FakeBody;
        VehiclePhysDef physics = asset.Phys;
        VehicleEngineSoundFields engine = asset.EngineSounds;
        VehicleSuspensionSoundFields suspension = asset.SuspensionSounds;

        source.AddEnum("type", (int)asset.Type, VehicleTypeNames,
            $"Vehicle '{assetName}' type");
        source.AddString("useHintString", Materialized(
            asset.UseHintStringPointer.Raw, asset.UseHintString,
            $"Vehicle '{assetName}' use hint"));
        source.AddInt("health", asset.Health);
        source.AddBoolean("quadBarrel", asset.QuadBarrel);
        source.AddFloat("textureScrollScale", asset.TexScrollScale);
        source.AddMilesPerHour("topSpeed", asset.TopSpeed);
        source.AddMilesPerHour("accel", asset.Accel);
        source.AddFloat("rotRate", asset.RotRate);
        source.AddFloat("rotAccel", asset.RotAccel);
        source.AddFloat("maxBodyPitch", asset.MaxBodyPitch);
        source.AddFloat("maxBodyRoll", asset.MaxBodyRoll);
        source.AddFloat("fakeBodyAccelPitch", fakeBody.AccelPitch);
        source.AddFloat("fakeBodyAccelRoll", fakeBody.AccelRoll);
        source.AddFloat("fakeBodyVelPitch", fakeBody.VelPitch);
        source.AddFloat("fakeBodyVelRoll", fakeBody.VelRoll);
        source.AddFloat("fakeBodySideVelPitch", fakeBody.SideVelPitch);
        source.AddFloat("fakeBodyPitchStrength", fakeBody.PitchStrength);
        source.AddFloat("fakeBodyRollStrength", fakeBody.RollStrength);
        source.AddFloat("fakeBodyPitchDampening", fakeBody.PitchDampening);
        source.AddFloat("fakeBodyRollDampening", fakeBody.RollDampening);
        source.AddFloat("fakeBodyBoatRockingAmplitude", fakeBody.BoatRockingAmplitude);
        source.AddFloat("fakeBodyBoatRockingPeriod", fakeBody.BoatRockingPeriod);
        source.AddFloat("fakeBodyBoatRockingRotationPeriod", fakeBody.BoatRockingRotationPeriod);
        source.AddMilesPerHour("fakeBodyBoatRockingFadeoutSpeed", fakeBody.BoatRockingFadeoutSpeed);
        source.AddMilesPerHour("boatBouncingMinForce", fakeBody.BoatBouncingMinForce);
        source.AddMilesPerHour("boatBouncingMaxForce", fakeBody.BoatBouncingMaxForce);
        source.AddFloat("boatBouncingRate", fakeBody.BoatBouncingRate);
        source.AddMilesPerHour("boatBouncingFadeinSpeed", fakeBody.BoatBouncingFadeinSpeed);
        source.AddFloat("boatBouncingFadeoutSteeringAngle", fakeBody.BoatBouncingFadeoutSteeringAngle);
        source.AddFloat("collisionDamage", asset.CollisionDamage);
        source.AddMilesPerHour("collisionSpeed", asset.CollisionSpeed);
        source.AddFloat("killcamZDist", asset.KillcamOffset.X);
        source.AddFloat("killcamBackDist", asset.KillcamOffset.Y);
        source.AddFloat("killcamUpDist", asset.KillcamOffset.Z);
        source.AddBoolean("playerProtected", asset.PlayerProtected);
        source.AddBoolean("bulletDamage", asset.BulletDamage);
        source.AddBoolean("armorPiercingDamage", asset.ArmorPiercingDamage);
        source.AddBoolean("grenadeDamage", asset.GrenadeDamage);
        source.AddBoolean("projectileDamage", asset.ProjectileDamage);
        source.AddBoolean("projectileSplashDamage", asset.ProjectileSplashDamage);
        source.AddBoolean("heavyExplosiveDamage", asset.HeavyExplosiveDamage);
        source.AddBoolean("physicsEnabled", physics.PhysicsEnabled);
        source.AddString("physicsPreset", Materialized(
            physics.PhysPresetNamePointer.Raw, physics.PhysPresetName,
            $"Vehicle '{assetName}' physics preset name"));
        source.AddString("accelerationGraph", Materialized(
            physics.AccelGraphNamePointer.Raw, physics.AccelGraphName,
            $"Vehicle '{assetName}' acceleration graph name"));
        source.AddEnum("steeringAxle", (int)physics.SteeringAxle, AxleTypeNames,
            $"Vehicle '{assetName}' steering axle");
        source.AddEnum("powerAxle", (int)physics.PowerAxle, AxleTypeNames,
            $"Vehicle '{assetName}' power axle");
        source.AddEnum("brakingAxle", (int)physics.BrakingAxle, AxleTypeNames,
            $"Vehicle '{assetName}' braking axle");
        source.AddMilesPerHour("reverseSpeed", physics.ReverseSpeed);
        source.AddMilesPerHour("maxVelocity", physics.MaxVelocity);
        source.AddFloat("maxPitch", physics.MaxPitch);
        source.AddFloat("maxRoll", physics.MaxRoll);
        source.AddFloat("suspensionTravelRear", physics.SuspensionTravelRear);
        source.AddFloat("suspensionStrengthFront", physics.SuspensionStrengthFront);
        source.AddFloat("suspensionDampingFront", physics.SuspensionDampingFront);
        source.AddFloat("suspensionStrengthRear", physics.SuspensionStrengthRear);
        source.AddFloat("suspensionDampingRear", physics.SuspensionDampingRear);
        source.AddFloat("frictionBraking", physics.FrictionBraking);
        source.AddFloat("frictionCoasting", physics.FrictionCoasting);
        source.AddFloat("frictionTopSpeed", physics.FrictionTopSpeed);
        source.AddFloat("frictionSide", physics.FrictionSide);
        source.AddFloat("frictionSideRear", physics.FrictionSideRear);
        source.AddFloat("velocityDependentSlip", physics.VelocityDependentSlip);
        source.AddFloat("rollStability", physics.RollStability);
        source.AddMilesPerHour("rollResistance", physics.RollResistance);
        source.AddMilesPerHour("pitchResistance", physics.PitchResistance);
        source.AddMilesPerHour("yawResistance", physics.YawResistance);
        source.AddMilesPerHour("uprightStrengthPitch", physics.UprightStrengthPitch);
        source.AddMilesPerHour("uprightStrengthRoll", physics.UprightStrengthRoll);
        source.AddFloat("targetAirPitch", physics.TargetAirPitch);
        source.AddMilesPerHour("airYawTorque", physics.AirYawTorque);
        source.AddMilesPerHour("airPitchTorque", physics.AirPitchTorque);
        source.AddMilesPerHour("minimumMomentumForCollision", physics.MinimumMomentumForCollision);
        source.AddFloat("collisionLaunchForceScale", physics.CollisionLaunchForceScale);
        source.AddFloat("wreckedMassScale", physics.WreckedMassScale);
        source.AddFloat("wreckedBodyFriction", physics.WreckedBodyFriction);
        source.AddMilesPerHour("minimumJoltForNotify", physics.MinimumJoltForNotify);
        source.AddFloat("slipThresholdFront", physics.SlipThresholdFront);
        source.AddFloat("slipThresholdRear", physics.SlipThresholdRear);
        source.AddFloat("slipFricScaleFront", physics.SlipFricScaleFront);
        source.AddFloat("slipFricScaleRear", physics.SlipFricScaleRear);
        source.AddFloat("slipFricRateFront", physics.SlipFricRateFront);
        source.AddFloat("slipFricRateRear", physics.SlipFricRateRear);
        source.AddMilesPerHour("slipYawTorque", physics.SlipYawTorque);
        source.AddFloat("boostDuration", asset.BoostDuration);
        source.AddFloat("boostRechargeTime", asset.BoostRechargeTime);
        source.AddMilesPerHour("boostAcceleration", asset.BoostAcceleration);
        source.AddFloat("suspensionTravel", asset.SuspensionTravel);
        source.AddFloat("maxSteeringAngle", asset.MaxSteeringAngle);
        source.AddFloat("steeringLerp", asset.SteeringLerp);
        source.AddFloat("minSteeringScale", asset.MinSteeringScale);
        source.AddMilesPerHour("minSteeringSpeed", asset.MinSteeringSpeed);
        source.AddBoolean("camLookEnabled", asset.CamLookEnabled);
        source.AddFloat("camLerp", asset.CamLerp);
        source.AddFloat("camPitchInfluence", asset.CamPitchInfluence);
        source.AddFloat("camRollInfluence", asset.CamRollInfluence);
        source.AddFloat("camFovIncrease", asset.CamFovIncrease);
        source.AddFloat("camFovOffset", asset.CamFovOffset);
        source.AddFloat("camFovSpeed", asset.CamFovSpeed);
        source.AddString("turretWeaponName", Materialized(
            asset.TurretWeaponNamePointer.Raw, asset.TurretWeaponName,
            $"Vehicle '{assetName}' turret weapon name"));
        source.AddFloat("turretHorizSpanLeft", asset.TurretHorizSpanLeft);
        source.AddFloat("turretHorizSpanRight", asset.TurretHorizSpanRight);
        source.AddFloat("turretVertSpanUp", asset.TurretVertSpanUp);
        source.AddFloat("turretVertSpanDown", asset.TurretVertSpanDown);
        source.AddFloat("turretRotRate", asset.TurretRotRate);
        source.AddString("turretSpinSnd", SoundName(asset.TurretSpinSound,
            $"Vehicle '{assetName}' turret spin sound"));
        source.AddString("turretStopSnd", SoundName(asset.TurretStopSound,
            $"Vehicle '{assetName}' turret stop sound"));
        source.AddBoolean("trophyEnabled", asset.TrophyEnabled);
        source.AddFloat("trophyRadius", asset.TrophyRadius);
        source.AddFloat("trophyInactiveRadius", asset.TrophyInactiveRadius);
        source.AddInt("trophyAmmoCount", asset.TrophyAmmoCount);
        source.AddFloat("trophyReloadTime", asset.TrophyReloadTime);
        source.AddString("trophyTags", TrophyTags(asset, assetName));
        source.AddString("compassFriendlyIcon", Referenced(
            asset.CompassFriendlyIconPointer.Raw,
            asset.CompassFriendlyIcon?.SerializedAssetName,
            $"Vehicle '{assetName}' friendly compass icon"));
        source.AddString("compassEnemyIcon", Referenced(
            asset.CompassEnemyIconPointer.Raw,
            asset.CompassEnemyIcon?.SerializedAssetName,
            $"Vehicle '{assetName}' enemy compass icon"));
        source.AddInt("compassIconWidth", asset.CompassIconWidth);
        source.AddInt("compassIconHeight", asset.CompassIconHeight);
        source.AddString("lowIdleSnd", SoundName(engine.IdleLowSound,
            $"Vehicle '{assetName}' low idle sound"));
        source.AddString("highIdleSnd", SoundName(engine.IdleHighSound,
            $"Vehicle '{assetName}' high idle sound"));
        source.AddString("lowEngineSnd", SoundName(engine.EngineLowSound,
            $"Vehicle '{assetName}' low engine sound"));
        source.AddString("highEngineSnd", SoundName(engine.EngineHighSound,
            $"Vehicle '{assetName}' high engine sound"));
        source.AddMilesPerHour("engineSndSpeed", engine.EngineSoundSpeed);
        source.AddString("engineStartUpSnd", SoundName(engine.EngineStartUpSound,
            $"Vehicle '{assetName}' engine startup sound"));
        source.AddMilliseconds("engineStartUpLength", engine.EngineStartUpLength);
        source.AddString("engineShutdownSnd", SoundName(engine.EngineShutdownSound,
            $"Vehicle '{assetName}' engine shutdown sound"));
        source.AddString("engineIdleSnd", SoundName(engine.EngineIdleSound,
            $"Vehicle '{assetName}' engine idle sound"));
        source.AddString("engineSustainSnd", SoundName(engine.EngineSustainSound,
            $"Vehicle '{assetName}' engine sustain sound"));
        source.AddString("engineRampUpSnd", SoundName(engine.EngineRampUpSound,
            $"Vehicle '{assetName}' engine ramp-up sound"));
        source.AddMilliseconds("engineRampUpLength", engine.EngineRampUpLength);
        source.AddString("engineRampDownSnd", SoundName(engine.EngineRampDownSound,
            $"Vehicle '{assetName}' engine ramp-down sound"));
        source.AddMilliseconds("engineRampDownLength", engine.EngineRampDownLength);
        source.AddString("suspensionSoftSnd", SoundName(suspension.SuspensionSoftSound,
            $"Vehicle '{assetName}' soft suspension sound"));
        source.AddFloat("suspensionSoftCompression", suspension.SuspensionSoftCompression);
        source.AddString("suspensionHardSnd", SoundName(suspension.SuspensionHardSound,
            $"Vehicle '{assetName}' hard suspension sound"));
        source.AddFloat("suspensionHardCompression", suspension.SuspensionHardCompression);
        source.AddString("collisionSnd", SoundName(asset.CollisionSound,
            $"Vehicle '{assetName}' collision sound"));
        source.AddMilesPerHour("collisionBlendSpeed", asset.CollisionBlendSpeed);
        source.AddString("speedSnd", SoundName(asset.SpeedSound,
            $"Vehicle '{assetName}' speed sound"));
        source.AddMilesPerHour("speedSndBlendSpeed", asset.SpeedSoundBlendSpeed);
        source.AddString("surfaceSndPrefix", Materialized(
            asset.SurfaceSoundPrefixPointer.Raw, asset.SurfaceSoundPrefix,
            $"Vehicle '{assetName}' surface sound prefix"));
        source.AddMilesPerHour("surfaceSndBlendSpeed", asset.SurfaceSoundBlendSpeed);
        source.AddFloat("slideVolume", asset.SlideVolume);
        source.AddMilesPerHour("slideBlendSpeed", asset.SlideBlendSpeed);
        source.AddFloat("inAirPitch", asset.InAirPitch);
        return source;
    }

    private static string TrophyTags(VehicleDefAsset asset, string assetName)
    {
        if (asset.TrophyTags.Count != VehicleDefAsset.ScriptStringCount)
        {
            throw new InvalidDataException(
                $"Vehicle '{assetName}' requires {VehicleDefAsset.ScriptStringCount} materialized trophy tags but has {asset.TrophyTags.Count}.");
        }

        return string.Join("\n", asset.TrophyTags
            .Select((tag, index) => InfoStringSourceWriter.ScriptStringText(
                tag,
                $"Vehicle '{assetName}' trophy tag {index}"))
            .Where(value => value.Length != 0));
    }

    private static string SoundName(VehicleSoundAliasField sound, string field) =>
        Materialized(Presence(sound.Pointer.Raw, sound.ValuePointer.Raw),
            sound.Value, field);

    private static int Presence(int first, int second) =>
        first != 0 || second != 0 ? 1 : 0;

    private static string Materialized(int pointerRaw, string? value, string field) =>
        InfoStringSourceWriter.MaterializedString(pointerRaw, value, field);

    private static string Referenced(int pointerRaw, string? value, string field) =>
        InfoStringSourceWriter.ReferencedAssetName(pointerRaw, value, field);
}
