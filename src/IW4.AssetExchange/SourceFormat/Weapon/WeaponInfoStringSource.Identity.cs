using IW4.AssetExchange.SourceFormat.InfoString;
using IW4.Assets.Assets.Weapon;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;

namespace IW4.AssetExchange.SourceFormat.Weapon;

internal static partial class WeaponInfoStringSource
{
    private static void AddIdentityAndAnimations(
        InfoStringSourceWriter source,
        WeaponVariantDef variant,
        WeaponDef definition,
        string assetName)
    {
        source.AddString("displayName", Materialized(
            variant.DisplayNamePointer.Raw,
            variant.DisplayName,
            $"Weapon '{assetName}' display name"));

        // WeaponDef offset 0x000 corresponds to IW4 szOverlayName despite
        // the legacy name used by the current PS3 asset model.
        source.AddString("AIOverlayDescription", Materialized(
            definition.InternalNamePointer.Raw,
            definition.InternalName,
            $"Weapon '{assetName}' AI overlay description"));

        source.AddString("modeName", Materialized(
            definition.ModeNamePointer.Raw,
            definition.ModeName,
            $"Weapon '{assetName}' mode name"));
        source.AddEnum(
            "playerAnimType",
            definition.PlayerAnimType,
            PlayerAnimationTypeNames,
            $"Weapon '{assetName}' player animation type");

        AddModelArray(
            source,
            "gunModel",
            definition.GunModelsPointer.Raw,
            definition.GunModelPointers,
            definition.GunModels,
            WeaponDef.GunModelCount,
            $"Weapon '{assetName}' gun models");
        source.AddString("handModel", Referenced(
            definition.HandModelPointer.Raw,
            definition.HandModel,
            $"Weapon '{assetName}' hand model"));
        source.AddString("hideTags", HideTags(variant, assetName));
        source.AddString(
            "notetrackSoundMap",
            NoteTrackMap(
                definition.NoteTrackMaps.SoundMapKeysPointer.Raw,
                definition.NoteTrackMaps.SoundMapValuesPointer.Raw,
                definition.NoteTrackMaps.SoundMappings,
                WeaponDef.NoteTrackMapCount,
                $"Weapon '{assetName}' sound notetrack map"));
        source.AddString(
            "notetrackRumbleMap",
            NoteTrackMap(
                definition.NoteTrackMaps.RumbleMapKeysPointer.Raw,
                definition.NoteTrackMaps.RumbleMapValuesPointer.Raw,
                definition.NoteTrackMaps.RumbleMappings,
                WeaponDef.NoteTrackMapCount,
                $"Weapon '{assetName}' rumble notetrack map"));

        AddAnimationFields(
            source,
            string.Empty,
            variant.AnimationNamesPointer.Raw,
            variant.AnimationNamePointers,
            variant.AnimationNames,
            $"Weapon '{assetName}' variant animations");
        AddAnimationFields(
            source,
            "R",
            definition.RightHandAnimationNamesPointer.Raw,
            definition.RightHandAnimationNamePointers,
            definition.RightHandAnimationNames,
            $"Weapon '{assetName}' right-handed animations");
        AddAnimationFields(
            source,
            "L",
            definition.LeftHandAnimationNamesPointer.Raw,
            definition.LeftHandAnimationNamePointers,
            definition.LeftHandAnimationNames,
            $"Weapon '{assetName}' left-handed animations");

        source.AddString("script", Materialized(
            definition.ScriptNamePointer.Raw,
            definition.ScriptName,
            $"Weapon '{assetName}' script name"));
        source.AddEnum(
            "weaponType",
            (int)definition.WeaponType,
            WeaponTypeNames,
            $"Weapon '{assetName}' type");
        source.AddEnum(
            "weaponClass",
            (int)definition.WeaponClass,
            WeaponClassNames,
            $"Weapon '{assetName}' class");
        source.AddEnum(
            "penetrateType",
            (int)definition.PenetrateType,
            PenetrateTypeNames,
            $"Weapon '{assetName}' penetration type");
        source.AddFloat("penetrateMultiplier", variant.PenetrateMultiplier);
        source.AddEnum(
            "impactType",
            variant.ImpactType,
            ImpactTypeNames,
            $"Weapon '{assetName}' impact type");
        source.AddEnum(
            "inventoryType",
            (int)definition.InventoryType,
            InventoryTypeNames,
            $"Weapon '{assetName}' inventory type");
        source.AddEnum(
            "fireType",
            (int)definition.FireType,
            FireTypeNames,
            $"Weapon '{assetName}' fire type");
        source.AddEnum(
            "offhandClass",
            (int)definition.OffhandClass,
            OffhandClassNames,
            $"Weapon '{assetName}' offhand class");
    }

    private static void AddModelArray(
        InfoStringSourceWriter source,
        string firstKey,
        int arrayPointerRaw,
        IReadOnlyList<XPointer<XModelAsset>> pointers,
        IReadOnlyList<XModelAsset?> models,
        int expectedCount,
        string field)
    {
        RequireList(arrayPointerRaw, models.Count, expectedCount, field);
        if (pointers.Count != expectedCount &&
            (arrayPointerRaw != 0 || pointers.Count != 0))
        {
            throw new InvalidDataException(
                $"{field} requires {expectedCount} materialized pointer cells but has {pointers.Count}.");
        }

        for (int index = 0; index < expectedCount; index++)
        {
            string key = index == 0 ? firstKey : $"{firstKey}{index + 1}";
            int pointerRaw = pointers.Count == expectedCount
                ? pointers[index].Raw
                : 0;
            XModelAsset? model = models.Count == expectedCount
                ? models[index]
                : null;
            source.AddString(key, Referenced(
                pointerRaw,
                model,
                $"{field} entry {index}"));
        }
    }

    private static void AddAnimationFields(
        InfoStringSourceWriter source,
        string suffix,
        int arrayPointerRaw,
        IReadOnlyList<XPointer<string>> pointers,
        IReadOnlyList<string?> names,
        string field)
    {
        int expectedCount = (int)WeaponAnimationSlot.Count;
        RequireList(arrayPointerRaw, names.Count, expectedCount, field);
        if (pointers.Count != expectedCount &&
            (arrayPointerRaw != 0 || pointers.Count != 0))
        {
            throw new InvalidDataException(
                $"{field} requires {expectedCount} materialized pointer cells but has {pointers.Count}.");
        }

        int rootIndex = (int)WeaponAnimationSlot.Root;
        int rootPointerRaw = pointers.Count == expectedCount
            ? pointers[rootIndex].Raw
            : 0;
        string? rootName = names.Count == expectedCount
            ? names[rootIndex]
            : null;
        if (Materialized(rootPointerRaw, rootName, $"{field} slot Root").Length != 0)
        {
            throw new InvalidDataException(
                $"{field} has a Root animation, which the IW4 source format does not expose.");
        }

        foreach ((string key, WeaponAnimationSlot slot) in AnimationFields)
        {
            int index = (int)slot;
            int pointerRaw = pointers.Count == expectedCount
                ? pointers[index].Raw
                : 0;
            string? name = names.Count == expectedCount
                ? names[index]
                : null;
            source.AddString(
                $"{key}{suffix}",
                Materialized(pointerRaw, name, $"{field} slot {slot}"));
        }
    }

    private static string HideTags(WeaponVariantDef variant, string assetName)
    {
        RequireList(
            variant.HideTagsPointer.Raw,
            variant.HideTags.Count,
            WeaponVariantDef.HideTagCount,
            $"Weapon '{assetName}' hide tags");
        if (variant.HideTags.Count == 0)
            return string.Empty;

        return string.Join("\n", variant.HideTags
            .Select((tag, index) => ScriptString(
                tag,
                $"Weapon '{assetName}' hide tag {index}"))
            .Where(value => value.Length != 0));
    }

    private static string NoteTrackMap(
        int keysPointerRaw,
        int valuesPointerRaw,
        IReadOnlyList<WeaponNoteTrackMapEntry> mappings,
        int expectedCount,
        string field)
    {
        RequireList(
            Presence(keysPointerRaw, valuesPointerRaw),
            mappings.Count,
            expectedCount,
            field);
        if (mappings.Count == 0)
            return string.Empty;

        var rows = new List<string>(expectedCount);
        for (int index = 0; index < expectedCount; index++)
        {
            string key = ScriptString(mappings[index].Key, $"{field} key {index}");
            string value = ScriptString(
                mappings[index].Value,
                $"{field} value {index}");
            if (key.Length == 0)
            {
                if (value.Length != 0)
                {
                    throw new InvalidDataException(
                        $"{field} entry {index} has a value without a key.");
                }

                continue;
            }
            if (value.Length == 0)
            {
                throw new InvalidDataException(
                    $"{field} entry {index} has a key without a value.");
            }
            if (key.Any(char.IsWhiteSpace) || value.Any(char.IsWhiteSpace))
            {
                throw new InvalidDataException(
                    $"{field} entry {index} contains whitespace and cannot be represented as an IW4 notetrack pair.");
            }

            rows.Add($"{key} {value}");
        }

        return string.Join("\n", rows);
    }
}
