using IW4.Game.Assets;
using IW4.Game.Assets.Fx;
using IW4.Game.Assets.Font;
using IW4.Game.Assets.LightDef;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.Menu;
using IW4.Game.Assets.Sound;
using IW4.Game.Assets.TechniqueSet;
using IW4.Game.Assets.Tracer;
using IW4.Game.Assets.Vehicle;
using IW4.Game.Assets.Weapon;
using IW4.Game.Assets.XModel;
using IW4.Game.Zone;
using IW4.Linker.Contracts;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Workbench.Composition;

internal static partial class SourceAssetDumpOperation
{
    private static BaseAsset[] CollectAssets(
        IReadOnlyList<BaseAsset> roots,
        FastFileWorkspace workspace,
        CancellationToken cancellationToken,
        List<SourceAssetDumpFailure> failures)
    {
        var selected = new Dictionary<AssetKey, BaseAsset>();
        var pending = new Queue<BaseAsset>();
        var providers = new Dictionary<AssetKey, BaseAsset>();

        if (!workspace.IsBlank)
        {
            foreach (var slot in workspace.LoadedZone.Context.AssetPool.Slots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var provider = slot.ActiveProvider;
                if (provider.IsReferencePlaceholder ||
                    !SupportedAssetTypes.Contains(provider.AssetType))
                    continue;
                try
                {
                    providers.TryAdd(AssetKey.FromDefinition(provider.Asset), provider.Asset);
                }
                catch (ArgumentException)
                {
                    // An unrelated malformed pool definition is not part of
                    // this dump; selected roots and dependencies report theirs.
                }
            }
        }

        // Reserve every applied root before visiting any nested reference. A
        // nested provider can still point at an older definition of that root.
        foreach (BaseAsset root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryKey(root, failures, out AssetKey key) ||
                !SupportedAssetTypes.Contains(root.SerializedAssetType) ||
                !selected.TryAdd(key, root))
                continue;

            pending.Enqueue(root);
        }

        while (pending.TryDequeue(out BaseAsset? asset))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (BaseAsset dependency in DirectDependencies(asset))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!SupportedAssetTypes.Contains(dependency.SerializedAssetType) ||
                    !TryKey(dependency, failures, out AssetKey key) ||
                    selected.ContainsKey(key))
                    continue;

                BaseAsset selectedDependency = dependency;
                if (dependency.SerializedAssetName is { Length: > 0 } wireName &&
                    wireName[0] == ',')
                {
                    if (!providers.TryGetValue(key, out BaseAsset? fullDefinition))
                    {
                        failures.Add(new SourceAssetDumpFailure(
                            asset.SerializedAssetType,
                            asset.SerializedAssetName ?? "<unnamed>",
                            $"Referenced {dependency.SerializedAssetType} '{wireName}' has no loaded full definition."));
                        continue;
                    }
                    selectedDependency = fullDefinition;
                }
                selected.Add(key, selectedDependency);
                pending.Enqueue(selectedDependency);
            }

            foreach ((XAssetType type, string? name) in NamedDependencies(asset))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(name) || !SupportedAssetTypes.Contains(type))
                    continue;

                AssetKey key;
                try
                {
                    key = AssetKey.FromWireName(
                        CanonicalAssetFamily.FromSerializedType(type), name);
                }
                catch (ArgumentException exception)
                {
                    failures.Add(new SourceAssetDumpFailure(
                        asset.SerializedAssetType,
                        asset.SerializedAssetName ?? "<unnamed>",
                        $"Invalid referenced {type} '{name}': {exception.Message}"));
                    continue;
                }

                if (selected.ContainsKey(key))
                    continue;
                if (!providers.TryGetValue(key, out BaseAsset? dependency))
                {
                    failures.Add(new SourceAssetDumpFailure(
                        asset.SerializedAssetType,
                        asset.SerializedAssetName ?? "<unnamed>",
                        $"Referenced {type} '{name}' has no loaded full definition."));
                    continue;
                }

                selected.Add(key, dependency);
                pending.Enqueue(dependency);
            }
        }

        return selected.Values.ToArray();
    }

    private static bool TryKey(
        BaseAsset asset,
        List<SourceAssetDumpFailure> failures,
        out AssetKey key)
    {
        try
        {
            key = AssetKey.FromDefinition(asset);
            return true;
        }
        catch (ArgumentException exception)
        {
            failures.Add(new SourceAssetDumpFailure(
                asset.SerializedAssetType,
                asset.SerializedAssetName ?? "<unnamed>",
                $"Cannot select asset with an invalid name: {exception.Message}"));
            key = default;
            return false;
        }
    }

    private static IEnumerable<BaseAsset> DirectDependencies(BaseAsset asset)
    {
        switch (asset)
        {
            case MaterialAsset material:
                if (material.TechniqueSet is { } materialTechniqueSet)
                    yield return materialTechniqueSet;
                foreach (MaterialTextureDef texture in material.Textures)
                {
                    if (texture.Image is { } image)
                        yield return image;
                    if (texture.Water?.Image is { } waterImage)
                        yield return waterImage;
                }
                break;

            case MaterialTechniqueSetAsset techniqueSet:
                foreach (MaterialTechniqueSlot slot in techniqueSet.TechniqueSlots)
                {
                    if (slot.Technique is not { } technique)
                        continue;
                    foreach (MaterialPassAsset pass in technique.Passes)
                    {
                        if (pass.VertexShader is { } vertexShader)
                            yield return vertexShader;
                        if (pass.PixelShader is { } pixelShader)
                            yield return pixelShader;
                    }
                }
                break;

            case XModelAsset model:
                foreach (MaterialAsset? modelMaterial in model.Materials)
                {
                    if (modelMaterial is not null)
                        yield return modelMaterial;
                }
                if (model.PhysPreset is { } preset)
                    yield return preset;
                if (model.PhysCollmap is { } collmap)
                    yield return collmap;
                break;

            case LightDefAsset lightDef when lightDef.Image is { } lightImage:
                yield return lightImage;
                break;

            case FontAsset font:
                if (font.Material is { } fontMaterial)
                    yield return fontMaterial;
                if (font.GlowMaterial is { } glowMaterial)
                    yield return glowMaterial;
                break;

            case TracerDefAsset tracer when tracer.Material is { } tracerMaterial:
                yield return tracerMaterial;
                break;

            case MenuFileAsset menuFile:
                foreach (MenuDefReference reference in menuFile.Menus)
                {
                    if ((reference.CanonicalMenu ?? reference.SourceMenu) is { } fileMenu)
                        yield return fileMenu;
                }
                break;

            case MenuDefAsset menu:
                if (menu.Window.BackgroundMaterial is { } background)
                    yield return background;
                foreach (ItemDefReference reference in menu.Items)
                {
                    if (reference.Item is { } item)
                    {
                        if (item.Window.BackgroundMaterial is { } itemBackground)
                            yield return itemBackground;
                        if (item.ListBox?.SelectIconMaterial is { } selectIcon)
                            yield return selectIcon;
                        if (item.FocusSoundAsset is { } focusSound)
                            yield return focusSound;
                    }
                }
                break;

            case SoundAliasListAsset sound:
                foreach (SndAlias alias in sound.Aliases)
                {
                    if (alias.VolumeFalloffCurve is { } curve)
                        yield return curve;
                }
                break;

            case FxEffectDefAsset effect:
                foreach (FxElemDef element in effect.ElemDefs)
                {
                    foreach (BaseAsset visual in VisualDependencies(element.Visuals))
                        yield return visual;
                    foreach (FxElemDefVisuals visual in element.VisualArray)
                    {
                        foreach (BaseAsset dependency in VisualDependencies(visual))
                            yield return dependency;
                    }
                    foreach (FxElemMarkVisuals mark in element.MarkVisualArray)
                    {
                        if (mark.Material0 is { } first)
                            yield return first;
                        if (mark.Material1 is { } second)
                            yield return second;
                    }
                }
                break;

            case VehicleDefAsset vehicle:
                if (vehicle.Phys.PhysPreset is { } vehiclePreset)
                    yield return vehiclePreset;
                if (vehicle.TurretWeapon is { } turretWeapon)
                    yield return turretWeapon;
                if (vehicle.CompassFriendlyIcon is { } friendlyIcon)
                    yield return friendlyIcon;
                if (vehicle.CompassEnemyIcon is { } enemyIcon)
                    yield return enemyIcon;
                break;

            case WeaponAsset weapon:
                WeaponVariantDef variant = weapon.Variant;
                if (variant.KillIcon is { } killIcon)
                    yield return killIcon;
                if (variant.DpadIcon is { } dpadIcon)
                    yield return dpadIcon;
                if (variant.Definition is not { } definition)
                    break;

                foreach (XModelAsset? weaponModel in definition.GunModels.Concat(definition.WorldGunModels))
                {
                    if (weaponModel is not null)
                        yield return weaponModel;
                }
                if (definition.HandModel is { } handModel)
                    yield return handModel;
                if (definition.WorldClipModel is { } clipModel)
                    yield return clipModel;
                if (definition.RocketModel is { } rocketModel)
                    yield return rocketModel;
                if (definition.KnifeModel is { } knifeModel)
                    yield return knifeModel;
                if (definition.WorldKnifeModel is { } worldKnifeModel)
                    yield return worldKnifeModel;
                if (definition.PhysCollmap is { } weaponCollmap)
                    yield return weaponCollmap;
                if (definition.Tracer is { } weaponTracer)
                    yield return weaponTracer;
                if (definition.Icons.HudIcon is { } hudIcon)
                    yield return hudIcon;
                if (definition.Icons.PickupIcon is { } pickupIcon)
                    yield return pickupIcon;
                if (definition.Icons.AmmoCounterIcon is { } ammoIcon)
                    yield return ammoIcon;
                if (definition.Overlay.Material is { } overlay)
                    yield return overlay;
                if (definition.Overlay.MaterialLowRes is { } overlayLowRes)
                    yield return overlayLowRes;
                if (definition.Overlay.MaterialEmp is { } overlayEmp)
                    yield return overlayEmp;
                if (definition.Overlay.MaterialEmpLowRes is { } overlayEmpLowRes)
                    yield return overlayEmpLowRes;
                if (definition.Reticle.CenterMaterial is { } reticleCenter)
                    yield return reticleCenter;
                if (definition.Reticle.SideMaterial is { } reticleSide)
                    yield return reticleSide;
                if (definition.FlashEffects.View is { } flashView)
                    yield return flashView;
                if (definition.FlashEffects.World is { } flashWorld)
                    yield return flashWorld;
                if (definition.ShellEjectEffects.View is { } ejectView)
                    yield return ejectView;
                if (definition.ShellEjectEffects.World is { } ejectWorld)
                    yield return ejectWorld;
                if (definition.ShellEjectEffects.ViewLastShot is { } ejectViewLast)
                    yield return ejectViewLast;
                if (definition.ShellEjectEffects.WorldLastShot is { } ejectWorldLast)
                    yield return ejectWorldLast;
                if (definition.Projectile.Model is { } projectileModel)
                    yield return projectileModel;
                if (definition.Projectile.ExplosionEffect is { } explosion)
                    yield return explosion;
                if (definition.Projectile.DudEffect is { } dud)
                    yield return dud;
                if (definition.Projectile.TrailEffect is { } trail)
                    yield return trail;
                if (definition.Projectile.BeaconEffect is { } beacon)
                    yield return beacon;
                if (definition.Projectile.IgnitionEffect is { } ignition)
                    yield return ignition;
                if (definition.Turret.OverheatEffect is { } overheat)
                    yield return overheat;
                break;
        }
    }

    private static IEnumerable<BaseAsset> VisualDependencies(FxElemDefVisuals visuals)
    {
        if (visuals.Material?.Material is { } material)
            yield return material;
        if (visuals.Model?.Model is { } model)
            yield return model;
    }

    private static IEnumerable<(XAssetType Type, string? Name)> NamedDependencies(
        BaseAsset asset)
    {
        switch (asset)
        {
            case FxEffectDefAsset effect:
                foreach (FxElemDef element in effect.ElemDefs)
                {
                    yield return (XAssetType.Fx, element.EffectOnImpact.Name);
                    yield return (XAssetType.Fx, element.EffectOnDeath.Name);
                    yield return (XAssetType.Fx, element.EffectEmitted.Name);
                    foreach (var visual in new[] { element.Visuals }.Concat(element.VisualArray))
                    {
                        yield return (XAssetType.Fx, visual.Effect?.EffectDef.Name);
                        yield return (XAssetType.Sound, visual.Sound?.SoundName);
                    }
                }
                break;

            case SoundAliasListAsset sound:
                foreach (SndAlias alias in sound.Aliases)
                {
                    yield return (XAssetType.Sound, alias.SecondaryAliasName);
                    yield return (XAssetType.Sound, alias.ChainAliasName);
                }
                break;

            case MenuDefAsset menu:
                yield return (XAssetType.Material, menu.Window.BackgroundMaterialName);
                foreach (ItemDefReference reference in menu.Items)
                {
                    if (reference.Item is not { } item)
                        continue;
                    yield return (XAssetType.Material, item.Window.BackgroundMaterialName);
                    yield return (XAssetType.Material, item.ListBox?.SelectIconMaterialName);
                    yield return (XAssetType.Sound, item.FocusSoundName);
                }
                break;

            case VehicleDefAsset vehicle:
                yield return (XAssetType.PhysPreset, vehicle.Phys.PhysPresetName);
                yield return (XAssetType.Weapon, vehicle.TurretWeaponName);
                yield return (XAssetType.Sound, vehicle.TurretSpinSound.Value);
                yield return (XAssetType.Sound, vehicle.TurretStopSound.Value);
                yield return (XAssetType.Sound, vehicle.CollisionSound.Value);
                yield return (XAssetType.Sound, vehicle.SpeedSound.Value);
                foreach (VehicleSoundAliasField soundField in new[]
                {
                    vehicle.EngineSounds.IdleLowSound,
                    vehicle.EngineSounds.IdleHighSound,
                    vehicle.EngineSounds.EngineLowSound,
                    vehicle.EngineSounds.EngineHighSound,
                    vehicle.EngineSounds.EngineStartUpSound,
                    vehicle.EngineSounds.EngineShutdownSound,
                    vehicle.EngineSounds.EngineIdleSound,
                    vehicle.EngineSounds.EngineSustainSound,
                    vehicle.EngineSounds.EngineRampUpSound,
                    vehicle.EngineSounds.EngineRampDownSound,
                    vehicle.SuspensionSounds.SuspensionSoftSound,
                    vehicle.SuspensionSounds.SuspensionHardSound
                }.Concat(vehicle.SurfaceSoundFields))
                    yield return (XAssetType.Sound, soundField.Value);
                break;

            case WeaponAsset weapon:
                WeaponVariantDef variant = weapon.Variant;
                foreach (string? name in variant.AnimationNames)
                    yield return (XAssetType.XAnim, name);
                yield return (XAssetType.Weapon, variant.AlternateWeaponName);
                if (variant.Definition is not { } definition)
                    break;
                foreach (string? name in definition.RightHandAnimationNames.Concat(
                             definition.LeftHandAnimationNames))
                    yield return (XAssetType.XAnim, name);
                yield return (XAssetType.PhysCollmap, definition.PhysCollmapName);
                foreach (WeaponSoundAliasField soundField in WeaponSounds(definition))
                    yield return (XAssetType.Sound, soundField.Name);
                yield return (XAssetType.Sound, definition.Projectile.ExplosionSound);
                yield return (XAssetType.Sound, definition.Projectile.DudSound);
                yield return (XAssetType.Sound, definition.Projectile.IgnitionSound);
                yield return (XAssetType.Sound, definition.Turret.OverheatSound);
                yield return (XAssetType.Sound, definition.Turret.BarrelSpinMaxSound);
                yield return (XAssetType.Sound, definition.MissileConeSound.Alias);
                yield return (XAssetType.Sound, definition.MissileConeSound.AliasAtBase);
                break;
        }
    }

    private static IEnumerable<WeaponSoundAliasField> WeaponSounds(WeaponDef definition)
    {
        WeaponPrimarySoundFields sounds = definition.PrimarySounds;
        foreach (WeaponSoundAliasField sound in new[]
        {
            sounds.PickupSound, sounds.PickupSoundPlayer,
            sounds.AmmoPickupSound, sounds.AmmoPickupSoundPlayer,
            sounds.ProjectileSound, sounds.PullbackSound, sounds.PullbackSoundPlayer,
            sounds.FireSound, sounds.FireSoundPlayer, sounds.FireSoundPlayerAkimbo,
            sounds.FireLoopSound, sounds.FireLoopSoundPlayer,
            sounds.FireStopSound, sounds.FireStopSoundPlayer,
            sounds.FireLastSound, sounds.FireLastSoundPlayer,
            sounds.EmptyFireSound, sounds.EmptyFireSoundPlayer,
            sounds.MeleeSwipeSound, sounds.MeleeSwipeSoundPlayer,
            sounds.MeleeHitSound, sounds.MeleeMissSound,
            sounds.RechamberSound, sounds.RechamberSoundPlayer,
            sounds.ReloadSound, sounds.ReloadSoundPlayer,
            sounds.ReloadEmptySound, sounds.ReloadEmptySoundPlayer,
            sounds.ReloadStartSound, sounds.ReloadStartSoundPlayer,
            sounds.ReloadEndSound, sounds.ReloadEndSoundPlayer,
            sounds.DetonateSound, sounds.DetonateSoundPlayer,
            sounds.NightVisionWearSound, sounds.NightVisionWearSoundPlayer,
            sounds.NightVisionRemoveSound, sounds.NightVisionRemoveSoundPlayer,
            sounds.AltSwitchSound, sounds.AltSwitchSoundPlayer,
            sounds.RaiseSound, sounds.RaiseSoundPlayer,
            sounds.FirstRaiseSound, sounds.FirstRaiseSoundPlayer,
            sounds.PutawaySound, sounds.PutawaySoundPlayer,
            sounds.ScanSound
        }.Concat(definition.BounceSounds)
         .Concat(definition.Turret.BarrelSpinUpSounds)
         .Concat(definition.Turret.BarrelSpinDownSounds))
            yield return sound;
    }
}
