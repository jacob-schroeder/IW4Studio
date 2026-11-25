using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using IW4.Game.Assets;
using IW4.Game.Assets.Fx;
using IW4.Game.Assets.Menu;
using IW4.Game.Assets.Sound;
using IW4.Game.Assets.Vehicle;
using IW4.Game.Assets.Weapon;
using IW4.Game.Zone;
using IW4.Linker.Contracts;

namespace IW4.Studio.Documents.AssetReferences;

/// <summary>
/// Reads immediate references from a loaded asset that has no frozen linker
/// provider. A nested BaseAsset is an edge, never another traversal root.
/// </summary>
internal static class WorkspaceAssetDependencyEnumerator
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Properties = new();
    private static readonly ConcurrentDictionary<Type, bool> ReachableTypes = new();

    public static IEnumerable<(XAssetType Type, string Name, string Field)> Enumerate(
        BaseAsset source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Walk(source, "", new HashSet<object>(ReferenceEqualityComparer.Instance),
            cancellationToken);
    }

    private static IEnumerable<(XAssetType Type, string Name, string Field)> Walk(
        object value,
        string path,
        HashSet<object> active,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (path.Length != 0 && value is BaseAsset asset)
        {
            if (!TryKey(asset, out AssetKey key))
                yield break;

            yield return (asset.SerializedAssetType, key.NormalizedName, path);
            yield break;
        }

        if (value is IEnumerable sequence and not string)
        {
            if (!active.Add(value))
                yield break;
            try
            {
                int index = 0;
                foreach (object? item in sequence)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (item is not null)
                    {
                        foreach (var reference in Walk(item, $"{path}[{index}]", active,
                                     cancellationToken))
                            yield return reference;
                    }
                    index++;
                }
            }
            finally
            {
                active.Remove(value);
            }
            yield break;
        }

        Type type = value.GetType();
        if (!IsAssetSchemaType(type) || !active.Add(value))
            yield break;
        try
        {
            foreach ((XAssetType referenceType, string? name, string field) in
                     NamedReferences(value, path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryKey(referenceType, name, out AssetKey key))
                    continue;
                yield return (referenceType, key.NormalizedName, field);
            }

            foreach (PropertyInfo property in GetProperties(type))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!MayContainReferences(property.PropertyType))
                    continue;
                object? child = property.GetValue(value);
                if (child is null)
                    continue;
                string childPath = Field(path, property.Name);
                foreach (var reference in Walk(child, childPath, active, cancellationToken))
                    yield return reference;
            }
        }
        finally
        {
            active.Remove(value);
        }
    }

    private static IEnumerable<(XAssetType Type, string? Name, string Field)>
        NamedReferences(object value, string path)
    {
        switch (value)
        {
            case FxEffectDefRef effect:
                yield return (XAssetType.Fx, effect.Name, Field(path, nameof(effect.Name)));
                break;
            case FxSoundVisual sound:
                yield return (XAssetType.Sound, sound.SoundName,
                    Field(path, nameof(sound.SoundName)));
                break;
            case SndAlias alias:
                yield return (XAssetType.Sound, alias.SecondaryAliasName,
                    Field(path, nameof(alias.SecondaryAliasName)));
                yield return (XAssetType.Sound, alias.ChainAliasName,
                    Field(path, nameof(alias.ChainAliasName)));
                break;
            case WindowDef window:
                yield return (XAssetType.Material, window.BackgroundMaterialName,
                    Field(path, nameof(window.BackgroundMaterialName)));
                break;
            case ListBoxDef listBox:
                yield return (XAssetType.Material, listBox.SelectIconMaterialName,
                    Field(path, nameof(listBox.SelectIconMaterialName)));
                break;
            case ItemDefAsset item:
                yield return (XAssetType.Sound, item.FocusSoundName,
                    Field(path, nameof(item.FocusSoundName)));
                break;
            case VehiclePhysDef physics:
                yield return (XAssetType.PhysPreset, physics.PhysPresetName,
                    Field(path, nameof(physics.PhysPresetName)));
                break;
            case VehicleDefAsset vehicle:
                yield return (XAssetType.Weapon, vehicle.TurretWeaponName,
                    Field(path, nameof(vehicle.TurretWeaponName)));
                break;
            case VehicleSoundAliasField vehicleSound:
                yield return (XAssetType.Sound, vehicleSound.Value,
                    Field(path, nameof(vehicleSound.Value)));
                break;
            case WeaponSoundAliasField weaponSound:
                yield return (XAssetType.Sound, weaponSound.Name,
                    Field(path, nameof(weaponSound.Name)));
                break;
            case WeaponVariantDef variant:
                yield return (XAssetType.Weapon, variant.AlternateWeaponName,
                    Field(path, nameof(variant.AlternateWeaponName)));
                foreach (var reference in AnimationNames(variant.AnimationNames,
                             Field(path, nameof(variant.AnimationNames))))
                    yield return reference;
                break;
            case WeaponDef definition:
                foreach (var reference in AnimationNames(definition.RightHandAnimationNames,
                             Field(path, nameof(definition.RightHandAnimationNames))))
                    yield return reference;
                foreach (var reference in AnimationNames(definition.LeftHandAnimationNames,
                             Field(path, nameof(definition.LeftHandAnimationNames))))
                    yield return reference;
                yield return (XAssetType.PhysCollmap, definition.PhysCollmapName,
                    Field(path, nameof(definition.PhysCollmapName)));
                break;
            case WeaponProjectileFields projectile:
                yield return (XAssetType.Sound, projectile.ExplosionSound,
                    Field(path, nameof(projectile.ExplosionSound)));
                yield return (XAssetType.Sound, projectile.DudSound,
                    Field(path, nameof(projectile.DudSound)));
                yield return (XAssetType.Sound, projectile.IgnitionSound,
                    Field(path, nameof(projectile.IgnitionSound)));
                break;
            case WeaponTurretFields turret:
                yield return (XAssetType.Sound, turret.OverheatSound,
                    Field(path, nameof(turret.OverheatSound)));
                yield return (XAssetType.Sound, turret.BarrelSpinMaxSound,
                    Field(path, nameof(turret.BarrelSpinMaxSound)));
                break;
            case WeaponMissileConeSoundFields missile:
                yield return (XAssetType.Sound, missile.Alias,
                    Field(path, nameof(missile.Alias)));
                yield return (XAssetType.Sound, missile.AliasAtBase,
                    Field(path, nameof(missile.AliasAtBase)));
                break;
        }
    }

    private static IEnumerable<(XAssetType Type, string? Name, string Field)> AnimationNames(
        IReadOnlyList<string?> names,
        string path)
    {
        for (int index = 0; index < names.Count; index++)
            yield return (XAssetType.XAnim, names[index], $"{path}[{index}]");
    }

    private static string Field(string path, string member) =>
        path.Length == 0 ? member : $"{path}.{member}";

    private static bool TryKey(BaseAsset asset, out AssetKey key)
    {
        try
        {
            key = AssetKey.FromDefinition(asset);
            return true;
        }
        catch (ArgumentException)
        {
            key = default;
            return false;
        }
    }

    private static bool TryKey(XAssetType type, string? name, out AssetKey key)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            key = default;
            return false;
        }
        try
        {
            key = AssetKey.FromWireName(
                CanonicalAssetFamily.FromSerializedType(type), name);
            return true;
        }
        catch (ArgumentException)
        {
            key = default;
            return false;
        }
    }

    private static bool MayContainReferences(Type type) =>
        ReachableTypes.GetOrAdd(type, static candidate =>
            ComputeReachability(candidate, new HashSet<Type>()));

    private static bool ComputeReachability(Type type, HashSet<Type> active)
    {
        if (typeof(BaseAsset).IsAssignableFrom(type) || HasNamedReferences(type))
            return true;
        if (TryElementType(type, out Type elementType))
            return ComputeReachability(elementType, active);
        if (!IsAssetSchemaType(type))
            return false;
        if (type.IsAbstract)
            return true;
        if (!active.Add(type))
            return false;
        try
        {
            return GetProperties(type).Any(property =>
                ComputeReachability(property.PropertyType, active));
        }
        finally
        {
            active.Remove(type);
        }
    }

    private static bool TryElementType(Type type, out Type elementType)
    {
        if (type.IsArray)
        {
            elementType = type.GetElementType()!;
            return true;
        }
        Type? sequence = type.GetInterfaces()
            .Append(type)
            .FirstOrDefault(candidate => candidate.IsGenericType &&
                candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        if (sequence is not null)
        {
            elementType = sequence.GetGenericArguments()[0];
            return true;
        }
        elementType = null!;
        return false;
    }

    private static PropertyInfo[] GetProperties(Type type) =>
        Properties.GetOrAdd(type, static candidate => candidate
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetMethod?.IsPublic == true &&
                property.SetMethod?.IsPublic == true &&
                property.GetIndexParameters().Length == 0)
            .ToArray());

    private static bool IsAssetSchemaType(Type type) =>
        type.Namespace?.StartsWith("IW4.Game.Assets", StringComparison.Ordinal) == true;

    private static bool HasNamedReferences(Type type) =>
        type == typeof(FxEffectDefRef) || type == typeof(FxSoundVisual) ||
        type == typeof(SndAlias) || type == typeof(WindowDef) ||
        type == typeof(ListBoxDef) || type == typeof(ItemDefAsset) ||
        type == typeof(VehiclePhysDef) || type == typeof(VehicleDefAsset) ||
        type == typeof(VehicleSoundAliasField) ||
        type == typeof(WeaponSoundAliasField) ||
        type == typeof(WeaponVariantDef) || type == typeof(WeaponDef) ||
        type == typeof(WeaponProjectileFields) ||
        type == typeof(WeaponTurretFields) ||
        type == typeof(WeaponMissileConeSoundFields);
}
