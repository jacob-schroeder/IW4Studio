using IW4.Formats.SourceFormat.Weapon;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.Weapon;
using IW4.Game.Assets.XModel;
using IW4.Game.ScriptStrings;
using IW4.Game.Zone;
using IW4.Runtime.Assets;

namespace IW4.Studio.Documents;

internal static partial class WeaponSourceImport
{
    private static bool TryApplyCollectionField(
        WeaponDraft draft, string key, string value, XAssetPool pool)
    {
        if (key == "hideTags")
        {
            ApplyHideTags(draft, value);
            return true;
        }
        if (key is "notetrackSoundMap" or "notetrackRumbleMap")
        {
            ApplyNoteTrackMap(draft, key, value);
            return true;
        }
        if (key == "bounceSound")
        {
            ApplyBounceSound(draft, value);
            return true;
        }
        if (TryModelIndex(key, "gunModel", out int modelIndex))
        {
            RequireMaterialized(draft.Definition!.GunModels.Count,
                WeaponDef.GunModelCount, key);
            draft.SetDefinitionGunModels(modelIndex,
                ResolveAsset<XModelAsset>(value, key, pool, XAssetType.XModel));
            return true;
        }
        if (TryModelIndex(key, "worldModel", out modelIndex))
        {
            RequireMaterialized(draft.Definition!.WorldGunModels.Count,
                WeaponDef.GunModelCount, key);
            draft.SetDefinitionWorldGunModels(modelIndex,
                ResolveAsset<XModelAsset>(value, key, pool, XAssetType.XModel));
            return true;
        }
        if (TryAnimation(draft, key, value))
            return true;
        if (WeaponExchange.TryGetPrimarySoundSlot(key, out WeaponPrimarySoundSlot soundSlot))
        {
            ApplyPrimarySound(draft, soundSlot, NullableText(value));
            return true;
        }
        if (TryTurretSound(draft, key, value))
            return true;
        if (WeaponExchange.TryGetHitLocation(key, out HitLocation location))
        {
            RequireMaterialized(draft.Definition!.LocationDamageMultipliers.Count,
                (int)HitLocation.Count, key);
            draft.SetDefinitionLocationDamageMultipliers((int)location,
                ParseFloat(value, key));
            return true;
        }
        if (TryBounceValue(draft, key, value))
            return true;
        return false;
    }

    private static bool TryModelIndex(string key, string prefix, out int index)
    {
        index = -1;
        if (key == prefix)
        {
            index = 0;
            return true;
        }
        if (!key.StartsWith(prefix, StringComparison.Ordinal) ||
            !int.TryParse(key.AsSpan(prefix.Length), out int oneBased) ||
            oneBased < 2 || oneBased > WeaponDef.GunModelCount)
            return false;
        index = oneBased - 1;
        return true;
    }

    private static bool TryAnimation(WeaponDraft draft, string key, string value)
    {
        if (WeaponExchange.TryGetAnimationSlot(key, out WeaponAnimationSlot slot))
        {
            RequireMaterialized(draft.Variant.AnimationNames.Count,
                (int)WeaponAnimationSlot.Count, key);
            draft.SetVariantAnimationNames((int)slot, NullableText(value));
            return true;
        }
        if (key.Length <= 1 || key[^1] is not ('R' or 'L') ||
            !WeaponExchange.TryGetAnimationSlot(key[..^1], out slot))
            return false;
        if (key[^1] == 'R')
        {
            RequireMaterialized(draft.Definition!.RightHandAnimationNames.Count,
                (int)WeaponAnimationSlot.Count, key);
            draft.SetDefinitionRightHandAnimationNames((int)slot, NullableText(value));
        }
        else
        {
            RequireMaterialized(draft.Definition!.LeftHandAnimationNames.Count,
                (int)WeaponAnimationSlot.Count, key);
            draft.SetDefinitionLeftHandAnimationNames((int)slot, NullableText(value));
        }
        return true;
    }

    private static bool TryTurretSound(WeaponDraft draft, string key, string value)
    {
        const string up = "turretBarrelSpinUpSnd";
        const string down = "turretBarrelSpinDownSnd";
        bool isUp = key.StartsWith(up, StringComparison.Ordinal);
        string prefix = isUp ? up : down;
        if (!key.StartsWith(prefix, StringComparison.Ordinal) ||
            !int.TryParse(key.AsSpan(prefix.Length), out int oneBased) ||
            oneBased < 1 || oneBased > (int)WeaponTurretBarrelSpinSoundSlot.Count)
            return false;
        IReadOnlyList<WeaponSoundAliasField> sounds = isUp
            ? draft.Definition!.Turret.BarrelSpinUpSounds
            : draft.Definition!.Turret.BarrelSpinDownSounds;
        RequireMaterialized(sounds.Count,
            (int)WeaponTurretBarrelSpinSoundSlot.Count, key);
        int index = oneBased - 1;
        WeaponSoundAliasField field = WithSoundName(sounds[index], NullableText(value));
        if (isUp)
            draft.SetDefinitionTurretBarrelSpinUpSounds(index, field);
        else
            draft.SetDefinitionTurretBarrelSpinDownSounds(index, field);
        return true;
    }

    private static bool TryBounceValue(WeaponDraft draft, string key, string value)
    {
        bool parallel = key.StartsWith("parallel", StringComparison.Ordinal);
        string prefix = parallel ? "parallel" : "perpendicular";
        if (!key.StartsWith(prefix, StringComparison.Ordinal) ||
            !key.EndsWith("Bounce", StringComparison.Ordinal) ||
            !WeaponExchange.TryGetSurfaceIndex(
                key[prefix.Length..(key.Length - "Bounce".Length)], out int index))
            return false;
        IReadOnlyList<float> values = parallel
            ? draft.Definition!.Projectile.ParallelBounce
            : draft.Definition!.Projectile.PerpendicularBounce;
        RequireMaterialized(values.Count, (int)MaterialSurfaceType.Count, key);
        float parsed = ParseFloat(value, key);
        if (parallel)
            draft.SetDefinitionProjectileParallelBounce(index, parsed);
        else
            draft.SetDefinitionProjectilePerpendicularBounce(index, parsed);
        return true;
    }

    private static void ApplyBounceSound(WeaponDraft draft, string prefix)
    {
        IReadOnlyList<WeaponSoundAliasField> sounds = draft.Definition!.BounceSounds;
        RequireMaterialized(sounds.Count, (int)MaterialSurfaceType.Count,
            "bounceSound");
        for (int index = 0; index < sounds.Count; index++)
        {
            string? name = prefix.Length == 0 ? null :
                prefix + WeaponExchange.GetBounceSoundSuffix(index);
            draft.SetDefinitionBounceSounds(index, WithSoundName(sounds[index], name));
        }
    }

    private static void ApplyHideTags(WeaponDraft draft, string value)
    {
        IReadOnlyList<ScriptStringReference> tags = draft.Variant.HideTags;
        RequireMaterialized(tags.Count, WeaponVariantDef.HideTagCount, "hideTags");
        string[] lines = SplitLines(value, WeaponVariantDef.HideTagCount, "hideTags");
        for (int index = 0; index < tags.Count; index++)
            draft.SetVariantHideTags(index, ScriptString(
                index < lines.Length ? lines[index] : null));
    }

    private static void ApplyNoteTrackMap(WeaponDraft draft, string key, string value)
    {
        bool sound = key == "notetrackSoundMap";
        IReadOnlyList<WeaponNoteTrackMapEntry> rows = sound
            ? draft.Definition!.NoteTrackMaps.SoundMappings
            : draft.Definition!.NoteTrackMaps.RumbleMappings;
        RequireMaterialized(rows.Count, WeaponDef.NoteTrackMapCount, key);
        string[] lines = SplitLines(value, WeaponDef.NoteTrackMapCount, key);
        for (int index = 0; index < rows.Count; index++)
        {
            string? left = null;
            string? right = null;
            if (index < lines.Length)
            {
                string[] pair = lines[index].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (pair.Length != 2 || pair.Any(part => part.Any(char.IsWhiteSpace)))
                    throw new InvalidDataException($"Weapon field '{key}' row {index + 1} requires a key and value.");
                left = pair[0];
                right = pair[1];
            }
            var entry = new WeaponNoteTrackMapEntry
            {
                Key = ScriptString(left),
                Value = ScriptString(right)
            };
            if (sound)
                draft.SetDefinitionNoteTrackMapsSoundMappings(index, entry);
            else
                draft.SetDefinitionNoteTrackMapsRumbleMappings(index, entry);
        }
    }

    private static string[] SplitLines(string value, int maximum, string key)
    {
        if (value.Length == 0)
            return [];
        string[] lines = value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length > maximum || lines.Any(line => line.Length == 0))
            throw new InvalidDataException($"Weapon field '{key}' has invalid or too many rows.");
        return lines;
    }

    private static ScriptStringReference ScriptString(string? text) =>
        new(0, text, ScriptStringHandle.Null, default);

    private static WeaponSoundAliasField WithSoundName(
        WeaponSoundAliasField current, string? name) => new()
    {
        Pointer = current.Pointer,
        ValuePointer = current.ValuePointer,
        Name = name
    };

    private static void RequireMaterialized(int count, int expected, string key)
    {
        if (count != expected)
            throw new InvalidDataException(
                $"Weapon field '{key}' cannot populate an absent native table.");
    }

    private static void ApplyPrimarySound(
        WeaponDraft draft, WeaponPrimarySoundSlot slot, string? value)
    {
        switch (slot)
        {
            case WeaponPrimarySoundSlot.PickupSound:
                draft.SetDefinitionPrimarySoundsPickupSoundName(value);
                break;
            case WeaponPrimarySoundSlot.PickupSoundPlayer:
                draft.SetDefinitionPrimarySoundsPickupSoundPlayerName(value);
                break;
            case WeaponPrimarySoundSlot.AmmoPickupSound:
                draft.SetDefinitionPrimarySoundsAmmoPickupSoundName(value);
                break;
            case WeaponPrimarySoundSlot.AmmoPickupSoundPlayer:
                draft.SetDefinitionPrimarySoundsAmmoPickupSoundPlayerName(value);
                break;
            case WeaponPrimarySoundSlot.ProjectileSound:
                draft.SetDefinitionPrimarySoundsProjectileSoundName(value);
                break;
            case WeaponPrimarySoundSlot.PullbackSound:
                draft.SetDefinitionPrimarySoundsPullbackSoundName(value);
                break;
            case WeaponPrimarySoundSlot.PullbackSoundPlayer:
                draft.SetDefinitionPrimarySoundsPullbackSoundPlayerName(value);
                break;
            case WeaponPrimarySoundSlot.FireSound:
                draft.SetDefinitionPrimarySoundsFireSoundName(value);
                break;
            case WeaponPrimarySoundSlot.FireSoundPlayer:
                draft.SetDefinitionPrimarySoundsFireSoundPlayerName(value);
                break;
            case WeaponPrimarySoundSlot.FireSoundPlayerAkimbo:
                draft.SetDefinitionPrimarySoundsFireSoundPlayerAkimboName(value);
                break;
            case WeaponPrimarySoundSlot.FireLoopSound:
                draft.SetDefinitionPrimarySoundsFireLoopSoundName(value);
                break;
            case WeaponPrimarySoundSlot.FireLoopSoundPlayer:
                draft.SetDefinitionPrimarySoundsFireLoopSoundPlayerName(value);
                break;
            case WeaponPrimarySoundSlot.FireStopSound:
                draft.SetDefinitionPrimarySoundsFireStopSoundName(value);
                break;
            case WeaponPrimarySoundSlot.FireStopSoundPlayer:
                draft.SetDefinitionPrimarySoundsFireStopSoundPlayerName(value);
                break;
            case WeaponPrimarySoundSlot.FireLastSound:
                draft.SetDefinitionPrimarySoundsFireLastSoundName(value);
                break;
            case WeaponPrimarySoundSlot.FireLastSoundPlayer:
                draft.SetDefinitionPrimarySoundsFireLastSoundPlayerName(value);
                break;
            case WeaponPrimarySoundSlot.EmptyFireSound:
                draft.SetDefinitionPrimarySoundsEmptyFireSoundName(value);
                break;
            case WeaponPrimarySoundSlot.EmptyFireSoundPlayer:
                draft.SetDefinitionPrimarySoundsEmptyFireSoundPlayerName(value);
                break;
            case WeaponPrimarySoundSlot.MeleeSwipeSound:
                draft.SetDefinitionPrimarySoundsMeleeSwipeSoundName(value);
                break;
            case WeaponPrimarySoundSlot.MeleeSwipeSoundPlayer:
                draft.SetDefinitionPrimarySoundsMeleeSwipeSoundPlayerName(value);
                break;
            case WeaponPrimarySoundSlot.MeleeHitSound:
                draft.SetDefinitionPrimarySoundsMeleeHitSoundName(value);
                break;
            case WeaponPrimarySoundSlot.MeleeMissSound:
                draft.SetDefinitionPrimarySoundsMeleeMissSoundName(value);
                break;
            case WeaponPrimarySoundSlot.RechamberSound:
                draft.SetDefinitionPrimarySoundsRechamberSoundName(value);
                break;
            case WeaponPrimarySoundSlot.RechamberSoundPlayer:
                draft.SetDefinitionPrimarySoundsRechamberSoundPlayerName(value);
                break;
            case WeaponPrimarySoundSlot.ReloadSound:
                draft.SetDefinitionPrimarySoundsReloadSoundName(value);
                break;
            case WeaponPrimarySoundSlot.ReloadSoundPlayer:
                draft.SetDefinitionPrimarySoundsReloadSoundPlayerName(value);
                break;
            case WeaponPrimarySoundSlot.ReloadEmptySound:
                draft.SetDefinitionPrimarySoundsReloadEmptySoundName(value);
                break;
            case WeaponPrimarySoundSlot.ReloadEmptySoundPlayer:
                draft.SetDefinitionPrimarySoundsReloadEmptySoundPlayerName(value);
                break;
            case WeaponPrimarySoundSlot.ReloadStartSound:
                draft.SetDefinitionPrimarySoundsReloadStartSoundName(value);
                break;
            case WeaponPrimarySoundSlot.ReloadStartSoundPlayer:
                draft.SetDefinitionPrimarySoundsReloadStartSoundPlayerName(value);
                break;
            case WeaponPrimarySoundSlot.ReloadEndSound:
                draft.SetDefinitionPrimarySoundsReloadEndSoundName(value);
                break;
            case WeaponPrimarySoundSlot.ReloadEndSoundPlayer:
                draft.SetDefinitionPrimarySoundsReloadEndSoundPlayerName(value);
                break;
            case WeaponPrimarySoundSlot.DetonateSound:
                draft.SetDefinitionPrimarySoundsDetonateSoundName(value);
                break;
            case WeaponPrimarySoundSlot.DetonateSoundPlayer:
                draft.SetDefinitionPrimarySoundsDetonateSoundPlayerName(value);
                break;
            case WeaponPrimarySoundSlot.NightVisionWearSound:
                draft.SetDefinitionPrimarySoundsNightVisionWearSoundName(value);
                break;
            case WeaponPrimarySoundSlot.NightVisionWearSoundPlayer:
                draft.SetDefinitionPrimarySoundsNightVisionWearSoundPlayerName(value);
                break;
            case WeaponPrimarySoundSlot.NightVisionRemoveSound:
                draft.SetDefinitionPrimarySoundsNightVisionRemoveSoundName(value);
                break;
            case WeaponPrimarySoundSlot.NightVisionRemoveSoundPlayer:
                draft.SetDefinitionPrimarySoundsNightVisionRemoveSoundPlayerName(value);
                break;
            case WeaponPrimarySoundSlot.AltSwitchSound:
                draft.SetDefinitionPrimarySoundsAltSwitchSoundName(value);
                break;
            case WeaponPrimarySoundSlot.AltSwitchSoundPlayer:
                draft.SetDefinitionPrimarySoundsAltSwitchSoundPlayerName(value);
                break;
            case WeaponPrimarySoundSlot.RaiseSound:
                draft.SetDefinitionPrimarySoundsRaiseSoundName(value);
                break;
            case WeaponPrimarySoundSlot.RaiseSoundPlayer:
                draft.SetDefinitionPrimarySoundsRaiseSoundPlayerName(value);
                break;
            case WeaponPrimarySoundSlot.FirstRaiseSound:
                draft.SetDefinitionPrimarySoundsFirstRaiseSoundName(value);
                break;
            case WeaponPrimarySoundSlot.FirstRaiseSoundPlayer:
                draft.SetDefinitionPrimarySoundsFirstRaiseSoundPlayerName(value);
                break;
            case WeaponPrimarySoundSlot.PutawaySound:
                draft.SetDefinitionPrimarySoundsPutawaySoundName(value);
                break;
            case WeaponPrimarySoundSlot.PutawaySoundPlayer:
                draft.SetDefinitionPrimarySoundsPutawaySoundPlayerName(value);
                break;
            case WeaponPrimarySoundSlot.ScanSound:
                draft.SetDefinitionPrimarySoundsScanSoundName(value);
                break;
        }
    }
}
