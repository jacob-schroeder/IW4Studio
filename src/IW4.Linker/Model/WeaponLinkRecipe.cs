using IW4.Assets.Assets;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Weapon;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Strings;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;
using FxEffectDefAsset = IW4.Assets.Assets.Fx.FxEffectDefAsset;
using PhysicsPhysCollmapAsset = IW4.Assets.Assets.Physics.PhysCollmapAsset;
using TracerDefAsset = IW4.Assets.Assets.Tracer.TracerDefAsset;
using XModelAsset = IW4.Assets.Assets.XModel.XModelAsset;
using XString = IW4.FastFiles.Pointers.XPointer<string>;

namespace IW4.Linker.Model;

/// <summary>
/// Frozen WeaponVariantDef/WeaponDef graph. The nested WeaponDef is direct
/// storage identity, asset references are provider symbols, and sound-alias
/// names retain their native one-word direct wrappers around XStrings.
/// </summary>
internal sealed class WeaponLinkRecipe : AssetLinkRecipe
{
    private delegate LinkOperation FrozenOperation(
        LinkStorageSymbol owner,
        int addend);

    private WeaponLinkRecipe(
        AssetKey key,
        string originalSerializedName,
        LinkStorageSymbol nameStorage,
        byte[] rootTemplate,
        IReadOnlyList<FrozenOperation> operations)
        : base(key, originalSerializedName, nameStorage)
    {
        Root = LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            rootTemplate,
            alignment: 4,
            root =>
            [
                NameOperation(root, 0),
                .. operations.Select(operation => operation(root, 0))
            ]);
    }

    internal override LinkStorageSymbol Root { get; }

    public static AssetLinkRecipe Freeze(
        AssetKey key,
        string originalSerializedName,
        WeaponAsset definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(freeze);
        WeaponVariantDef variant = definition.Variant ??
            throw new InvalidDataException("Weapon.Variant cannot be null.");
        if (originalSerializedName.StartsWith(','))
        {
            ValidateReferenceShape(variant);
            return ExternalAssetLinkRecipe.Create(
                key,
                XAssetType.Weapon,
                originalSerializedName,
                freeze);
        }

        var operations = new List<FrozenOperation>();
        FreezeVariant(variant, freeze, operations);
        return new WeaponLinkRecipe(
            key,
            originalSerializedName,
            freeze.FreezeProviderName(originalSerializedName, 0, "Asset.Name"),
            BuildVariantTemplate(variant),
            Array.AsReadOnly(operations.ToArray()));
    }

    private static void FreezeVariant(
        WeaponVariantDef variant,
        LinkAssetFreezeScope freeze,
        ICollection<FrozenOperation> operations)
    {
        if (variant.Definition is null)
        {
            if (variant.DefinitionPointer.Type != PointerType.Null)
            {
                throw new NotSupportedException(
                    "Weapon.Variant.Definition retains direct storage without semantic data.");
            }
        }
        else
        {
            LinkStorageTarget definition = FreezeDefinition(
                variant.Definition,
                variant,
                variant.DefinitionPointer.Untyped,
                freeze);
            AddDirect(operations, 0x04, definition, "Weapon.Variant.Definition");
        }

        AddXString(
            operations,
            0x08,
            variant.DisplayName,
            variant.DisplayNamePointer.Untyped,
            "Weapon.Variant.DisplayName",
            freeze);
        LinkStorageTarget? hideTags = FreezeScriptStringArray(
            variant.HideTags,
            variant.HideTagsPointer.Untyped,
            WeaponVariantDef.HideTagCount,
            "Weapon.Variant.HideTags",
            freeze);
        if (hideTags is { } hideTagStorage)
            AddDirect(operations, 0x0c, hideTagStorage, "Weapon.Variant.HideTags");
        LinkStorageTarget? animations = FreezeXStringArray(
            variant.AnimationNamePointers,
            variant.AnimationNames,
            variant.AnimationNamesPointer.Untyped,
            WeaponVariantDef.WeaponAnimCount,
            "Weapon.Variant.AnimationNames",
            freeze);
        if (animations is { } animationStorage)
        {
            AddDirect(
                operations,
                0x10,
                animationStorage,
                "Weapon.Variant.AnimationNames");
        }
        AddXString(
            operations,
            0x3c,
            variant.AlternateWeaponName,
            variant.AlternateWeaponNamePointer.Untyped,
            "Weapon.Variant.AlternateWeaponName",
            freeze);
        AddProvider(
            operations,
            0x48,
            variant.KillIcon,
            variant.KillIconPointer.Untyped,
            XAssetType.Material,
            "Weapon.Variant.KillIcon");
        AddProvider(
            operations,
            0x4c,
            variant.DpadIcon,
            variant.DpadIconPointer.Untyped,
            XAssetType.Material,
            "Weapon.Variant.DpadIcon");
        RequireCount(
            variant.AccuracyGraphKnotCount,
            variant.AccuracyGraphKnots.Count,
            "Weapon.Variant.AccuracyGraphKnots");
        RequireCount(
            variant.OriginalAccuracyGraphKnotCount,
            variant.OriginalAccuracyGraphKnots.Count,
            "Weapon.Variant.OriginalAccuracyGraphKnots");
        LinkStorageTarget? graph = FreezeVec2Array(
            variant.AccuracyGraphKnots,
            variant.AccuracyGraphKnotsPointer.Untyped,
            "Weapon.Variant.AccuracyGraphKnots",
            freeze);
        if (graph is { } graphStorage)
            AddDirect(operations, 0x68, graphStorage, "Weapon.Variant.AccuracyGraphKnots");
        LinkStorageTarget? originalGraph = FreezeVec2Array(
            variant.OriginalAccuracyGraphKnots,
            variant.OriginalAccuracyGraphKnotsPointer.Untyped,
            "Weapon.Variant.OriginalAccuracyGraphKnots",
            freeze);
        if (originalGraph is { } originalGraphStorage)
        {
            AddDirect(
                operations,
                0x6c,
                originalGraphStorage,
                "Weapon.Variant.OriginalAccuracyGraphKnots");
        }
    }

    private static LinkStorageTarget FreezeDefinition(
        WeaponDef definition,
        WeaponVariantDef variant,
        XPointerReference pointer,
        LinkAssetFreezeScope freeze)
    {
        ValidateDefinitionEnums(definition);
        var operations = new List<FrozenOperation>();
        FreezeDefinitionOperations(definition, variant, freeze, operations);
        return freeze.FreezeStorage(
            pointer,
            BuildDefinitionTemplate(definition),
            XFileBlockType.LARGE,
            alignment: 4,
            (owner, addend) => operations.Select(operation => operation(owner, addend)),
            "Weapon.Variant.Definition");
    }

    private static void FreezeDefinitionOperations(
        WeaponDef definition,
        WeaponVariantDef variant,
        LinkAssetFreezeScope freeze,
        ICollection<FrozenOperation> operations)
    {
        AddXString(
            operations,
            0x000,
            definition.InternalName,
            definition.InternalNamePointer.Untyped,
            "Weapon.Definition.InternalName",
            freeze);
        LinkStorageTarget? gunModels = FreezeProviderTable(
            definition.GunModelPointers,
            definition.GunModels,
            definition.GunModelsPointer.Untyped,
            WeaponDef.GunModelCount,
            XAssetType.XModel,
            "Weapon.Definition.GunModels",
            freeze);
        if (gunModels is { } gunModelStorage)
            AddDirect(operations, 0x004, gunModelStorage, "Weapon.Definition.GunModels");
        AddProvider(
            operations,
            0x008,
            definition.HandModel,
            definition.HandModelPointer.Untyped,
            XAssetType.XModel,
            "Weapon.Definition.HandModel");
        LinkStorageTarget? rightAnimations = FreezeXStringArray(
            definition.RightHandAnimationNamePointers,
            definition.RightHandAnimationNames,
            definition.RightHandAnimationNamesPointer.Untyped,
            WeaponDef.WeaponAnimCount,
            "Weapon.Definition.RightHandAnimationNames",
            freeze);
        if (rightAnimations is { } rightAnimationStorage)
        {
            AddDirect(
                operations,
                0x00c,
                rightAnimationStorage,
                "Weapon.Definition.RightHandAnimationNames");
        }
        LinkStorageTarget? leftAnimations = FreezeXStringArray(
            definition.LeftHandAnimationNamePointers,
            definition.LeftHandAnimationNames,
            definition.LeftHandAnimationNamesPointer.Untyped,
            WeaponDef.WeaponAnimCount,
            "Weapon.Definition.LeftHandAnimationNames",
            freeze);
        if (leftAnimations is { } leftAnimationStorage)
        {
            AddDirect(
                operations,
                0x010,
                leftAnimationStorage,
                "Weapon.Definition.LeftHandAnimationNames");
        }
        AddXString(
            operations,
            0x014,
            definition.ModeName,
            definition.ModeNamePointer.Untyped,
            "Weapon.Definition.ModeName",
            freeze);

        WeaponNoteTrackMaps noteTracks = definition.NoteTrackMaps ??
            throw new InvalidDataException("Weapon.Definition.NoteTrackMaps cannot be null.");
        FreezeScriptMap(
            operations,
            0x018,
            noteTracks.SoundMapKeys,
            noteTracks.SoundMapKeysPointer.Untyped,
            "Weapon.Definition.NoteTrackMaps.SoundMapKeys",
            freeze);
        FreezeScriptMap(
            operations,
            0x01c,
            noteTracks.SoundMapValues,
            noteTracks.SoundMapValuesPointer.Untyped,
            "Weapon.Definition.NoteTrackMaps.SoundMapValues",
            freeze);
        FreezeScriptMap(
            operations,
            0x020,
            noteTracks.RumbleMapKeys,
            noteTracks.RumbleMapKeysPointer.Untyped,
            "Weapon.Definition.NoteTrackMaps.RumbleMapKeys",
            freeze);
        FreezeScriptMap(
            operations,
            0x024,
            noteTracks.RumbleMapValues,
            noteTracks.RumbleMapValuesPointer.Untyped,
            "Weapon.Definition.NoteTrackMaps.RumbleMapValues",
            freeze);

        AddProviders(
            operations,
            0x048,
            definition.FlashEffectPointers,
            definition.FlashEffects,
            2,
            XAssetType.Fx,
            "Weapon.Definition.FlashEffects");
        AddSoundCells(
            operations,
            0x050,
            definition.SoundAliasPointers,
            definition.SoundAliasValuePointers,
            definition.SoundAliasNames,
            WeaponDef.WeaponSoundAliasCount,
            "Weapon.Definition.SoundAliases",
            freeze);
        LinkStorageTarget? bounceSounds = FreezeSoundCellTable(
            definition.BounceSoundPointers,
            definition.BounceSoundValuePointers,
            definition.BounceSoundNames,
            definition.BounceSoundPointer.Untyped,
            WeaponDef.SurfaceCount,
            "Weapon.Definition.BounceSounds",
            freeze);
        if (bounceSounds is { } bounceStorage)
            AddDirect(operations, 0x10c, bounceStorage, "Weapon.Definition.BounceSounds");
        AddProviders(
            operations,
            0x110,
            definition.EffectPointers,
            definition.Effects,
            4,
            XAssetType.Fx,
            "Weapon.Definition.Effects");
        AddProviders(
            operations,
            0x120,
            definition.MaterialPointers,
            definition.Materials,
            2,
            XAssetType.Material,
            "Weapon.Definition.Materials");

        LinkStorageTarget? worldGunModels = FreezeProviderTable(
            definition.WorldGunModelPointers,
            definition.WorldGunModels,
            definition.WorldGunModelsPointer.Untyped,
            WeaponDef.GunModelCount,
            XAssetType.XModel,
            "Weapon.Definition.WorldGunModels",
            freeze);
        if (worldGunModels is { } worldGunStorage)
        {
            AddDirect(
                operations,
                0x1d8,
                worldGunStorage,
                "Weapon.Definition.WorldGunModels");
        }
        AddProviders(
            operations,
            0x1dc,
            definition.WorldModelPointers,
            definition.WorldModels,
            4,
            XAssetType.XModel,
            "Weapon.Definition.WorldModels");

        WeaponIconPointers icons = definition.Icons ??
            throw new InvalidDataException("Weapon.Definition.Icons cannot be null.");
        RequireExactCount(definition.IconMaterials, 3, "Weapon.Definition.IconMaterials");
        AddProvider(
            operations,
            0x1ec,
            definition.IconMaterials[0],
            icons.HudIconPointer.Untyped,
            XAssetType.Material,
            "Weapon.Definition.Icons.HudIcon");
        AddProvider(
            operations,
            0x1f4,
            definition.IconMaterials[1],
            icons.PickupIconPointer.Untyped,
            XAssetType.Material,
            "Weapon.Definition.Icons.PickupIcon");
        AddProvider(
            operations,
            0x1fc,
            definition.IconMaterials[2],
            icons.AmmoCounterIconPointer.Untyped,
            XAssetType.Material,
            "Weapon.Definition.Icons.AmmoCounterIcon");

        WeaponAmmoFields ammo = definition.Ammo ??
            throw new InvalidDataException("Weapon.Definition.Ammo cannot be null.");
        AddXString(operations, 0x20c, ammo.AmmoName, ammo.AmmoNamePointer.Untyped,
            "Weapon.Definition.Ammo.AmmoName", freeze);
        AddXString(operations, 0x214, ammo.ClipName, ammo.ClipNamePointer.Untyped,
            "Weapon.Definition.Ammo.ClipName", freeze);
        AddXString(operations, 0x224, ammo.SharedAmmoCapName,
            ammo.SharedAmmoCapNamePointer.Untyped,
            "Weapon.Definition.Ammo.SharedAmmoCapName", freeze);

        WeaponOverlayFields overlay = definition.Overlay ??
            throw new InvalidDataException("Weapon.Definition.Overlay cannot be null.");
        AddProviders(
            operations,
            0x308,
            overlay.OverlayMaterials,
            definition.OverlayMaterials,
            4,
            XAssetType.Material,
            "Weapon.Definition.OverlayMaterials");
        if (definition.PhysCollmap is null && definition.PhysCollmapName is not null)
        {
            throw new NotSupportedException(
                "Weapon.Definition.PhysCollmapName retains a logical name without its semantic provider.");
        }
        if (definition.PhysCollmap is not null &&
            definition.PhysCollmapName is not null &&
            !StringComparer.Ordinal.Equals(
                definition.PhysCollmapName,
                definition.PhysCollmap.Name))
        {
            throw new InvalidDataException(
                "Weapon.Definition.PhysCollmapName disagrees with its semantic provider.");
        }
        AddProvider(
            operations,
            0x3c8,
            definition.PhysCollmap,
            definition.PhysCollmapPointer.Untyped,
            XAssetType.PhysCollmap,
            "Weapon.Definition.PhysCollmap");

        FreezeProjectileOperations(definition, freeze, operations);
        FreezeAccuracyOperations(definition, variant, freeze, operations);

        WeaponHintFields hints = definition.Hints ??
            throw new InvalidDataException("Weapon.Definition.Hints cannot be null.");
        AddXString(operations, 0x568, hints.UseHintString,
            hints.UseHintStringPointer.Untyped,
            "Weapon.Definition.Hints.UseHintString", freeze);
        AddXString(operations, 0x56c, hints.DropHintString,
            hints.DropHintStringPointer.Untyped,
            "Weapon.Definition.Hints.DropHintString", freeze);
        AddXString(operations, 0x58c, definition.ScriptName,
            definition.ScriptNamePointer.Untyped,
            "Weapon.Definition.ScriptName", freeze);
        LinkStorageTarget? locationDamage = FreezeFloatArray(
            definition.LocationDamageMultipliers,
            definition.LocationDamageMultipliersPointer.Untyped,
            WeaponDef.HitLocationCount,
            "Weapon.Definition.LocationDamageMultipliers",
            freeze);
        if (locationDamage is { } locationStorage)
        {
            AddDirect(
                operations,
                0x5b4,
                locationStorage,
                "Weapon.Definition.LocationDamageMultipliers");
        }

        WeaponRumbleFields rumble = definition.Rumble ??
            throw new InvalidDataException("Weapon.Definition.Rumble cannot be null.");
        AddXString(operations, 0x5b8, rumble.FireRumble,
            rumble.FireRumblePointer.Untyped,
            "Weapon.Definition.Rumble.FireRumble", freeze);
        AddXString(operations, 0x5bc, rumble.MeleeImpactRumble,
            rumble.MeleeImpactRumblePointer.Untyped,
            "Weapon.Definition.Rumble.MeleeImpactRumble", freeze);
        AddProvider(operations, 0x5c0, definition.Tracer,
            definition.TracerPointer.Untyped,
            XAssetType.Tracer, "Weapon.Definition.Tracer");
        FreezeTurretOperations(definition, freeze, operations);
    }

    private static void FreezeProjectileOperations(
        WeaponDef definition,
        LinkAssetFreezeScope freeze,
        ICollection<FrozenOperation> operations)
    {
        WeaponProjectileFields projectile = definition.Projectile ??
            throw new InvalidDataException("Weapon.Definition.Projectile cannot be null.");
        AddProvider(operations, 0x420, projectile.Model,
            projectile.ModelPointer.Untyped,
            XAssetType.XModel, "Weapon.Definition.Projectile.Model");
        RequireExactCount(
            definition.ProjectileEffects,
            2,
            "Weapon.Definition.ProjectileEffects");
        AddProvider(operations, 0x428, definition.ProjectileEffects[0],
            projectile.ExplosionEffectPointer.Untyped,
            XAssetType.Fx, "Weapon.Definition.Projectile.ExplosionEffect");
        AddProvider(operations, 0x42c, definition.ProjectileEffects[1],
            projectile.DudEffectPointer.Untyped,
            XAssetType.Fx, "Weapon.Definition.Projectile.DudEffect");
        AddSoundCell(operations, 0x430, projectile.ExplosionSoundPointer,
            projectile.ExplosionSoundValuePointer, projectile.ExplosionSound,
            "Weapon.Definition.Projectile.ExplosionSound", freeze);
        AddSoundCell(operations, 0x434, projectile.DudSoundPointer,
            projectile.DudSoundValuePointer, projectile.DudSound,
            "Weapon.Definition.Projectile.DudSound", freeze);

        LinkStorageTarget? parallel = FreezeFloatArray(
            projectile.ParallelBounce,
            projectile.ParallelBouncePointer.Untyped,
            WeaponDef.SurfaceCount,
            "Weapon.Definition.Projectile.ParallelBounce",
            freeze);
        if (parallel is { } parallelStorage)
        {
            AddDirect(
                operations,
                0x444,
                parallelStorage,
                "Weapon.Definition.Projectile.ParallelBounce");
        }
        LinkStorageTarget? perpendicular = FreezeFloatArray(
            projectile.PerpendicularBounce,
            projectile.PerpendicularBouncePointer.Untyped,
            WeaponDef.SurfaceCount,
            "Weapon.Definition.Projectile.PerpendicularBounce",
            freeze);
        if (perpendicular is { } perpendicularStorage)
        {
            AddDirect(
                operations,
                0x448,
                perpendicularStorage,
                "Weapon.Definition.Projectile.PerpendicularBounce");
        }

        RequireExactCount(
            definition.ImpactEffects,
            2,
            "Weapon.Definition.ImpactEffects");
        AddProvider(operations, 0x44c, definition.ImpactEffects[0],
            projectile.TrailEffectPointer.Untyped,
            XAssetType.Fx, "Weapon.Definition.Projectile.TrailEffect");
        AddProvider(operations, 0x450, definition.ImpactEffects[1],
            projectile.BeaconEffectPointer.Untyped,
            XAssetType.Fx, "Weapon.Definition.Projectile.BeaconEffect");
        AddProvider(operations, 0x46c, definition.ViewShellEjectEffect,
            projectile.IgnitionEffectPointer.Untyped,
            XAssetType.Fx, "Weapon.Definition.Projectile.IgnitionEffect");
        AddSoundCell(operations, 0x470, projectile.IgnitionSoundPointer,
            projectile.IgnitionSoundValuePointer, projectile.IgnitionSound,
            "Weapon.Definition.Projectile.IgnitionSound", freeze);
    }

    private static void FreezeAccuracyOperations(
        WeaponDef definition,
        WeaponVariantDef variant,
        LinkAssetFreezeScope freeze,
        ICollection<FrozenOperation> operations)
    {
        WeaponAccuracyFields accuracy = definition.Accuracy ??
            throw new InvalidDataException("Weapon.Definition.Accuracy cannot be null.");
        RequireCount(
            variant.AccuracyGraphKnotCount,
            accuracy.GraphKnots.Count,
            "Weapon.Definition.Accuracy.GraphKnots");
        RequireCount(
            variant.OriginalAccuracyGraphKnotCount,
            accuracy.OriginalGraphKnots.Count,
            "Weapon.Definition.Accuracy.OriginalGraphKnots");
        AddXString(operations, 0x50c, accuracy.GraphName0,
            accuracy.GraphName0Pointer.Untyped,
            "Weapon.Definition.Accuracy.GraphName0", freeze);
        LinkStorageTarget? graph = FreezeVec2Array(
            accuracy.GraphKnots,
            accuracy.GraphKnotsPointer.Untyped,
            "Weapon.Definition.Accuracy.GraphKnots",
            freeze);
        if (graph is { } graphStorage)
        {
            AddDirect(
                operations,
                0x514,
                graphStorage,
                "Weapon.Definition.Accuracy.GraphKnots");
        }
        AddXString(operations, 0x510, accuracy.GraphName1,
            accuracy.GraphName1Pointer.Untyped,
            "Weapon.Definition.Accuracy.GraphName1", freeze);
        LinkStorageTarget? original = FreezeVec2Array(
            accuracy.OriginalGraphKnots,
            accuracy.OriginalGraphKnotsPointer.Untyped,
            "Weapon.Definition.Accuracy.OriginalGraphKnots",
            freeze);
        if (original is { } originalStorage)
        {
            AddDirect(
                operations,
                0x518,
                originalStorage,
                "Weapon.Definition.Accuracy.OriginalGraphKnots");
        }
    }

    private static void FreezeTurretOperations(
        WeaponDef definition,
        LinkAssetFreezeScope freeze,
        ICollection<FrozenOperation> operations)
    {
        WeaponTurretFields turret = definition.Turret ??
            throw new InvalidDataException("Weapon.Definition.Turret cannot be null.");
        AddSoundCell(operations, 0x5dc, turret.OverheatSoundPointer,
            turret.OverheatSoundValuePointer, turret.OverheatSound,
            "Weapon.Definition.Turret.OverheatSound", freeze);
        AddProvider(operations, 0x5e0, definition.TurretOverheatEffect,
            turret.OverheatEffectPointer.Untyped,
            XAssetType.Fx, "Weapon.Definition.Turret.OverheatEffect");
        AddXString(operations, 0x5e4, turret.BarrelSpinRumble,
            turret.BarrelSpinRumblePointer.Untyped,
            "Weapon.Definition.Turret.BarrelSpinRumble", freeze);
        AddSoundCell(operations, 0x5f4, turret.BarrelSpinMaxSoundPointer,
            turret.BarrelSpinMaxSoundValuePointer, turret.BarrelSpinMaxSound,
            "Weapon.Definition.Turret.BarrelSpinMaxSound", freeze);
        AddSoundCells(
            operations,
            0x5f8,
            turret.BarrelSpinUpSoundPointers,
            turret.BarrelSpinUpSoundValuePointers,
            turret.BarrelSpinUpSoundNames,
            WeaponDef.TurretBarrelSpinSoundCount,
            "Weapon.Definition.Turret.BarrelSpinUpSounds",
            freeze);
        AddSoundCells(
            operations,
            0x608,
            turret.BarrelSpinDownSoundPointers,
            turret.BarrelSpinDownSoundValuePointers,
            turret.BarrelSpinDownSoundNames,
            WeaponDef.TurretBarrelSpinSoundCount,
            "Weapon.Definition.Turret.BarrelSpinDownSounds",
            freeze);

        WeaponMissileConeSoundFields missile = definition.MissileConeSound ??
            throw new InvalidDataException("Weapon.Definition.MissileConeSound cannot be null.");
        AddSoundCell(operations, 0x618, missile.AliasPointer,
            missile.AliasValuePointer, missile.Alias,
            "Weapon.Definition.MissileConeSound.Alias", freeze);
        AddSoundCell(operations, 0x61c, missile.AliasAtBasePointer,
            missile.AliasAtBaseValuePointer, missile.AliasAtBase,
            "Weapon.Definition.MissileConeSound.AliasAtBase", freeze);
    }

    private static LinkStorageTarget? FreezeProviderTable<TAsset>(
        IReadOnlyList<XPointer<TAsset>> pointers,
        IReadOnlyList<TAsset?> definitions,
        XPointerReference tablePointer,
        int expectedCount,
        XAssetType expectedType,
        string fieldPath,
        LinkAssetFreezeScope freeze)
        where TAsset : BaseAsset
    {
        ArgumentNullException.ThrowIfNull(pointers);
        ArgumentNullException.ThrowIfNull(definitions);
        bool absent = pointers.Count == 0 && definitions.Count == 0 &&
            tablePointer.Type == PointerType.Null;
        if (absent)
            return null;
        RequireExactCount(pointers, expectedCount, $"{fieldPath}.Pointers");
        RequireExactCount(definitions, expectedCount, fieldPath);
        AssetDependency?[] dependencies = new AssetDependency?[expectedCount];
        for (int index = 0; index < expectedCount; index++)
        {
            dependencies[index] = FreezeProviderDependency(
                pointers[index].Untyped,
                definitions[index],
                expectedType,
                $"{fieldPath}[{index}]");
        }

        return freeze.FreezeStorageView(
            tablePointer,
            new byte[checked(expectedCount * sizeof(int))],
            XFileBlockType.LARGE,
            alignment: 4,
            (table, addend) => dependencies
                .Select((dependency, index) => (dependency, index))
                .Where(item => item.dependency is not null)
                .Select(item => (LinkOperation)ProviderOperation(
                    table,
                    checked(addend + item.index * sizeof(int)),
                    item.dependency!.Value)),
            fieldPath,
            allowStandaloneDetach: true);
    }

    private static LinkStorageTarget? FreezeXStringArray(
        IReadOnlyList<XString> pointers,
        IReadOnlyList<string?> values,
        XPointerReference tablePointer,
        int expectedCount,
        string fieldPath,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(pointers);
        ArgumentNullException.ThrowIfNull(values);
        bool absent = pointers.Count == 0 && values.Count == 0 &&
            tablePointer.Type == PointerType.Null;
        if (absent)
            return null;
        RequireExactCount(pointers, expectedCount, $"{fieldPath}.Pointers");
        RequireExactCount(values, expectedCount, fieldPath);
        LinkStorageSymbol?[] texts = new LinkStorageSymbol?[expectedCount];
        for (int index = 0; index < expectedCount; index++)
        {
            texts[index] = FreezeOptionalXString(
                values[index],
                pointers[index].Untyped,
                $"{fieldPath}[{index}]",
                freeze);
        }

        return freeze.FreezeStorageView(
            tablePointer,
            new byte[checked(expectedCount * sizeof(int))],
            XFileBlockType.LARGE,
            alignment: 4,
            (table, addend) => texts
                .SelectMany((text, index) => IndirectXStringOperations(
                    table,
                    checked(addend + index * sizeof(int)),
                    text,
                    values[index],
                    XAssetType.XAnim,
                    $"{fieldPath}[{index}]")),
            fieldPath,
            allowStandaloneDetach: true);
    }

    private static LinkStorageTarget? FreezeScriptStringArray(
        IReadOnlyList<ScriptStringReference> values,
        XPointerReference pointer,
        int expectedCount,
        string fieldPath,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0 && pointer.Type == PointerType.Null)
            return null;
        RequireExactCount(values, expectedCount, fieldPath);
        ScriptStringReference[] frozen = values.ToArray();
        return freeze.FreezeStorageView(
            pointer,
            new byte[checked(expectedCount * sizeof(ushort))],
            XFileBlockType.LARGE,
            alignment: 2,
            (table, addend) => frozen.Select((value, index) =>
                (LinkOperation)new ScriptStringLinkOperation(
                    new LinkStorageCell(
                        table,
                        checked(addend + index * sizeof(ushort))),
                    value,
                    $"{fieldPath}[{index}]")),
            fieldPath,
            allowStandaloneDetach: true);
    }

    private static LinkStorageTarget? FreezeFloatArray(
        IReadOnlyList<float> values,
        XPointerReference pointer,
        int expectedCount,
        string fieldPath,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0 && pointer.Type == PointerType.Null)
            return null;
        RequireExactCount(values, expectedCount, fieldPath);
        var writer = new LinkTemplateWriter(checked(values.Count * sizeof(float)));
        foreach (float value in values)
            WriteSingle(writer, value);
        return freeze.FreezeStorageView(
            pointer,
            writer.Complete(),
            XFileBlockType.LARGE,
            alignment: 4,
            operations: null,
            fieldPath,
            allowStandaloneDetach: true);
    }

    private static LinkStorageTarget? FreezeVec2Array(
        IReadOnlyList<Vec2> values,
        XPointerReference pointer,
        string fieldPath,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0 && pointer.Type == PointerType.Null)
            return null;
        var writer = new LinkTemplateWriter(checked(values.Count * 2 * sizeof(float)));
        foreach (Vec2 value in values)
        {
            WriteSingle(writer, value.a);
            WriteSingle(writer, value.b);
        }
        return freeze.FreezeStorageView(
            pointer,
            writer.Complete(),
            XFileBlockType.LARGE,
            alignment: 4,
            operations: null,
            fieldPath,
            allowStandaloneDetach: true);
    }

    private static LinkStorageTarget? FreezeSoundCellTable(
        IReadOnlyList<XString> pointers,
        IReadOnlyList<XString> valuePointers,
        IReadOnlyList<string?> values,
        XPointerReference tablePointer,
        int expectedCount,
        string fieldPath,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(pointers);
        ArgumentNullException.ThrowIfNull(valuePointers);
        ArgumentNullException.ThrowIfNull(values);
        bool absent = pointers.Count == 0 && valuePointers.Count == 0 &&
            values.Count == 0 && tablePointer.Type == PointerType.Null;
        if (absent)
            return null;
        RequireExactCount(pointers, expectedCount, $"{fieldPath}.Pointers");
        RequireExactCount(valuePointers, expectedCount, $"{fieldPath}.ValuePointers");
        RequireExactCount(values, expectedCount, fieldPath);
        LinkStorageTarget?[] cells = new LinkStorageTarget?[expectedCount];
        for (int index = 0; index < expectedCount; index++)
        {
            cells[index] = FreezeSoundCell(
                pointers[index],
                valuePointers[index],
                values[index],
                $"{fieldPath}[{index}]",
                freeze);
        }

        return freeze.FreezeStorageView(
            tablePointer,
            new byte[checked(expectedCount * sizeof(int))],
            XFileBlockType.LARGE,
            alignment: 4,
            (table, addend) => cells
                .Select((cell, index) => (cell, index))
                .Where(item => item.cell is not null)
                .Select(item => (LinkOperation)Direct(
                    table,
                    checked(addend + item.index * sizeof(int)),
                    item.cell!.Value,
                    $"{fieldPath}[{item.index}]")),
            fieldPath,
            allowStandaloneDetach: true);
    }

    private static void AddSoundCells(
        ICollection<FrozenOperation> operations,
        int firstOffset,
        IReadOnlyList<XString> pointers,
        IReadOnlyList<XString> valuePointers,
        IReadOnlyList<string?> values,
        int expectedCount,
        string fieldPath,
        LinkAssetFreezeScope freeze)
    {
        RequireExactCount(pointers, expectedCount, $"{fieldPath}.Pointers");
        RequireExactCount(valuePointers, expectedCount, $"{fieldPath}.ValuePointers");
        RequireExactCount(values, expectedCount, fieldPath);
        for (int index = 0; index < expectedCount; index++)
        {
            AddSoundCell(
                operations,
                checked(firstOffset + index * sizeof(int)),
                pointers[index],
                valuePointers[index],
                values[index],
                $"{fieldPath}[{index}]",
                freeze);
        }
    }

    private static void AddSoundCell(
        ICollection<FrozenOperation> operations,
        int offset,
        XString pointer,
        XString valuePointer,
        string? value,
        string fieldPath,
        LinkAssetFreezeScope freeze)
    {
        LinkStorageTarget? cell = FreezeSoundCell(
            pointer,
            valuePointer,
            value,
            fieldPath,
            freeze);
        if (cell is { } target)
            AddDirect(operations, offset, target, fieldPath);
    }

    private static LinkStorageTarget? FreezeSoundCell(
        XString pointer,
        XString valuePointer,
        string? value,
        string fieldPath,
        LinkAssetFreezeScope freeze)
    {
        bool present = pointer.Type != PointerType.Null ||
            valuePointer.Type != PointerType.Null || value is not null;
        if (!present)
            return null;
        LinkStorageSymbol? text = FreezeOptionalXString(
            value,
            valuePointer.Untyped,
            $"{fieldPath}.Value",
            freeze);
        return freeze.FreezeStorage(
            pointer.Untyped,
            new byte[sizeof(int)],
            XFileBlockType.LARGE,
            alignment: 4,
            (cell, addend) => IndirectXStringOperations(
                cell,
                addend,
                text,
                value,
                XAssetType.Sound,
                $"{fieldPath}.Value"),
            fieldPath);
    }

    private static void FreezeScriptMap(
        ICollection<FrozenOperation> operations,
        int offset,
        IReadOnlyList<ScriptStringReference> values,
        XPointerReference pointer,
        string fieldPath,
        LinkAssetFreezeScope freeze)
    {
        LinkStorageTarget? storage = FreezeScriptStringArray(
            values,
            pointer,
            WeaponDef.NoteTrackMapCount,
            fieldPath,
            freeze);
        if (storage is { } target)
            AddDirect(operations, offset, target, fieldPath);
    }

    private static void AddXString(
        ICollection<FrozenOperation> operations,
        int offset,
        string? value,
        XPointerReference pointer,
        string fieldPath,
        LinkAssetFreezeScope freeze)
    {
        LinkStorageSymbol? text = FreezeOptionalXString(
            value,
            pointer,
            fieldPath,
            freeze);
        if (text is null)
            return;
        operations.Add((owner, addend) => XStringOperation(
            owner,
            checked(addend + offset),
            text,
            fieldPath));
    }

    private static LinkStorageSymbol? FreezeOptionalXString(
        string? value,
        XPointerReference pointer,
        string fieldPath,
        LinkAssetFreezeScope freeze)
    {
        if (value is null)
        {
            if (pointer.Type != PointerType.Null)
            {
                throw new NotSupportedException(
                    $"{fieldPath} retains a non-null XString pointer without semantic text.");
            }
            return null;
        }
        return freeze.FreezeRequiredXString(value, pointer, fieldPath);
    }

    private static void AddProviders<TAsset>(
        ICollection<FrozenOperation> operations,
        int firstOffset,
        IReadOnlyList<XPointer<TAsset>> pointers,
        IReadOnlyList<TAsset?> definitions,
        int expectedCount,
        XAssetType expectedType,
        string fieldPath)
        where TAsset : BaseAsset
    {
        RequireExactCount(pointers, expectedCount, $"{fieldPath}.Pointers");
        RequireExactCount(definitions, expectedCount, fieldPath);
        for (int index = 0; index < expectedCount; index++)
        {
            AddProvider(
                operations,
                checked(firstOffset + index * sizeof(int)),
                definitions[index],
                pointers[index].Untyped,
                expectedType,
                $"{fieldPath}[{index}]");
        }
    }

    private static void AddProvider(
        ICollection<FrozenOperation> operations,
        int offset,
        BaseAsset? definition,
        XPointerReference pointer,
        XAssetType expectedType,
        string fieldPath)
    {
        AssetDependency? dependency = FreezeProviderDependency(
            pointer,
            definition,
            expectedType,
            fieldPath);
        if (dependency is not { } frozen)
            return;
        operations.Add((owner, addend) => ProviderOperation(
            owner,
            checked(addend + offset),
            frozen));
    }

    private static void AddDirect(
        ICollection<FrozenOperation> operations,
        int offset,
        LinkStorageTarget target,
        string fieldPath) =>
        operations.Add((owner, addend) => Direct(
            owner,
            checked(addend + offset),
            target,
            fieldPath));

    private static DirectStorageLinkOperation Direct(
        LinkStorageSymbol owner,
        int offset,
        LinkStorageTarget target,
        string fieldPath) =>
        new(
            new LinkStorageCell(owner, offset),
            target.View,
            target.CanMaterializeRoot,
            fieldPath);

    private static void RequireExactCount<T>(
        IReadOnlyList<T> values,
        int expected,
        string fieldPath)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count != expected)
        {
            throw new InvalidDataException(
                $"{fieldPath} contains {values.Count} row(s); exactly {expected} are required.");
        }
    }

    private static void RequireCount(
        ushort expected,
        int actual,
        string fieldPath)
    {
        if (actual != expected)
        {
            throw new InvalidDataException(
                $"{fieldPath} contains {actual} row(s), but its native count requires {expected}.");
        }
    }

    private static void ValidateReferenceShape(WeaponVariantDef variant)
    {
        if (variant.DefinitionPointer.Raw != 0 ||
            variant.Definition is not null ||
            variant.DisplayNamePointer.Raw != 0 ||
            variant.DisplayName is not null ||
            variant.HideTagsPointer.Raw != 0 ||
            variant.HideTags.Count != 0 ||
            variant.AnimationNamesPointer.Raw != 0 ||
            variant.AnimationNamePointers.Count != 0 ||
            variant.AnimationNames.Count != 0 ||
            variant.AlternateWeaponNamePointer.Raw != 0 ||
            variant.AlternateWeaponName is not null ||
            variant.KillIconPointer.Raw != 0 ||
            variant.KillIcon is not null ||
            variant.DpadIconPointer.Raw != 0 ||
            variant.DpadIcon is not null ||
            variant.AccuracyGraphKnotsPointer.Raw != 0 ||
            variant.AccuracyGraphKnots.Count != 0 ||
            variant.OriginalAccuracyGraphKnotsPointer.Raw != 0 ||
            variant.OriginalAccuracyGraphKnots.Count != 0 ||
            BuildVariantTemplate(variant).Any(value => value != 0))
        {
            throw new InvalidDataException(
                "A comma-prefixed Weapon provider must have a zeroed reference body.");
        }
    }

    private static void ValidateDefinitionEnums(WeaponDef definition)
    {
        WeaponReticleFields reticle = definition.Reticle ??
            throw new InvalidDataException("Weapon.Definition.Reticle cannot be null.");
        WeaponIconPointers icons = definition.Icons ??
            throw new InvalidDataException("Weapon.Definition.Icons cannot be null.");
        WeaponOverlayFields overlay = definition.Overlay ??
            throw new InvalidDataException("Weapon.Definition.Overlay cannot be null.");
        WeaponProjectileFields projectile = definition.Projectile ??
            throw new InvalidDataException("Weapon.Definition.Projectile cannot be null.");
        RequireDefined(definition.WeaponType, "Weapon.Definition.WeaponType");
        RequireDefined(definition.WeaponClass, "Weapon.Definition.WeaponClass");
        RequireDefined(definition.PenetrateType, "Weapon.Definition.PenetrateType");
        RequireDefined(definition.InventoryType, "Weapon.Definition.InventoryType");
        RequireDefined(definition.FireType, "Weapon.Definition.FireType");
        RequireDefined(definition.OffhandClass, "Weapon.Definition.OffhandClass");
        RequireDefined(definition.Stance, "Weapon.Definition.Stance");
        RequireDefined(
            reticle.ActiveType,
            "Weapon.Definition.Reticle.ActiveType");
        RequireDefined(
            icons.AmmoCounterClip,
            "Weapon.Definition.Icons.AmmoCounterClip");
        RequireDefined(
            overlay.Reticle,
            "Weapon.Definition.Overlay.Reticle");
        RequireDefined(
            overlay.Interface,
            "Weapon.Definition.Overlay.Interface");
        RequireDefined(
            projectile.Explosion,
            "Weapon.Definition.Projectile.Explosion");
        RequireDefined(
            projectile.Stickiness,
            "Weapon.Definition.Projectile.Stickiness");
        RequireDefined(
            projectile.GuidedMissileType,
            "Weapon.Definition.Projectile.GuidedMissileType");
    }

    private static void RequireDefined<TEnum>(TEnum value, string fieldPath)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
            throw new InvalidDataException($"{fieldPath} has unsupported value {value}.");
    }

    private static byte[] BuildVariantTemplate(WeaponVariantDef value)
    {
        var writer = new LinkTemplateWriter(WeaponVariantDef.SerializedSize);
        writer.Skip(sizeof(int) * 5);
        WriteSingle(writer, value.AdsZoomFov);
        writer.WriteInt32(value.AdsTransitionInTime);
        writer.WriteInt32(value.AdsTransitionOutTime);
        writer.WriteInt32(value.ClipSize);
        writer.WriteInt32(value.ImpactType);
        writer.WriteInt32(value.FireTime);
        writer.WriteInt32(value.DpadIconRatio);
        WriteSingle(writer, value.PenetrateMultiplier);
        WriteSingle(writer, value.AdsViewKickCenterSpeed);
        WriteSingle(writer, value.HipViewKickCenterSpeed);
        writer.Skip(sizeof(int));
        writer.WriteUInt32(value.AlternateWeaponIndex);
        writer.WriteInt32(value.AlternateRaiseTime);
        writer.Skip(sizeof(int) * 2);
        writer.WriteInt32(value.DropAmmoMin);
        writer.WriteInt32(value.FirstRaiseTime);
        writer.WriteInt32(value.DropAmmoMax);
        WriteSingle(writer, value.AdsDofStart);
        WriteSingle(writer, value.AdsDofEnd);
        writer.WriteUInt16(value.AccuracyGraphKnotCount);
        writer.WriteUInt16(value.OriginalAccuracyGraphKnotCount);
        writer.Skip(sizeof(int) * 2);
        writer.WriteByte(value.MotionTracker);
        writer.WriteByte(value.Enhanced);
        writer.WriteByte(value.DpadIconShowsAmmo);
        writer.WriteByte(value.Padding73);
        return writer.Complete();
    }

    private static byte[] BuildDefinitionTemplate(WeaponDef value)
    {
        WeaponReticleFields reticle = value.Reticle ??
            throw new InvalidDataException("Weapon.Definition.Reticle cannot be null.");
        WeaponViewMovementFields view = value.ViewMovement ??
            throw new InvalidDataException("Weapon.Definition.ViewMovement cannot be null.");
        WeaponPositionalMovementFields positional = value.PositionalMovement ??
            throw new InvalidDataException("Weapon.Definition.PositionalMovement cannot be null.");
        WeaponIconPointers icons = value.Icons ??
            throw new InvalidDataException("Weapon.Definition.Icons cannot be null.");
        WeaponAmmoFields ammo = value.Ammo ??
            throw new InvalidDataException("Weapon.Definition.Ammo cannot be null.");
        WeaponTimingFields timing = value.Timing ??
            throw new InvalidDataException("Weapon.Definition.Timing cannot be null.");
        WeaponAimMovementTuningFields aim = value.AimMovementTuning ??
            throw new InvalidDataException("Weapon.Definition.AimMovementTuning cannot be null.");
        WeaponOverlayFields overlay = value.Overlay ??
            throw new InvalidDataException("Weapon.Definition.Overlay cannot be null.");
        WeaponAdsViewAndSpreadFields ads = value.AdsViewAndSpread ??
            throw new InvalidDataException("Weapon.Definition.AdsViewAndSpread cannot be null.");
        WeaponPhysicsFields physics = value.Physics ??
            throw new InvalidDataException("Weapon.Definition.Physics cannot be null.");
        WeaponProjectileFields projectile = value.Projectile ??
            throw new InvalidDataException("Weapon.Definition.Projectile cannot be null.");
        WeaponAccuracyFields accuracy = value.Accuracy ??
            throw new InvalidDataException("Weapon.Definition.Accuracy cannot be null.");
        WeaponTurnSpeedAndRangeFields turn = value.TurnSpeedAndRange ??
            throw new InvalidDataException("Weapon.Definition.TurnSpeedAndRange cannot be null.");
        WeaponHintFields hints = value.Hints ??
            throw new InvalidDataException("Weapon.Definition.Hints cannot be null.");
        WeaponRumbleFields rumble = value.Rumble ??
            throw new InvalidDataException("Weapon.Definition.Rumble cannot be null.");
        WeaponTurretFields turret = value.Turret ??
            throw new InvalidDataException("Weapon.Definition.Turret cannot be null.");
        WeaponMissileConeSoundFields missile = value.MissileConeSound ??
            throw new InvalidDataException("Weapon.Definition.MissileConeSound cannot be null.");
        WeaponTailFlags tail = value.TailFlags ??
            throw new InvalidDataException("Weapon.Definition.TailFlags cannot be null.");

        var writer = new LinkTemplateWriter(WeaponDef.SerializedSize);
        writer.Skip(sizeof(int) * 10);
        writer.WriteInt32(value.PlayerAnimType);
        writer.WriteInt32((int)value.WeaponType);
        writer.WriteInt32((int)value.WeaponClass);
        writer.WriteInt32((int)value.PenetrateType);
        writer.WriteInt32((int)value.InventoryType);
        writer.WriteInt32((int)value.FireType);
        writer.WriteInt32((int)value.OffhandClass);
        writer.WriteInt32((int)value.Stance);
        writer.Skip(sizeof(int) * 2);
        writer.Skip(sizeof(int) * WeaponDef.WeaponSoundAliasCount);
        writer.Skip(sizeof(int));
        writer.Skip(sizeof(int) * 4);
        writer.Skip(sizeof(int) * 2);
        WriteReticle(writer, reticle);
        WriteView(writer, view);
        WritePositional(writer, positional);
        writer.Skip(sizeof(int));
        writer.Skip(sizeof(int) * 4);
        WriteIcons(writer, icons);
        WriteAmmo(writer, ammo);
        WriteTiming(writer, timing);
        WriteAim(writer, aim);
        writer.Skip(sizeof(int) * 4);
        writer.WriteInt32((int)overlay.Reticle);
        writer.WriteInt32((int)overlay.Interface);
        writer.WriteInt32(overlay.Width);
        writer.WriteInt32(overlay.Height);
        writer.WriteInt32(overlay.WidthSplitscreen);
        writer.WriteInt32(overlay.HeightSplitscreen);
        WriteAds(writer, ads);
        writer.Skip(sizeof(int));
        WritePhysics(writer, physics);
        WriteProjectile(writer, projectile);
        WriteAccuracy(writer, accuracy);
        WriteTurn(writer, turn);
        WriteHints(writer, hints);
        writer.Skip(sizeof(int));
        WriteSingle(writer, value.OOPosAnimLength);
        WriteSingle(writer, value.MinDamage);
        writer.WriteInt32(value.MinPlayerDamage);
        WriteSingle(writer, value.MaxDamageRange);
        WriteSingle(writer, value.MinDamageRange);
        WriteSingle(writer, value.DestabilizationRateTime);
        WriteSingle(writer, value.DestabilizationCurvatureMax);
        WriteSingle(writer, value.DestabilizeDistance);
        writer.WriteInt32(value.DestabilizeDistanceToTimeScale);
        writer.Skip(sizeof(int) * 4);
        WriteSingle(writer, value.TurretScopeZoomRate);
        WriteSingle(writer, value.TurretScopeZoomMin);
        WriteSingle(writer, value.TurretScopeZoomMax);
        WriteSingle(writer, value.TurretOverheatUpRate);
        WriteSingle(writer, value.TurretOverheatDownRate);
        WriteSingle(writer, value.TurretOverheatPenalty);
        WriteTurret(writer, turret);
        WriteMissile(writer, missile);
        WriteTail(writer, tail);
        return writer.Complete();
    }

    private static void WriteReticle(
        LinkTemplateWriter writer,
        WeaponReticleFields value)
    {
        writer.WriteInt32(value.CenterSize);
        writer.WriteInt32(value.SideSize);
        writer.WriteInt32(value.MinOffset);
        writer.WriteInt32((int)value.ActiveType);
    }

    private static void WriteView(
        LinkTemplateWriter writer,
        WeaponViewMovementFields value)
    {
        WriteVec3(writer, value.StandMove);
        WriteVec3(writer, value.StandRotation);
        WriteVec3(writer, value.StrafeMove);
        WriteVec3(writer, value.StrafeRotation);
        WriteVec3(writer, value.DuckedOffset);
        WriteVec3(writer, value.DuckedMove);
        WriteVec3(writer, value.DuckedRotation);
        WriteVec3(writer, value.ProneOffset);
        WriteVec3(writer, value.ProneMove);
        WriteVec3(writer, value.ProneRotation);
    }

    private static void WritePositional(
        LinkTemplateWriter writer,
        WeaponPositionalMovementFields value) =>
        WriteSingles(
            writer,
            value.PositionMoveRate,
            value.PositionProneMoveRate,
            value.StandMoveMinSpeed,
            value.DuckedMoveMinSpeed,
            value.ProneMoveMinSpeed,
            value.PositionRotationRate,
            value.PositionProneRotationRate,
            value.StandRotationMinSpeed,
            value.DuckedRotationMinSpeed,
            value.ProneRotationMinSpeed);

    private static void WriteIcons(
        LinkTemplateWriter writer,
        WeaponIconPointers value)
    {
        writer.Skip(sizeof(int));
        writer.WriteInt32(value.HudIconRatio);
        writer.Skip(sizeof(int));
        writer.WriteInt32(value.PickupIconRatio);
        writer.Skip(sizeof(int));
        writer.WriteInt32(value.AmmoCounterIconRatio);
        writer.WriteInt32((int)value.AmmoCounterClip);
        writer.WriteInt32(value.StartAmmo);
    }

    private static void WriteAmmo(
        LinkTemplateWriter writer,
        WeaponAmmoFields value)
    {
        writer.Skip(sizeof(int));
        writer.WriteInt32(value.AmmoIndex);
        writer.Skip(sizeof(int));
        writer.WriteInt32(value.ClipIndex);
        writer.WriteInt32(value.MaxAmmo);
        writer.WriteInt32(value.ShotCount);
        writer.Skip(sizeof(int));
        writer.WriteInt32(value.SharedAmmoCapIndex);
        writer.WriteInt32(value.SharedAmmoCap);
        writer.WriteInt32(value.Damage);
        writer.WriteInt32(value.PlayerDamage);
        writer.WriteInt32(value.MeleeDamage);
        writer.WriteInt32(value.DamageType);
    }

    private static void WriteTiming(
        LinkTemplateWriter writer,
        WeaponTimingFields value)
    {
        WriteInts(
            writer,
            value.FireDelay,
            value.MeleeDelay,
            value.MeleeChargeDelay,
            value.DetonateDelay,
            value.RechamberTime,
            value.RechamberTimeOneHanded,
            value.RechamberBoltTime,
            value.HoldFireTime,
            value.DetonateTime,
            value.MeleeTime,
            value.MeleeChargeTime,
            value.ReloadTime,
            value.ReloadShowRocketTime,
            value.ReloadEmptyTime,
            value.ReloadAddTime,
            value.ReloadStartTime,
            value.ReloadStartAddTime,
            value.ReloadEndTime,
            value.DropTime,
            value.RaiseTime,
            value.AltDropTime,
            value.QuickDropTime,
            value.QuickRaiseTime,
            value.BreachRaiseTime,
            value.EmptyRaiseTime,
            value.EmptyDropTime,
            value.SprintInTime,
            value.SprintLoopTime,
            value.SprintOutTime,
            value.StunnedTimeBegin,
            value.StunnedTimeLoop,
            value.StunnedTimeEnd,
            value.NightVisionWearTime,
            value.NightVisionWearTimeFadeOutEnd,
            value.NightVisionWearTimePowerUp,
            value.NightVisionRemoveTime,
            value.NightVisionRemoveTimePowerDown,
            value.NightVisionRemoveTimeFadeInStart,
            value.FuseTime,
            value.AiFuseTime);
    }

    private static void WriteAim(
        LinkTemplateWriter writer,
        WeaponAimMovementTuningFields value) =>
        WriteSingles(
            writer,
            value.AutoAimRange,
            value.AimAssistRange,
            value.AimAssistRangeAds,
            value.AimPadding,
            value.EnemyCrosshairRange,
            value.MoveSpeedScale,
            value.AdsMoveSpeedScale,
            value.SprintDurationScale,
            value.AdsZoomInFraction,
            value.AdsZoomOutFraction);

    private static void WriteAds(
        LinkTemplateWriter writer,
        WeaponAdsViewAndSpreadFields value) =>
        WriteSingles(
            writer,
            value.AdsBobFactor,
            value.AdsViewBobMultiplier,
            value.HipSpreadStandMin,
            value.HipSpreadDuckedMin,
            value.HipSpreadProneMin,
            value.HipSpreadStandMax,
            value.HipSpreadDuckedMax,
            value.HipSpreadProneMax,
            value.HipSpreadDecayRate,
            value.HipSpreadFireAdd,
            value.HipSpreadTurnAdd,
            value.HipSpreadMoveAdd,
            value.HipSpreadDuckedDecay,
            value.HipSpreadProneDecay,
            value.HipReticleSidePosition,
            value.AdsIdleAmount,
            value.HipIdleAmount,
            value.AdsIdleSpeed,
            value.HipIdleSpeed,
            value.IdleCrouchFactor,
            value.IdleProneFactor,
            value.GunMaxPitch,
            value.GunMaxYaw,
            value.SwayMaxAngle,
            value.SwayLerpSpeed,
            value.SwayPitchScale,
            value.SwayYawScale,
            value.SwayHorizontalScale,
            value.SwayVerticalScale,
            value.SwayShellShockScale,
            value.AdsSwayMaxAngle,
            value.AdsSwayLerpSpeed,
            value.AdsSwayPitchScale,
            value.AdsSwayYawScale,
            value.AdsSwayHorizontalScale,
            value.AdsSwayVerticalScale,
            value.AdsViewErrorMin,
            value.AdsViewErrorMax);

    private static void WritePhysics(
        LinkTemplateWriter writer,
        WeaponPhysicsFields value)
    {
        WriteSingle(writer, value.DualWieldViewModelOffset);
        WriteInts(
            writer,
            value.KillIconRatio,
            value.ReloadAmmoAdd,
            value.ReloadStartAdd,
            value.AmmoDropStockMin);
        WriteSingle(writer, value.AmmoDropClipPercentMin);
        WriteSingle(writer, value.AmmoDropClipPercentMax);
        WriteInts(
            writer,
            value.ExplosionRadius,
            value.ExplosionRadiusMin,
            value.ExplosionInnerDamage,
            value.ExplosionOuterDamage);
        WriteSingle(writer, value.DamageConeAngle);
        WriteSingle(writer, value.BulletExplosionDamageMultiplier);
        WriteSingle(writer, value.BulletExplosionRadiusMultiplier);
        WriteInts(
            writer,
            value.ProjectileSpeed,
            value.ProjectileSpeedUp,
            value.ProjectileSpeedForward,
            value.ProjectileActivateDistance,
            value.ProjectileLifetime,
            value.TimeToAccelerate);
        WriteSingle(writer, value.ProjectileCurvature);
    }

    private static void WriteProjectile(
        LinkTemplateWriter writer,
        WeaponProjectileFields value)
    {
        writer.Skip(sizeof(int));
        writer.WriteInt32((int)value.Explosion);
        writer.Skip(sizeof(int) * 4);
        writer.WriteInt32((int)value.Stickiness);
        writer.WriteInt32(value.LowAmmoWarningThreshold);
        WriteSingle(writer, value.RicochetChance);
        writer.Skip(sizeof(int) * 4);
        WriteVec3(writer, value.ProjectileColor);
        writer.WriteInt32((int)value.GuidedMissileType);
        WriteSingle(writer, value.MaxSteeringAcceleration);
        writer.WriteInt32(value.IgnitionDelay);
        writer.Skip(sizeof(int) * 2);
        WriteSingle(writer, value.AdsAimPitch);
        WriteSingle(writer, value.AdsCrosshairInFraction);
        WriteSingle(writer, value.AdsCrosshairOutFraction);
        WriteKick(writer, value.GunKickAndDistance ??
            throw new InvalidDataException(
                "Weapon.Definition.Projectile.GunKickAndDistance cannot be null."));
    }

    private static void WriteKick(
        LinkTemplateWriter writer,
        WeaponGunKickAndDistanceFields value)
    {
        writer.WriteInt32(value.AdsGunKickReducedKickBullets);
        WriteSingles(
            writer,
            value.AdsGunKickReducedKickPercent,
            value.AdsGunKickPitchMin,
            value.AdsGunKickPitchMax,
            value.AdsGunKickYawMin,
            value.AdsGunKickYawMax,
            value.AdsGunKickAcceleration,
            value.AdsGunKickSpeedMax,
            value.AdsGunKickSpeedDecay,
            value.AdsGunKickStaticDecay,
            value.AdsViewKickPitchMin,
            value.AdsViewKickPitchMax,
            value.AdsViewKickYawMin,
            value.AdsViewKickYawMax,
            value.AdsViewScatterMin,
            value.AdsViewScatterMax,
            value.AdsSpread);
        writer.WriteInt32(value.HipGunKickReducedKickBullets);
        WriteSingles(
            writer,
            value.HipGunKickReducedKickPercent,
            value.HipGunKickPitchMin,
            value.HipGunKickPitchMax,
            value.HipGunKickYawMin,
            value.HipGunKickYawMax,
            value.HipGunKickAcceleration,
            value.HipGunKickSpeedMax,
            value.HipGunKickSpeedDecay,
            value.HipGunKickStaticDecay,
            value.HipViewKickPitchMin,
            value.HipViewKickPitchMax,
            value.HipViewKickYawMin,
            value.HipViewKickYawMax,
            value.HipViewScatterMin,
            value.HipViewScatterMax,
            value.FightDistance,
            value.MaxDistance);
    }

    private static void WriteAccuracy(
        LinkTemplateWriter writer,
        WeaponAccuracyFields value)
    {
        writer.Skip(sizeof(int) * 4);
        writer.WriteUInt16(value.LocalGraphKnotCount);
        writer.WriteUInt16(value.LocalOriginalGraphKnotCount);
        writer.WriteInt32(value.AnimationNotifyComparison);
        WriteSingles(
            writer,
            value.LeftArc,
            value.RightArc,
            value.TopArc,
            value.BottomArc,
            value.Accuracy,
            value.AiSpread,
            value.PlayerSpread);
    }

    private static void WriteTurn(
        LinkTemplateWriter writer,
        WeaponTurnSpeedAndRangeFields value) =>
        WriteSingles(
            writer,
            value.MinTurnSpeed,
            value.MaxTurnSpeed,
            value.PitchConvergenceTime,
            value.YawConvergenceTime,
            value.SuppressTime,
            value.MaxRange,
            value.AnimationHorizontalRotateIncrement,
            value.PlayerPositionDistance,
            value.ScanSpeed,
            value.ScanAcceleration);

    private static void WriteHints(
        LinkTemplateWriter writer,
        WeaponHintFields value)
    {
        writer.Skip(sizeof(int) * 2);
        writer.WriteInt32(value.UseHintStringIndex);
        writer.WriteInt32(value.DropHintStringIndex);
        WriteSingle(writer, value.HorizontalViewJitter);
        WriteSingle(writer, value.VerticalViewJitter);
        WriteSingle(writer, value.ScanSpeed);
        WriteSingle(writer, value.ScanAcceleration);
        writer.WriteInt32(value.ScanPauseTime);
    }

    private static void WriteTurret(
        LinkTemplateWriter writer,
        WeaponTurretFields value)
    {
        writer.Skip(sizeof(int) * 3);
        WriteSingle(writer, value.BarrelSpinSpeed);
        WriteSingle(writer, value.BarrelSpinUpTime);
        WriteSingle(writer, value.BarrelSpinDownTime);
        writer.Skip(sizeof(int));
        writer.Skip(sizeof(int) * WeaponDef.TurretBarrelSpinSoundCount);
        writer.Skip(sizeof(int) * WeaponDef.TurretBarrelSpinSoundCount);
    }

    private static void WriteMissile(
        LinkTemplateWriter writer,
        WeaponMissileConeSoundFields value)
    {
        writer.Skip(sizeof(int) * 2);
        WriteSingles(
            writer,
            value.RadiusAtTop,
            value.RadiusAtBase,
            value.Height,
            value.OriginOffset,
            value.VolumeScaleAtCore,
            value.VolumeScaleAtEdge,
            value.VolumeScaleCoreSize,
            value.PitchAtTop,
            value.PitchAtBottom,
            value.PitchTopSize,
            value.PitchBottomSize,
            value.CrossfadeTopSize,
            value.CrossfadeBottomSize);
    }

    private static void WriteTail(
        LinkTemplateWriter writer,
        WeaponTailFlags value)
    {
        writer.WriteByte(value.SharedAmmo);
        writer.WriteByte(value.LockonSupported);
        writer.WriteByte(value.RequireLockonToFire);
        writer.WriteByte(value.BigExplosion);
        writer.WriteByte(value.NoAdsWhenMagEmpty);
        writer.WriteByte(value.AvoidDropCleanup);
        writer.WriteByte(value.InheritsPerks);
        writer.WriteByte(value.CrosshairColorChange);
        writer.WriteByte(value.RifleBullet);
        writer.WriteByte(value.ArmorPiercing);
        writer.WriteByte(value.BoltAction);
        writer.WriteByte(value.AimDownSight);
        writer.WriteByte(value.RechamberWhileAds);
        writer.WriteByte(value.BulletExplosiveDamage);
        writer.WriteByte(value.CookOffHold);
        writer.WriteByte(value.ClipOnly);
        writer.WriteByte(value.NoAmmoPickup);
        writer.WriteByte(value.AdsFireOnly);
        writer.WriteByte(value.CancelAutoHolsterWhenEmpty);
        writer.WriteByte(value.DisableSwitchToWhenEmpty);
        writer.WriteByte(value.SuppressAmmoReserveDisplay);
        writer.WriteByte(value.LaserSightDuringNightvision);
        writer.WriteByte(value.MarkableViewmodel);
        writer.WriteByte(value.NoDualWield);
        writer.WriteByte(value.FlipKillIcon);
        writer.WriteByte(value.NoPartialReload);
        writer.WriteByte(value.SegmentedReload);
        writer.WriteByte(value.BlocksProne);
        writer.WriteByte(value.Silenced);
        writer.WriteByte(value.IsRollingGrenade);
        writer.WriteByte(value.ProjectileExplosionEffectForceNormalUp);
        writer.WriteByte(value.ProjectileImpactExplode);
        writer.WriteByte(value.StickToPlayers);
        writer.WriteByte(value.HasDetonator);
        writer.WriteByte(value.DisableFiring);
        writer.WriteByte(value.TimedDetonation);
        writer.WriteByte(value.Rotate);
        writer.WriteByte(value.HoldButtonToThrow);
        writer.WriteByte(value.FreezeMovementWhenFiring);
        writer.WriteByte(value.ThermalScope);
        writer.WriteByte(value.AltModeSameWeapon);
        writer.WriteByte(value.TurretBarrelSpinEnabled);
        writer.WriteByte(value.MissileConeSoundEnabled);
        writer.WriteByte(value.MissileConeSoundPitchShiftEnabled);
        writer.WriteByte(value.MissileConeSoundCrossfadeEnabled);
        writer.WriteByte(value.OffhandHoldIsCancelable);
        writer.WriteUInt16(value.ReservedPadding);
    }

    private static void WriteVec3(LinkTemplateWriter writer, Vec3 value)
    {
        WriteSingle(writer, value.X);
        WriteSingle(writer, value.Y);
        WriteSingle(writer, value.Z);
    }

    private static void WriteSingle(LinkTemplateWriter writer, float value) =>
        writer.WriteInt32(BitConverter.SingleToInt32Bits(value));

    private static void WriteSingles(
        LinkTemplateWriter writer,
        params float[] values)
    {
        foreach (float value in values)
            WriteSingle(writer, value);
    }

    private static void WriteInts(
        LinkTemplateWriter writer,
        params int[] values)
    {
        foreach (int value in values)
            writer.WriteInt32(value);
    }
}
