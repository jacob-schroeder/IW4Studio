using IW4.AssetExchange.SourceFormat.InfoString;
using IW4.Assets.Assets.Weapon;
using IW4.Assets.Math;

namespace IW4.AssetExchange.SourceFormat.Weapon;

internal static partial class WeaponInfoStringSource
{
    private static void AddEffectsSoundsAndMovement(
        InfoStringSourceWriter source,
        WeaponVariantDef variant,
        WeaponDef definition,
        string assetName)
    {
        source.AddString("viewFlashEffect", Referenced(
            definition.FlashEffects.ViewPointer.Raw,
            definition.FlashEffects.View,
            $"Weapon '{assetName}' view flash effect"));
        source.AddString("worldFlashEffect", Referenced(
            definition.FlashEffects.WorldPointer.Raw,
            definition.FlashEffects.World,
            $"Weapon '{assetName}' world flash effect"));

        foreach ((string key, WeaponPrimarySoundSlot slot) in PrimarySoundFields)
        {
            source.AddString(
                key,
                Sound(
                    definition.PrimarySounds.Get(slot),
                    $"Weapon '{assetName}' {key}"));
        }

        source.AddString("bounceSound", BounceSoundPrefix(definition, assetName));
        source.AddString("viewShellEjectEffect", Referenced(
            definition.ShellEjectEffects.ViewPointer.Raw,
            definition.ShellEjectEffects.View,
            $"Weapon '{assetName}' view shell-eject effect"));
        source.AddString("worldShellEjectEffect", Referenced(
            definition.ShellEjectEffects.WorldPointer.Raw,
            definition.ShellEjectEffects.World,
            $"Weapon '{assetName}' world shell-eject effect"));
        source.AddString("viewLastShotEjectEffect", Referenced(
            definition.ShellEjectEffects.ViewLastShotPointer.Raw,
            definition.ShellEjectEffects.ViewLastShot,
            $"Weapon '{assetName}' view last-shot eject effect"));
        source.AddString("worldLastShotEjectEffect", Referenced(
            definition.ShellEjectEffects.WorldLastShotPointer.Raw,
            definition.ShellEjectEffects.WorldLastShot,
            $"Weapon '{assetName}' world last-shot eject effect"));

        WeaponReticleFields reticle = definition.Reticle;
        source.AddString("reticleCenter", Referenced(
            reticle.CenterMaterialPointer.Raw,
            reticle.CenterMaterial,
            $"Weapon '{assetName}' center reticle material"));
        source.AddString("reticleSide", Referenced(
            reticle.SideMaterialPointer.Raw,
            reticle.SideMaterial,
            $"Weapon '{assetName}' side reticle material"));
        source.AddInt("reticleCenterSize", reticle.CenterSize);
        source.AddInt("reticleSideSize", reticle.SideSize);
        source.AddInt("reticleMinOfs", reticle.MinOffset);
        source.AddEnum(
            "activeReticleType",
            (int)reticle.ActiveType,
            ActiveReticleNames,
            $"Weapon '{assetName}' active reticle type");

        WeaponViewMovementFields view = definition.ViewMovement;
        AddVec3(source, view.StandMove, "standMoveF", "standMoveR", "standMoveU");
        AddVec3(source, view.StandRotation, "standRotP", "standRotY", "standRotR");
        AddVec3(source, view.StrafeMove, "strafeMoveF", "strafeMoveR", "strafeMoveU");
        AddVec3(source, view.StrafeRotation, "strafeRotP", "strafeRotY", "strafeRotR");
        AddVec3(source, view.DuckedOffset, "duckedOfsF", "duckedOfsR", "duckedOfsU");
        AddVec3(source, view.DuckedMove, "duckedMoveF", "duckedMoveR", "duckedMoveU");
        AddVec3(source, view.DuckedRotation, "duckedRotP", "duckedRotY", "duckedRotR");
        AddVec3(source, view.ProneOffset, "proneOfsF", "proneOfsR", "proneOfsU");
        AddVec3(source, view.ProneMove, "proneMoveF", "proneMoveR", "proneMoveU");
        AddVec3(source, view.ProneRotation, "proneRotP", "proneRotY", "proneRotR");

        WeaponPositionalMovementFields position = definition.PositionalMovement;
        source.AddFloat("posMoveRate", position.PositionMoveRate);
        source.AddFloat("posProneMoveRate", position.PositionProneMoveRate);
        source.AddFloat("standMoveMinSpeed", position.StandMoveMinSpeed);
        source.AddFloat("duckedMoveMinSpeed", position.DuckedMoveMinSpeed);
        source.AddFloat("proneMoveMinSpeed", position.ProneMoveMinSpeed);
        source.AddFloat("posRotRate", position.PositionRotationRate);
        source.AddFloat("posProneRotRate", position.PositionProneRotationRate);
        source.AddFloat("standRotMinSpeed", position.StandRotationMinSpeed);
        source.AddFloat("duckedRotMinSpeed", position.DuckedRotationMinSpeed);
        source.AddFloat("proneRotMinSpeed", position.ProneRotationMinSpeed);

        AddModelArray(
            source,
            "worldModel",
            definition.WorldGunModelsPointer.Raw,
            definition.WorldGunModelPointers,
            definition.WorldGunModels,
            WeaponDef.GunModelCount,
            $"Weapon '{assetName}' world models");
        source.AddString("worldClipModel", Referenced(
            definition.WorldClipModelPointer.Raw,
            definition.WorldClipModel,
            $"Weapon '{assetName}' world clip model"));
        source.AddString("rocketModel", Referenced(
            definition.RocketModelPointer.Raw,
            definition.RocketModel,
            $"Weapon '{assetName}' rocket model"));
        source.AddString("knifeModel", Referenced(
            definition.KnifeModelPointer.Raw,
            definition.KnifeModel,
            $"Weapon '{assetName}' knife model"));
        source.AddString("worldKnifeModel", Referenced(
            definition.WorldKnifeModelPointer.Raw,
            definition.WorldKnifeModel,
            $"Weapon '{assetName}' world knife model"));

        WeaponIconPointers icons = definition.Icons;
        source.AddString("hudIcon", Referenced(
            icons.HudIconPointer.Raw,
            icons.HudIcon,
            $"Weapon '{assetName}' HUD icon"));
        source.AddEnum("hudIconRatio", icons.HudIconRatio, IconRatioNames,
            $"Weapon '{assetName}' HUD icon ratio");
        source.AddString("pickupIcon", Referenced(
            icons.PickupIconPointer.Raw,
            icons.PickupIcon,
            $"Weapon '{assetName}' pickup icon"));
        source.AddEnum("pickupIconRatio", icons.PickupIconRatio, IconRatioNames,
            $"Weapon '{assetName}' pickup icon ratio");
        source.AddString("ammoCounterIcon", Referenced(
            icons.AmmoCounterIconPointer.Raw,
            icons.AmmoCounterIcon,
            $"Weapon '{assetName}' ammo-counter icon"));
        source.AddEnum(
            "ammoCounterIconRatio",
            icons.AmmoCounterIconRatio,
            IconRatioNames,
            $"Weapon '{assetName}' ammo-counter icon ratio");
        source.AddEnum(
            "ammoCounterClip",
            (int)icons.AmmoCounterClip,
            AmmoCounterClipNames,
            $"Weapon '{assetName}' ammo-counter clip type");
        source.AddInt("startAmmo", icons.StartAmmo);
        source.AddBoolean("shareAmmo", definition.TailFlags.SharedAmmo);

        WeaponAmmoFields ammo = definition.Ammo;
        source.AddString("ammoName", Materialized(
            ammo.AmmoNamePointer.Raw,
            ammo.AmmoName,
            $"Weapon '{assetName}' ammo name"));
        source.AddString("clipName", Materialized(
            ammo.ClipNamePointer.Raw,
            ammo.ClipName,
            $"Weapon '{assetName}' clip name"));
        source.AddInt("maxAmmo", ammo.MaxAmmo);
        source.AddInt("clipSize", variant.ClipSize);
        source.AddInt("shotCount", ammo.ShotCount);
        source.AddString("sharedAmmoCapName", Materialized(
            ammo.SharedAmmoCapNamePointer.Raw,
            ammo.SharedAmmoCapName,
            $"Weapon '{assetName}' shared-ammo-cap name"));
        source.AddInt("sharedAmmoCap", ammo.SharedAmmoCap);
    }

    private static void AddVec3(
        InfoStringSourceWriter source,
        Vec3 value,
        string xKey,
        string yKey,
        string zKey)
    {
        source.AddFloat(xKey, value.X);
        source.AddFloat(yKey, value.Y);
        source.AddFloat(zKey, value.Z);
    }

    private static string BounceSoundPrefix(
        WeaponDef definition,
        string assetName)
    {
        RequireList(
            definition.BounceSoundPointer.Raw,
            definition.BounceSounds.Count,
            SurfaceNames.Length,
            $"Weapon '{assetName}' bounce sounds");
        if (definition.BounceSounds.Count == 0)
            return string.Empty;

        var names = new string[BounceSoundSuffixes.Length];
        for (int index = 0; index < names.Length; index++)
        {
            names[index] = Sound(
                definition.BounceSounds[index],
                $"Weapon '{assetName}' bounce sound {index}");
        }

        string first = names[0];
        if (first.Length == 0)
        {
            if (names.Any(name => name.Length != 0))
            {
                throw new InvalidDataException(
                    $"Weapon '{assetName}' has bounce sounds without a default entry.");
            }

            return string.Empty;
        }

        const string defaultSuffix = "_default";
        if (!first.EndsWith(defaultSuffix, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Weapon '{assetName}' default bounce sound '{first}' does not end in '_default'.");
        }

        string prefix = first[..^defaultSuffix.Length];
        if (prefix.Length == 0)
        {
            throw new InvalidDataException(
                $"Weapon '{assetName}' has a bounce-sound table with an empty source prefix.");
        }

        for (int index = 0; index < names.Length; index++)
        {
            string expected = prefix + BounceSoundSuffixes[index];
            if (!string.Equals(names[index], expected, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Weapon '{assetName}' bounce sound {index} is '{names[index]}' instead of the source-derived name '{expected}'.");
            }
        }

        return prefix;
    }
}
