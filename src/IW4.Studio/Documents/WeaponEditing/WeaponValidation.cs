using IW4.Assets.Assets;
using IW4.Assets.Assets.Weapon;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Strings;
using IW4.FastFiles.Zone;

namespace IW4.Studio.Documents;

internal static class WeaponValidation
{
    internal static IReadOnlyList<AssetValidationIssue> Validate(WeaponDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var issues = new List<AssetValidationIssue>();
        WeaponVariantDef variant = draft.Variant;

        RequiredText(issues, "weapon.variant.internalName", variant.InternalName);
        Text(issues, "weapon.variant.displayName", variant.DisplayName);
        Text(issues, "weapon.variant.alternateWeaponName", variant.AlternateWeaponName);
        OptionalTable(issues, "weapon.variant.hideTags", variant.HideTagsPointer.Type,
            WeaponVariantDef.HideTagCount, variant.HideTags.Count);
        OptionalParallelTable(issues, "weapon.variant.animationNames",
            variant.AnimationNamesPointer.Type, WeaponVariantDef.WeaponAnimCount,
            variant.AnimationNamePointers.Count, variant.AnimationNames.Count);
        ScriptStrings(issues, "weapon.variant.hideTags", variant.HideTags);
        Texts(issues, "weapon.variant.animationNames", variant.AnimationNames);
        Count(issues, "weapon.variant.accuracyGraphKnots",
            variant.AccuracyGraphKnotCount, variant.AccuracyGraphKnots.Count);
        Count(issues, "weapon.variant.originalAccuracyGraphKnots",
            variant.OriginalAccuracyGraphKnotCount,
            variant.OriginalAccuracyGraphKnots.Count);
        Provider(issues, "weapon.variant.killIcon", variant.KillIcon,
            XAssetType.Material);
        Provider(issues, "weapon.variant.dpadIcon", variant.DpadIcon,
            XAssetType.Material);
        WeaponNonFiniteValidation.Append(issues, draft.ToAsset());

        if (!draft.HasDefinition || variant.Definition is null)
        {
            Error(issues, "weapon.variant.definition",
                "Weapon definition is unavailable.");
            return Array.AsReadOnly(issues.ToArray());
        }

        ValidateDefinition(issues, variant, variant.Definition);
        return Array.AsReadOnly(issues.ToArray());
    }

    private static void ValidateDefinition(
        List<AssetValidationIssue> issues,
        WeaponVariantDef variant,
        WeaponDef definition)
    {
        Text(issues, "weapon.definition.internalName", definition.InternalName);
        if (definition.InternalName is null &&
            definition.InternalNamePointer.Type != PointerType.Null)
        {
            Error(issues, "weapon.definition.internalName",
                "A non-null XString pointer requires semantic text.");
        }
        if (!string.Equals(variant.InternalName, definition.InternalName,
                StringComparison.Ordinal))
        {
            Warning(issues, "weapon.definition.internalName",
                "Weapon variant and definition names disagree.");
        }
        Text(issues, "weapon.definition.modeName", definition.ModeName);
        Text(issues, "weapon.definition.scriptName", definition.ScriptName);

        OptionalProviderTable(issues, "weapon.definition.gunModels",
            definition.GunModelsPointer.Type, WeaponDef.GunModelCount,
            definition.GunModelPointers.Count, definition.GunModels.Count);
        OptionalParallelTable(issues,
            "weapon.definition.rightHandAnimationNames",
            definition.RightHandAnimationNamesPointer.Type,
            WeaponDef.WeaponAnimCount,
            definition.RightHandAnimationNamePointers.Count,
            definition.RightHandAnimationNames.Count);
        OptionalParallelTable(issues,
            "weapon.definition.leftHandAnimationNames",
            definition.LeftHandAnimationNamesPointer.Type,
            WeaponDef.WeaponAnimCount,
            definition.LeftHandAnimationNamePointers.Count,
            definition.LeftHandAnimationNames.Count);
        Texts(issues, "weapon.definition.rightHandAnimationNames",
            definition.RightHandAnimationNames);
        Texts(issues, "weapon.definition.leftHandAnimationNames",
            definition.LeftHandAnimationNames);

        ValidateNoteTracks(issues, definition.NoteTrackMaps);
        ExactParallel(issues, "weapon.definition.flashEffects", 2,
            definition.FlashEffectPointers.Count, definition.FlashEffects.Count);
        ExactParallel(issues, "weapon.definition.soundAliases",
            WeaponDef.WeaponSoundAliasCount, definition.SoundAliasPointers.Count,
            definition.SoundAliasValuePointers.Count,
            definition.SoundAliasNames.Count);
        OptionalParallelTable(issues, "weapon.definition.bounceSounds",
            definition.BounceSoundPointer.Type, WeaponDef.SurfaceCount,
            definition.BounceSoundPointers.Count,
            definition.BounceSoundValuePointers.Count,
            definition.BounceSoundNames.Count);
        ExactParallel(issues, "weapon.definition.effects", 4,
            definition.EffectPointers.Count, definition.Effects.Count);
        ExactParallel(issues, "weapon.definition.materials", 2,
            definition.MaterialPointers.Count, definition.Materials.Count);
        OptionalProviderTable(issues, "weapon.definition.worldGunModels",
            definition.WorldGunModelsPointer.Type, WeaponDef.GunModelCount,
            definition.WorldGunModelPointers.Count,
            definition.WorldGunModels.Count);
        ExactParallel(issues, "weapon.definition.worldModels", 4,
            definition.WorldModelPointers.Count, definition.WorldModels.Count);
        Exact(issues, "weapon.definition.iconMaterials", 3,
            definition.IconMaterials.Count);
        ExactParallel(issues, "weapon.definition.overlayMaterials", 4,
            definition.Overlay.OverlayMaterials.Count,
            definition.OverlayMaterials.Count);
        Exact(issues, "weapon.definition.projectileEffects", 2,
            definition.ProjectileEffects.Count);
        Exact(issues, "weapon.definition.impactEffects", 2,
            definition.ImpactEffects.Count);
        OptionalTable(issues, "weapon.definition.projectile.parallelBounce",
            definition.Projectile.ParallelBouncePointer.Type,
            WeaponDef.SurfaceCount, definition.Projectile.ParallelBounce.Count);
        OptionalTable(issues,
            "weapon.definition.projectile.perpendicularBounce",
            definition.Projectile.PerpendicularBouncePointer.Type,
            WeaponDef.SurfaceCount,
            definition.Projectile.PerpendicularBounce.Count);
        OptionalTable(issues,
            "weapon.definition.locationDamageMultipliers",
            definition.LocationDamageMultipliersPointer.Type,
            WeaponDef.HitLocationCount,
            definition.LocationDamageMultipliers.Count);
        ExactParallel(issues,
            "weapon.definition.turret.barrelSpinUpSoundNames",
            WeaponDef.TurretBarrelSpinSoundCount,
            definition.Turret.BarrelSpinUpSoundPointers.Count,
            definition.Turret.BarrelSpinUpSoundValuePointers.Count,
            definition.Turret.BarrelSpinUpSoundNames.Count);
        ExactParallel(issues,
            "weapon.definition.turret.barrelSpinDownSoundNames",
            WeaponDef.TurretBarrelSpinSoundCount,
            definition.Turret.BarrelSpinDownSoundPointers.Count,
            definition.Turret.BarrelSpinDownSoundValuePointers.Count,
            definition.Turret.BarrelSpinDownSoundNames.Count);

        Count(issues, "weapon.definition.accuracy.graphKnots",
            variant.AccuracyGraphKnotCount,
            definition.Accuracy.GraphKnots.Count);
        Count(issues, "weapon.definition.accuracy.originalGraphKnots",
            variant.OriginalAccuracyGraphKnotCount,
            definition.Accuracy.OriginalGraphKnots.Count);

        Defined(issues, "weapon.definition.weaponType", definition.WeaponType);
        Defined(issues, "weapon.definition.weaponClass", definition.WeaponClass);
        Defined(issues, "weapon.definition.penetrateType", definition.PenetrateType);
        Defined(issues, "weapon.definition.inventoryType", definition.InventoryType);
        Defined(issues, "weapon.definition.fireType", definition.FireType);
        Defined(issues, "weapon.definition.offhandClass", definition.OffhandClass);
        Defined(issues, "weapon.definition.stance", definition.Stance);
        Defined(issues, "weapon.definition.reticle.activeType",
            definition.Reticle.ActiveType);
        Defined(issues, "weapon.definition.icons.ammoCounterClip",
            definition.Icons.AmmoCounterClip);
        Defined(issues, "weapon.definition.overlay.reticle",
            definition.Overlay.Reticle);
        Defined(issues, "weapon.definition.overlay.interface",
            definition.Overlay.Interface);
        Defined(issues, "weapon.definition.projectile.explosion",
            definition.Projectile.Explosion);
        Defined(issues, "weapon.definition.projectile.stickiness",
            definition.Projectile.Stickiness);
        Defined(issues, "weapon.definition.projectile.guidedMissileType",
            definition.Projectile.GuidedMissileType);

        Providers(issues, "weapon.definition.gunModels", definition.GunModels,
            XAssetType.XModel);
        Provider(issues, "weapon.definition.handModel", definition.HandModel,
            XAssetType.XModel);
        Providers(issues, "weapon.definition.flashEffects",
            definition.FlashEffects, XAssetType.Fx);
        Providers(issues, "weapon.definition.effects", definition.Effects,
            XAssetType.Fx);
        Providers(issues, "weapon.definition.materials", definition.Materials,
            XAssetType.Material);
        Providers(issues, "weapon.definition.worldGunModels",
            definition.WorldGunModels, XAssetType.XModel);
        Providers(issues, "weapon.definition.worldModels",
            definition.WorldModels, XAssetType.XModel);
        Providers(issues, "weapon.definition.iconMaterials",
            definition.IconMaterials, XAssetType.Material);
        Providers(issues, "weapon.definition.overlayMaterials",
            definition.OverlayMaterials, XAssetType.Material);
        Provider(issues, "weapon.definition.physCollmap",
            definition.PhysCollmap, XAssetType.PhysCollmap);
        Provider(issues, "weapon.definition.projectile.model",
            definition.Projectile.Model, XAssetType.XModel);
        Providers(issues, "weapon.definition.projectileEffects",
            definition.ProjectileEffects, XAssetType.Fx);
        Providers(issues, "weapon.definition.impactEffects",
            definition.ImpactEffects, XAssetType.Fx);
        Provider(issues, "weapon.definition.projectile.ignitionEffect",
            definition.ViewShellEjectEffect, XAssetType.Fx);
        Provider(issues, "weapon.definition.tracer", definition.Tracer,
            XAssetType.Tracer);
        Provider(issues, "weapon.definition.turret.overheatEffect",
            definition.TurretOverheatEffect, XAssetType.Fx);

        if (definition.PhysCollmap is null && definition.PhysCollmapName is not null)
        {
            Error(issues, "weapon.definition.physCollmap",
                "PhysCollmap name has no semantic provider.");
        }
        else if (definition.PhysCollmap is not null &&
            definition.PhysCollmapName is not null &&
            !string.Equals(definition.PhysCollmap.Name,
                definition.PhysCollmapName, StringComparison.Ordinal))
        {
            Error(issues, "weapon.definition.physCollmap",
                "PhysCollmap name disagrees with its semantic provider.");
        }

        ValidateDefinitionTexts(issues, definition);
    }

    private static void ValidateNoteTracks(
        List<AssetValidationIssue> issues,
        WeaponNoteTrackMaps maps)
    {
        OptionalTable(issues, "weapon.definition.noteTrackMaps.soundKeys",
            maps.SoundMapKeysPointer.Type, WeaponDef.NoteTrackMapCount,
            maps.SoundMapKeys.Count);
        OptionalTable(issues, "weapon.definition.noteTrackMaps.soundValues",
            maps.SoundMapValuesPointer.Type, WeaponDef.NoteTrackMapCount,
            maps.SoundMapValues.Count);
        OptionalTable(issues, "weapon.definition.noteTrackMaps.rumbleKeys",
            maps.RumbleMapKeysPointer.Type, WeaponDef.NoteTrackMapCount,
            maps.RumbleMapKeys.Count);
        OptionalTable(issues, "weapon.definition.noteTrackMaps.rumbleValues",
            maps.RumbleMapValuesPointer.Type, WeaponDef.NoteTrackMapCount,
            maps.RumbleMapValues.Count);
        ScriptStrings(issues, "weapon.definition.noteTrackMaps.soundKeys",
            maps.SoundMapKeys);
        ScriptStrings(issues, "weapon.definition.noteTrackMaps.soundValues",
            maps.SoundMapValues);
        ScriptStrings(issues, "weapon.definition.noteTrackMaps.rumbleKeys",
            maps.RumbleMapKeys);
        ScriptStrings(issues, "weapon.definition.noteTrackMaps.rumbleValues",
            maps.RumbleMapValues);
    }

    private static void ValidateDefinitionTexts(
        List<AssetValidationIssue> issues,
        WeaponDef value)
    {
        Texts(issues, "weapon.definition.soundAliases", value.SoundAliasNames);
        Texts(issues, "weapon.definition.bounceSounds", value.BounceSoundNames);
        Text(issues, "weapon.definition.ammo.ammoName", value.Ammo.AmmoName);
        Text(issues, "weapon.definition.ammo.clipName", value.Ammo.ClipName);
        Text(issues, "weapon.definition.ammo.sharedAmmoCapName",
            value.Ammo.SharedAmmoCapName);
        Text(issues, "weapon.definition.projectile.explosionSound",
            value.Projectile.ExplosionSound);
        Text(issues, "weapon.definition.projectile.dudSound",
            value.Projectile.DudSound);
        Text(issues, "weapon.definition.projectile.ignitionSound",
            value.Projectile.IgnitionSound);
        Text(issues, "weapon.definition.accuracy.graphName0",
            value.Accuracy.GraphName0);
        Text(issues, "weapon.definition.accuracy.graphName1",
            value.Accuracy.GraphName1);
        Text(issues, "weapon.definition.hints.useHintString",
            value.Hints.UseHintString);
        Text(issues, "weapon.definition.hints.dropHintString",
            value.Hints.DropHintString);
        Text(issues, "weapon.definition.rumble.fireRumble",
            value.Rumble.FireRumble);
        Text(issues, "weapon.definition.rumble.meleeImpactRumble",
            value.Rumble.MeleeImpactRumble);
        Text(issues, "weapon.definition.turret.overheatSound",
            value.Turret.OverheatSound);
        Text(issues, "weapon.definition.turret.barrelSpinRumble",
            value.Turret.BarrelSpinRumble);
        Text(issues, "weapon.definition.turret.barrelSpinMaxSound",
            value.Turret.BarrelSpinMaxSound);
        Texts(issues, "weapon.definition.turret.barrelSpinUpSoundNames",
            value.Turret.BarrelSpinUpSoundNames);
        Texts(issues, "weapon.definition.turret.barrelSpinDownSoundNames",
            value.Turret.BarrelSpinDownSoundNames);
        Text(issues, "weapon.definition.missileConeSound.alias",
            value.MissileConeSound.Alias);
        Text(issues, "weapon.definition.missileConeSound.aliasAtBase",
            value.MissileConeSound.AliasAtBase);
    }

    private static void OptionalProviderTable(
        List<AssetValidationIssue> issues, string path, PointerType pointerType,
        int expected, params int[] counts) =>
        OptionalParallelTable(issues, path, pointerType, expected, counts);

    private static void OptionalParallelTable(
        List<AssetValidationIssue> issues, string path, PointerType pointerType,
        int expected, params int[] counts)
    {
        bool absent = pointerType == PointerType.Null && counts.All(count => count == 0);
        if (absent)
            return;
        if (pointerType == PointerType.Null)
        {
            Error(issues, path,
                "The owning pointer is null, but one or more parallel collections contain rows.");
        }
        ExactParallel(issues, path, expected, counts);
    }

    private static void OptionalTable(
        List<AssetValidationIssue> issues, string path, PointerType pointerType,
        int expected, int count)
    {
        if (pointerType == PointerType.Null && count == 0)
            return;
        if (pointerType == PointerType.Null)
        {
            Error(issues, path,
                "The owning pointer is null, but the collection contains rows.");
        }
        Exact(issues, path, expected, count);
    }

    private static void ExactParallel(
        List<AssetValidationIssue> issues, string path, int expected,
        params int[] counts)
    {
        for (int index = 0; index < counts.Length; index++)
        {
            if (counts[index] != expected)
            {
                Error(issues, path,
                    $"Parallel collection {index} contains {counts[index]} row(s); exactly {expected} are required.");
            }
        }
    }

    private static void Exact(
        List<AssetValidationIssue> issues, string path, int expected, int actual)
    {
        if (actual != expected)
            Error(issues, path,
                $"Collection contains {actual} row(s); exactly {expected} are required.");
    }

    private static void Count(
        List<AssetValidationIssue> issues, string path, ushort expected,
        int actual)
    {
        if (actual != expected)
            Error(issues, path,
                $"Collection contains {actual} row(s), but its native count requires {expected}.");
    }

    private static void Defined<TEnum>(
        List<AssetValidationIssue> issues, string path, TEnum value)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
            Error(issues, path, $"Value {value} is unsupported.");
    }

    private static void Providers<T>(
        List<AssetValidationIssue> issues, string path,
        IReadOnlyList<T?> values, XAssetType expected) where T : BaseAsset
    {
        for (int index = 0; index < values.Count; index++)
            Provider(issues, $"{path}[{index}]", values[index], expected);
    }

    private static void Provider(
        List<AssetValidationIssue> issues, string path, BaseAsset? value,
        XAssetType expected)
    {
        if (value is null)
            return;
        if (value.SerializedAssetType != expected)
        {
            Error(issues, path,
                $"Provider type {value.SerializedAssetType} does not match {expected}.");
            return;
        }
        RequiredText(issues, path, value.SerializedAssetName);
    }

    private static void ScriptStrings(
        List<AssetValidationIssue> issues, string path,
        IReadOnlyList<ScriptStringReference> values)
    {
        for (int index = 0; index < values.Count; index++)
            Text(issues, $"{path}[{index}]", values[index].Text);
    }

    private static void Texts(
        List<AssetValidationIssue> issues, string path,
        IReadOnlyList<string?> values)
    {
        for (int index = 0; index < values.Count; index++)
            Text(issues, $"{path}[{index}]", values[index]);
    }

    private static void RequiredText(
        List<AssetValidationIssue> issues, string path, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Error(issues, path, "A non-empty value is required.");
            return;
        }
        Text(issues, path, value);
    }

    private static void Text(
        List<AssetValidationIssue> issues, string path, string? value)
    {
        if (value is null)
            return;
        if (value.Contains('\0'))
            Error(issues, path, "Text cannot contain NUL.");
        if (value.Any(character => character > byte.MaxValue))
            Error(issues, path, "Text must be representable as Latin-1.");
    }

    private static void Error(
        List<AssetValidationIssue> issues, string path, string message) =>
        issues.Add(new AssetValidationIssue(path, message,
            AssetValidationSeverity.Error));

    private static void Warning(
        List<AssetValidationIssue> issues, string path, string message) =>
        issues.Add(new AssetValidationIssue(path, message,
            AssetValidationSeverity.Warning));
}

internal sealed class WeaponAdapter : AssetAuthoringAdapter<WeaponAsset, WeaponDraft>
{
    public override XAssetType AssetType => XAssetType.Weapon;

    public override WeaponDraft CreateDraft(WeaponAsset definition) =>
        new(definition ?? throw new ArgumentNullException(nameof(definition)));

    public override WeaponDraft CloneDraft(WeaponDraft draft) =>
        (draft ?? throw new ArgumentNullException(nameof(draft))).Clone();

    public override WeaponAsset CreateDefinition(WeaponDraft draft) =>
        (draft ?? throw new ArgumentNullException(nameof(draft))).ToAsset();

    public override bool SemanticallyEquals(WeaponDraft left, WeaponDraft right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return WeaponGraph.Equal(left.ToAsset(), right.ToAsset());
    }

    public override IReadOnlyList<AssetValidationIssue> Validate(WeaponDraft draft) =>
        WeaponValidation.Validate(draft);
}
