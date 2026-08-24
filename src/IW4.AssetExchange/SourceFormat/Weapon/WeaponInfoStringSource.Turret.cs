using IW4.AssetExchange.SourceFormat.InfoString;
using IW4.Assets.Assets.Weapon;

namespace IW4.AssetExchange.SourceFormat.Weapon;

internal static partial class WeaponInfoStringSource
{
    private static void AddAccuracyAndTurret(
        InfoStringSourceWriter source,
        WeaponVariantDef variant,
        WeaponDef definition,
        string assetName)
    {
        RequireList(
            definition.LocationDamageMultipliersPointer.Raw,
            definition.LocationDamageMultipliers.Count,
            (int)HitLocation.Count,
            $"Weapon '{assetName}' location-damage multipliers");
        if (definition.LocationDamageMultipliers.Count != 0)
        {
            float shieldMultiplier = definition.LocationDamageMultipliers[
                (int)HitLocation.Shield];
            if (!float.IsFinite(shieldMultiplier) || shieldMultiplier != 0.0f)
            {
                throw new InvalidDataException(
                    $"Weapon '{assetName}' has shield location-damage multiplier {shieldMultiplier}, which the IW4 source format does not expose.");
            }
        }

        foreach ((string key, HitLocation location) in HitLocationFields)
        {
            source.AddFloat(
                key,
                definition.LocationDamageMultipliers.Count == 0
                    ? 0.0f
                    : definition.LocationDamageMultipliers[(int)location]);
        }

        source.AddString("fireRumble", Materialized(
            definition.Rumble.FireRumblePointer.Raw,
            definition.Rumble.FireRumble,
            $"Weapon '{assetName}' fire rumble"));
        source.AddString("meleeImpactRumble", Materialized(
            definition.Rumble.MeleeImpactRumblePointer.Raw,
            definition.Rumble.MeleeImpactRumble,
            $"Weapon '{assetName}' melee-impact rumble"));
        source.AddString("tracerType", Referenced(
            definition.TracerPointer.Raw,
            definition.Tracer,
            $"Weapon '{assetName}' tracer"));
        source.AddFloat("adsDofStart", variant.AdsDofStart);
        source.AddFloat("adsDofEnd", variant.AdsDofEnd);
        source.AddFloat("turretScopeZoomRate", definition.TurretScopeZoomRate);
        source.AddFloat("turretScopeZoomMin", definition.TurretScopeZoomMin);
        source.AddFloat("turretScopeZoomMax", definition.TurretScopeZoomMax);

        WeaponTailFlags flags = definition.TailFlags;
        source.AddBoolean("thermalScope", flags.ThermalScope);
        source.AddBoolean("altModeSameWeapon", flags.AltModeSameWeapon);
        source.AddFloat("turretOverheatUpRate", definition.TurretOverheatUpRate);
        source.AddFloat("turretOverheatDownRate", definition.TurretOverheatDownRate);
        source.AddFloat("turretOverheatPenalty", definition.TurretOverheatPenalty);

        WeaponTurretFields turret = definition.Turret;
        source.AddString("turretOverheatSound", Materialized(
            Presence(turret.OverheatSoundPointer.Raw, turret.OverheatSoundValuePointer.Raw),
            turret.OverheatSound,
            $"Weapon '{assetName}' turret overheat sound"));
        source.AddString("turretOverheatEffect", Referenced(
            turret.OverheatEffectPointer.Raw,
            turret.OverheatEffect,
            $"Weapon '{assetName}' turret overheat effect"));
        source.AddBoolean("turretBarrelSpinEnabled", flags.TurretBarrelSpinEnabled);
        source.AddFloat("turretBarrelSpinUpTime", turret.BarrelSpinUpTime);
        source.AddFloat("turretBarrelSpinDownTime", turret.BarrelSpinDownTime);
        source.AddString("turretBarrelSpinRumble", Materialized(
            turret.BarrelSpinRumblePointer.Raw,
            turret.BarrelSpinRumble,
            $"Weapon '{assetName}' turret barrel-spin rumble"));
        source.AddFloat("turretBarrelSpinSpeed", turret.BarrelSpinSpeed);
        source.AddString("turretBarrelSpinMaxSnd", Materialized(
            Presence(turret.BarrelSpinMaxSoundPointer.Raw, turret.BarrelSpinMaxSoundValuePointer.Raw),
            turret.BarrelSpinMaxSound,
            $"Weapon '{assetName}' maximum barrel-spin sound"));
        AddTurretSoundArray(
            source,
            "turretBarrelSpinUpSnd",
            turret.BarrelSpinUpSounds,
            $"Weapon '{assetName}' barrel-spin-up sounds");
        AddTurretSoundArray(
            source,
            "turretBarrelSpinDownSnd",
            turret.BarrelSpinDownSounds,
            $"Weapon '{assetName}' barrel-spin-down sounds");

        WeaponMissileConeSoundFields missile = definition.MissileConeSound;
        source.AddBoolean("missileConeSoundEnabled", flags.MissileConeSoundEnabled);
        source.AddString("missileConeSoundAlias", Materialized(
            Presence(missile.AliasPointer.Raw, missile.AliasValuePointer.Raw),
            missile.Alias,
            $"Weapon '{assetName}' missile-cone sound"));
        source.AddString("missileConeSoundAliasAtBase", Materialized(
            Presence(missile.AliasAtBasePointer.Raw, missile.AliasAtBaseValuePointer.Raw),
            missile.AliasAtBase,
            $"Weapon '{assetName}' base missile-cone sound"));
        source.AddFloat("missileConeSoundRadiusAtTop", missile.RadiusAtTop);
        source.AddFloat("missileConeSoundRadiusAtBase", missile.RadiusAtBase);
        source.AddFloat("missileConeSoundHeight", missile.Height);
        source.AddFloat("missileConeSoundOriginOffset", missile.OriginOffset);
        source.AddFloat("missileConeSoundVolumescaleAtCore", missile.VolumeScaleAtCore);
        source.AddFloat("missileConeSoundVolumescaleAtEdge", missile.VolumeScaleAtEdge);
        source.AddFloat("missileConeSoundVolumescaleCoreSize", missile.VolumeScaleCoreSize);
        source.AddBoolean(
            "missileConeSoundPitchshiftEnabled",
            flags.MissileConeSoundPitchShiftEnabled);
        source.AddFloat("missileConeSoundPitchAtTop", missile.PitchAtTop);
        source.AddFloat("missileConeSoundPitchAtBottom", missile.PitchAtBottom);
        source.AddFloat("missileConeSoundPitchTopSize", missile.PitchTopSize);
        source.AddFloat("missileConeSoundPitchBottomSize", missile.PitchBottomSize);
        source.AddBoolean(
            "missileConeSoundCrossfadeEnabled",
            flags.MissileConeSoundCrossfadeEnabled);
        source.AddFloat("missileConeSoundCrossfadeTopSize", missile.CrossfadeTopSize);
        source.AddFloat("missileConeSoundCrossfadeBottomSize", missile.CrossfadeBottomSize);
    }

    private static void AddTurretSoundArray(
        InfoStringSourceWriter source,
        string keyPrefix,
        IReadOnlyList<WeaponSoundAliasField> sounds,
        string field)
    {
        int count = (int)WeaponTurretBarrelSpinSoundSlot.Count;
        if (sounds.Count != count)
        {
            throw new InvalidDataException(
                $"{field} requires {count} materialized values but has {sounds.Count}.");
        }

        for (int index = 0; index < count; index++)
        {
            source.AddString(
                $"{keyPrefix}{index + 1}",
                Sound(sounds[index], $"{field} entry {index}"));
        }
    }
}
