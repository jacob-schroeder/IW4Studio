using System.Text.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using IW4.Formats.SourceFormat.Technique;
using IW4.Game.Assets;
using IW4.Game.Assets.Fx;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.Physics;
using IW4.Game.Assets.Tracer;
using IW4.Game.Assets.Weapon;
using IW4.Game.Assets.XModel;
using IW4.Game.Pointers;
using IW4.Game.ScriptStrings;
using IW4.Game.Zone;

namespace IW4.Formats.SourceFormat.Weapon;

/// <summary>
/// Retains the modeled PS3 weapon values and storage presence for the stock
/// minigun turret. Pointer addresses and provider bodies are supplied anew.
/// </summary>
public sealed class WeaponNativeExchange
{
    public const string SupportedWeaponName = "turret_minigun_mp";
    private const string Format = "iw4-ps3-weapon-native";

    public IReadOnlyList<string> Unlink(string sourceDirectory, WeaponAsset weapon)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        string name = SourceOutput.NormalizeOwnedAssetName(weapon.Name, "Weapon");
        Validate(weapon, name);
        var document = new NativeWeapon(Format, 1, name, weapon.Variant);
        string json = JsonSerializer.Serialize(document, Options(null));
        return new SourceOutput(sourceDirectory).WriteTextBatch(
            [($"weapon_native/{name}.json", writer => writer.WriteLine(json))]);
    }

    public WeaponAsset Link(
        string sourceDirectory,
        string weaponName,
        Func<XAssetType, string, BaseAsset> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        string name = SourceOutput.NormalizeOwnedAssetName(weaponName, "Weapon");
        RequireSupportedName(name);
        string path = NativeSourcePath.Resolve(sourceDirectory, "weapon_native", name, ".json");
        NativeWeapon document = JsonSerializer.Deserialize<NativeWeapon>(
            File.ReadAllText(path), Options(resolve)) ??
            throw new InvalidDataException($"Weapon '{name}' has an empty native source document.");
        if (document.Format != Format || document.Version != 1 || document.Name != name ||
            document.Variant is null)
            throw new InvalidDataException($"Weapon '{name}' has invalid native source fields.");
        var weapon = new WeaponAsset { Variant = document.Variant };
        Validate(weapon, name);
        return weapon;
    }

    public static IReadOnlyList<(XAssetType Type, string Name)> EnumerateDependencies(WeaponAsset weapon)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        string name = SourceOutput.NormalizeOwnedAssetName(weapon.Name, "Weapon");
        Validate(weapon, name);
        WeaponVariantDef variant = weapon.Variant;
        WeaponDef definition = variant.Definition ??
            throw new InvalidDataException($"Weapon '{name}' has no WeaponDef body.");
        var dependencies = new HashSet<(XAssetType Type, string Name)>();
        void Add(BaseAsset? asset)
        {
            if (asset is null) return;
            string referenced = SourceOutput.NormalizeReferencedAssetName(
                asset.SerializedAssetName, $"Weapon '{name}' dependency");
            dependencies.Add((asset.SerializedAssetType, referenced));
        }
        void Sound(string? alias)
        {
            if (!string.IsNullOrEmpty(alias))
                dependencies.Add((XAssetType.Sound, alias));
        }
        void Rumble(string? rumble)
        {
            if (string.IsNullOrEmpty(rumble)) return;
            RequireSimpleName(rumble, "rumble definition");
            dependencies.Add((XAssetType.RawFile, $"rumble/{rumble}"));
        }

        Add(variant.KillIcon);
        Add(variant.DpadIcon);
        Add(definition.FlashEffects.View);
        Add(definition.FlashEffects.World);
        Add(definition.ShellEjectEffects.View);
        Add(definition.ShellEjectEffects.World);
        Add(definition.ShellEjectEffects.ViewLastShot);
        Add(definition.ShellEjectEffects.WorldLastShot);
        Add(definition.Reticle.CenterMaterial);
        Add(definition.Reticle.SideMaterial);
        Add(definition.Icons.HudIcon);
        Add(definition.Icons.PickupIcon);
        Add(definition.Icons.AmmoCounterIcon);
        Add(definition.Overlay.Material);
        Add(definition.Overlay.MaterialLowRes);
        Add(definition.Overlay.MaterialEmp);
        Add(definition.Overlay.MaterialEmpLowRes);
        Add(definition.Projectile.ExplosionEffect);
        Add(definition.Projectile.DudEffect);
        Add(definition.Projectile.TrailEffect);
        Add(definition.Projectile.BeaconEffect);
        Add(definition.Projectile.IgnitionEffect);
        Add(definition.Turret.OverheatEffect);
        Add(definition.Tracer);

        for (int slot = 0; slot < (int)WeaponPrimarySoundSlot.Count; slot++)
            Sound(definition.PrimarySounds.Get((WeaponPrimarySoundSlot)slot).Name);
        foreach (WeaponSoundAliasField field in definition.BounceSounds)
            Sound(field.Name);
        Sound(definition.Projectile.ExplosionSound);
        Sound(definition.Projectile.DudSound);
        Sound(definition.Projectile.IgnitionSound);
        Sound(definition.Turret.OverheatSound);
        Sound(definition.Turret.BarrelSpinMaxSound);
        foreach (WeaponSoundAliasField field in definition.Turret.BarrelSpinUpSounds)
            Sound(field.Name);
        foreach (WeaponSoundAliasField field in definition.Turret.BarrelSpinDownSounds)
            Sound(field.Name);
        Sound(definition.MissileConeSound.Alias);
        Sound(definition.MissileConeSound.AliasAtBase);
        Rumble(definition.Rumble.FireRumble);
        Rumble(definition.Rumble.MeleeImpactRumble);
        Rumble(definition.Turret.BarrelSpinRumble);
        return dependencies.OrderBy(entry => entry.Type)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Names the two graph RawFiles owned by a native RUMBLE definition.</summary>
    public static IReadOnlyList<string> EnumerateRumbleGraphFiles(
        string rawFileName,
        ReadOnlySpan<byte> content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawFileName);
        if (!rawFileName.StartsWith("rumble/", StringComparison.Ordinal) ||
            rawFileName.EndsWith(".rmb", StringComparison.OrdinalIgnoreCase))
            return [];

        string source;
        try
        {
            source = new UTF8Encoding(false, true).GetString(content).TrimEnd('\r', '\n');
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException($"Rumble RawFile '{rawFileName}' is not UTF-8.", exception);
        }
        string[] parts = source.Split('\\');
        if (parts.Length < 5 || parts.Length % 2 != 1 || parts[0] != "RUMBLE")
            throw new InvalidDataException($"Rumble RawFile '{rawFileName}' is not a complete RUMBLE info string.");

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 1; index < parts.Length; index += 2)
        {
            string key = parts[index];
            if (key.Length == 0 || !fields.TryAdd(key, parts[index + 1]))
                throw new InvalidDataException($"Rumble RawFile '{rawFileName}' has a missing or duplicate field key.");
        }
        string Graph(string key)
        {
            if (!fields.TryGetValue(key, out string? graph))
                throw new InvalidDataException($"Rumble RawFile '{rawFileName}' has no '{key}' field.");
            RequireSimpleName(graph, $"Rumble RawFile '{rawFileName}' {key}");
            if (!graph.EndsWith(".rmb", StringComparison.Ordinal))
                throw new InvalidDataException($"Rumble RawFile '{rawFileName}' {key} is not an .rmb graph.");
            return $"rumble/{graph}";
        }
        return [Graph("lowRumbleFile"), Graph("highRumbleFile")];
    }

    private static void RequireSimpleName(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." ||
            value.Contains('/') || value.Contains('\\') ||
            value.Contains(':') || value.Any(char.IsControl))
            throw new InvalidDataException($"{field} must be a simple file name.");
    }

    private static JsonSerializerOptions Options(Func<XAssetType, string, BaseAsset>? resolve)
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type == typeof(WeaponVariantDef) || info.Type == typeof(WeaponDef))
            {
                JsonPropertyInfo? offset = info.Properties.FirstOrDefault(
                    property => property.Name.Equals(nameof(WeaponDef.Offset),
                        StringComparison.OrdinalIgnoreCase));
                if (offset is not null) info.Properties.Remove(offset);
            }
            if (info.Type == typeof(NativeWeapon) ||
                info.Type.Namespace == typeof(WeaponDef).Namespace)
            {
                foreach (JsonPropertyInfo property in info.Properties)
                    property.IsRequired = true;
            }
        });
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            IncludeFields = true,
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true,
            TypeInfoResolver = resolver
        };
        options.Converters.Add(new PointerConverterFactory());
        options.Converters.Add(new AssetNameConverterFactory(resolve));
        options.Converters.Add(new ScriptStringConverter());
        return options;
    }

    private static void Validate(WeaponAsset weapon, string name)
    {
        RequireSupportedName(name);
        WeaponVariantDef variant = weapon.Variant;
        WeaponDef definition = variant.Definition ??
            throw new InvalidDataException($"Weapon '{name}' has no WeaponDef body.");
        if (variant.InternalName != name || definition.WeaponClass != WeaponClass.Turret)
            throw new InvalidDataException($"Weapon '{name}' is not the stock turret profile.");
        if (definition.NoteTrackMaps is null || definition.FlashEffects is null ||
            definition.PrimarySounds is null || definition.ShellEjectEffects is null ||
            definition.Reticle is null || definition.Icons is null ||
            definition.Overlay is null || definition.Projectile is null ||
            definition.Accuracy is null || definition.Turret is null ||
            definition.MissileConeSound is null || definition.Rumble is null)
            throw new InvalidDataException($"Weapon '{name}' has a missing native field group.");

        CheckArray(variant.HideTags, variant.HideTagsPointer.Raw, WeaponVariantDef.HideTagCount, "hide tags");
        CheckArray(variant.AnimationNames, variant.AnimationNamesPointer.Raw,
            (int)WeaponAnimationSlot.Count, "variant animations");
        CheckArray(variant.AnimationNamePointers, variant.AnimationNamesPointer.Raw,
            (int)WeaponAnimationSlot.Count, "variant animation pointers");
        CheckArray(definition.GunModels, definition.GunModelsPointer.Raw, WeaponDef.GunModelCount, "gun models");
        CheckArray(definition.GunModelPointers, definition.GunModelsPointer.Raw, WeaponDef.GunModelCount, "gun model pointers");
        CheckArray(definition.WorldGunModels, definition.WorldGunModelsPointer.Raw, WeaponDef.GunModelCount, "world gun models");
        CheckArray(definition.WorldGunModelPointers, definition.WorldGunModelsPointer.Raw, WeaponDef.GunModelCount, "world gun model pointers");
        CheckArray(definition.RightHandAnimationNames, definition.RightHandAnimationNamesPointer.Raw,
            (int)WeaponAnimationSlot.Count, "right animations");
        CheckArray(definition.RightHandAnimationNamePointers, definition.RightHandAnimationNamesPointer.Raw,
            (int)WeaponAnimationSlot.Count, "right animation pointers");
        CheckArray(definition.LeftHandAnimationNames, definition.LeftHandAnimationNamesPointer.Raw,
            (int)WeaponAnimationSlot.Count, "left animations");
        CheckArray(definition.LeftHandAnimationNamePointers, definition.LeftHandAnimationNamesPointer.Raw,
            (int)WeaponAnimationSlot.Count, "left animation pointers");
        CheckArray(definition.NoteTrackMaps.SoundMappings,
            definition.NoteTrackMaps.SoundMapKeysPointer.Raw | definition.NoteTrackMaps.SoundMapValuesPointer.Raw,
            WeaponDef.NoteTrackMapCount, "sound notetrack map");
        CheckArray(definition.NoteTrackMaps.RumbleMappings,
            definition.NoteTrackMaps.RumbleMapKeysPointer.Raw | definition.NoteTrackMaps.RumbleMapValuesPointer.Raw,
            WeaponDef.NoteTrackMapCount, "rumble notetrack map");
        if (definition.NoteTrackMaps.SoundMappings.Any(HasMapping) ||
            definition.NoteTrackMaps.RumbleMappings.Any(HasMapping))
            throw new NotSupportedException($"Weapon '{name}' has notetrack mappings outside the stock turret source profile.");
        CheckArray(definition.BounceSounds, definition.BounceSoundPointer.Raw,
            (int)MaterialSurfaceType.Count, "bounce sounds");
        CheckArray(definition.LocationDamageMultipliers, definition.LocationDamageMultipliersPointer.Raw,
            (int)HitLocation.Count, "location damage");
        CheckArray(definition.Projectile.ParallelBounce, definition.Projectile.ParallelBouncePointer.Raw,
            (int)MaterialSurfaceType.Count, "parallel bounce");
        CheckArray(definition.Projectile.PerpendicularBounce, definition.Projectile.PerpendicularBouncePointer.Raw,
            (int)MaterialSurfaceType.Count, "perpendicular bounce");
        RequireCount(variant.AiVsAiAccuracyGraphKnotCount, variant.AiVsAiAccuracyGraphKnots.Count,
            "AI-vs-AI current accuracy");
        RequireCount(variant.AiVsPlayerAccuracyGraphKnotCount, variant.AiVsPlayerAccuracyGraphKnots.Count,
            "AI-vs-player current accuracy");
        RequireCount(definition.Accuracy.OriginalAiVsAiGraphKnotCount,
            definition.Accuracy.OriginalAiVsAiGraphKnots.Count, "AI-vs-AI original accuracy");
        RequireCount(definition.Accuracy.OriginalAiVsPlayerGraphKnotCount,
            definition.Accuracy.OriginalAiVsPlayerGraphKnots.Count, "AI-vs-player original accuracy");
        if (definition.Turret.BarrelSpinUpSounds.Count != (int)WeaponTurretBarrelSpinSoundSlot.Count ||
            definition.Turret.BarrelSpinDownSounds.Count != (int)WeaponTurretBarrelSpinSoundSlot.Count)
            throw new InvalidDataException($"Weapon '{name}' has incomplete turret barrel-spin sounds.");
        if (definition.BounceSounds.Any(field => field is null) ||
            definition.Turret.BarrelSpinUpSounds.Any(field => field is null) ||
            definition.Turret.BarrelSpinDownSounds.Any(field => field is null))
            throw new InvalidDataException($"Weapon '{name}' has a null sound wrapper.");

        // The stock profile has no model, animation, alternate-weapon, or physics provider.
        if (definition.GunModels.Any(model => model is not null) ||
            definition.GunModelPointers.Any(pointer => pointer.Raw != 0) ||
            definition.WorldGunModels.Any(model => model is not null) ||
            definition.WorldGunModelPointers.Any(pointer => pointer.Raw != 0) ||
            definition.HandModel is not null || definition.WorldClipModel is not null ||
            definition.RocketModel is not null || definition.KnifeModel is not null ||
            definition.WorldKnifeModel is not null || definition.Projectile.Model is not null ||
            definition.HandModelPointer.Raw != 0 || definition.WorldClipModelPointer.Raw != 0 ||
            definition.RocketModelPointer.Raw != 0 || definition.KnifeModelPointer.Raw != 0 ||
            definition.WorldKnifeModelPointer.Raw != 0 || definition.Projectile.ModelPointer.Raw != 0 ||
            definition.PhysCollmap is not null ||
            definition.PhysCollmapPointer.Raw != 0 ||
            !string.IsNullOrEmpty(definition.PhysCollmapName) ||
            variant.AlternateWeaponName is not null && variant.AlternateWeaponName.Length != 0 ||
            variant.AnimationNames.Any(value => !string.IsNullOrEmpty(value)) ||
            definition.RightHandAnimationNames.Any(value => !string.IsNullOrEmpty(value)) ||
            definition.LeftHandAnimationNames.Any(value => !string.IsNullOrEmpty(value)))
            throw new NotSupportedException($"Weapon '{name}' exceeds the stock turret native-source profile.");
    }

    private static void RequireSupportedName(string name)
    {
        if (name != SupportedWeaponName)
            throw new NotSupportedException($"Native weapon source supports only '{SupportedWeaponName}'.");
    }

    private static void CheckArray<T>(IReadOnlyList<T> values, int pointerRaw, int expected, string field)
    {
        if (values is null || values.Count != 0 && values.Count != expected ||
            pointerRaw != 0 && values.Count == 0)
            throw new InvalidDataException($"Native weapon {field} requires {expected} values when present.");
    }

    private static void RequireCount(int expected, int actual, string field)
    {
        if (expected != actual)
            throw new InvalidDataException($"Native weapon {field} declares {expected} values but has {actual}.");
    }

    private static bool HasMapping(WeaponNoteTrackMapEntry? entry) =>
        entry is null || entry.Key is null || entry.Value is null ||
        !string.IsNullOrEmpty(entry.Key.Text) || !string.IsNullOrEmpty(entry.Value.Text);

    private sealed record NativeWeapon(string Format, int Version, string Name, WeaponVariantDef Variant);

    private sealed class PointerConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) =>
            typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(XPointer<>);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            Type valueType = typeToConvert.GetGenericArguments()[0];
            return Activator.CreateInstance(
                typeof(PointerConverter<>).MakeGenericType(valueType), nonPublic: true)
                as JsonConverter ?? throw new InvalidOperationException(
                    $"Cannot create weapon pointer converter for {valueType.Name}.");
        }
    }

    private sealed class PointerConverter<T> : JsonConverter<XPointer<T>>
    {
        public override XPointer<T> Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options)
        {
            bool present = reader.GetBoolean();
            return present
                ? new XPointer<T>(-1, typeof(BaseAsset).IsAssignableFrom(typeof(T))
                    ? XPointerResolutionMode.AliasCell : XPointerResolutionMode.Direct)
                : default;
        }

        public override void Write(Utf8JsonWriter writer, XPointer<T> value,
            JsonSerializerOptions options) => writer.WriteBooleanValue(value.Raw != 0);
    }

    private sealed class AssetNameConverterFactory(Func<XAssetType, string, BaseAsset>? resolve)
        : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) =>
            typeof(BaseAsset).IsAssignableFrom(typeToConvert);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            if (typeToConvert == typeof(MaterialAsset))
                return new AssetNameConverter<MaterialAsset>(resolve);
            if (typeToConvert == typeof(FxEffectDefAsset))
                return new AssetNameConverter<FxEffectDefAsset>(resolve);
            if (typeToConvert == typeof(TracerDefAsset))
                return new AssetNameConverter<TracerDefAsset>(resolve);
            if (typeToConvert == typeof(XModelAsset))
                return new AssetNameConverter<XModelAsset>(resolve);
            if (typeToConvert == typeof(PhysCollmapAsset))
                return new AssetNameConverter<PhysCollmapAsset>(resolve);
            throw new NotSupportedException(
                $"Weapon native source does not support {typeToConvert.Name} references.");
        }
    }

    private sealed class AssetNameConverter<T>(Func<XAssetType, string, BaseAsset>? resolve)
        : JsonConverter<T> where T : BaseAsset
    {
        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            XAssetType type = SupportedType();
            string name = SourceOutput.NormalizeReferencedAssetName(
                reader.GetString(), $"Weapon {type} reference");
            BaseAsset asset = resolve?.Invoke(type, name) ??
                throw new InvalidDataException($"Weapon {type} '{name}' has no provider resolver.");
            if (asset is not T || asset.SerializedAssetType != type || asset.SerializedAssetName != name)
                throw new InvalidDataException($"Weapon {type} '{name}' resolved to a different provider.");
            return (T)asset;
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            XAssetType type = SupportedType();
            if (value.SerializedAssetType != type)
                throw new InvalidDataException($"Weapon provider has wrong {type} type.");
            writer.WriteStringValue(SourceOutput.NormalizeReferencedAssetName(
                value.SerializedAssetName, $"Weapon {type} reference"));
        }

        private static XAssetType SupportedType()
        {
            if (typeof(T) == typeof(MaterialAsset)) return XAssetType.Material;
            if (typeof(T) == typeof(FxEffectDefAsset)) return XAssetType.Fx;
            if (typeof(T) == typeof(TracerDefAsset)) return XAssetType.Tracer;
            throw new NotSupportedException($"Weapon native source does not support {typeof(T).Name} references.");
        }
    }

    private sealed class ScriptStringConverter : JsonConverter<ScriptStringReference>
    {
        public override bool HandleNull => true;

        public override ScriptStringReference Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options) => new(0,
                reader.TokenType == JsonTokenType.Null ? null : reader.GetString(), default, default);

        public override void Write(Utf8JsonWriter writer, ScriptStringReference value,
            JsonSerializerOptions options)
        {
            if (value.Text is null && value.RawLocalIndex != 0)
                throw new InvalidDataException($"Weapon script string {value.RawLocalIndex} has no text.");
            if (value.Text is null) writer.WriteNullValue();
            else writer.WriteStringValue(value.Text);
        }
    }
}
