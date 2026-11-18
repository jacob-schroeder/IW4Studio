using System.Reflection;
using System.Text.RegularExpressions;
using IW4.Game.Assets;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.Vehicle;
using IW4.Game.Pointers;
using IW4.Game.ScriptStrings;
using IW4.Game.Zone;

namespace IW4.Studio.Documents;

public enum VehicleFieldKind
{
    Text,
    Integer,
    Float,
    Boolean,
    Choice,
    AssetReference,
    SoundAlias
}

/// <summary>One editable value in the native VehicleDef, including fixed collection slots.</summary>
public sealed class VehicleField
{
    private static readonly Regex WordBoundary = new(@"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])");
    private static readonly Dictionary<Type, PropertyInfo[]> CopyMembers = new[]
    {
        typeof(VehicleDefAsset), typeof(VehiclePhysDef), typeof(VehicleFakeBodyTuning),
        typeof(VehicleVec3), typeof(VehicleEngineSoundFields), typeof(VehicleSuspensionSoundFields)
    }.ToDictionary(type => type, type => type.GetProperties()
        .Where(property => property.SetMethod?.IsPublic == true && property.Name != nameof(BaseAsset.RuntimeAddress))
        .ToArray());

    private readonly PropertyInfo _value;
    private readonly PropertyInfo? _pointer;
    private readonly int? _index;

    private VehicleField(
        string section,
        PropertyInfo? group,
        PropertyInfo value,
        VehicleFieldKind kind,
        XAssetType? referenceType = null,
        int? index = null,
        string? label = null)
    {
        Section = section;
        Group = group;
        _value = value;
        _index = index;
        Kind = kind;
        ReferenceType = referenceType;
        string path = group is null ? value.Name : $"{group.Name}.{value.Name}";
        Path = index is { } slot ? $"{path}[{slot}]" : path;
        Label = label ?? WordBoundary.Replace(value.Name, " ");
        Options = kind == VehicleFieldKind.Choice
            ? Array.AsReadOnly(Enum.GetNames(value.PropertyType).Where(name => name != "Count").ToArray())
            : [];
        _pointer = value.DeclaringType?.GetProperty($"{value.Name}Pointer");
    }

    public string Path { get; }
    public string Section { get; }
    public string Label { get; }
    public VehicleFieldKind Kind { get; }
    public IReadOnlyList<string> Options { get; }
    public XAssetType? ReferenceType { get; }

    internal PropertyInfo? Group { get; }
    internal Type NativeType => _value.PropertyType;

    // Engine members are init-only. Writes are confined to new copies and
    // retain unrelated members, including pointer metadata, from the source.
    internal static void CopyValues(object source, object copy)
    {
        foreach (PropertyInfo member in CopyMembers[source.GetType()])
            member.SetValue(copy, member.GetValue(source));
    }

    internal object? Read(VehicleDefAsset asset)
    {
        object? value = _value.GetValue(Group?.GetValue(asset) ?? asset);
        return value switch
        {
            IReadOnlyList<ScriptStringReference> tags when _index is { } tag =>
                tag < tags.Count ? tags[tag].Text : null,
            IReadOnlyList<VehicleSoundAliasField> surfaces when _index is { } surface =>
                surface < surfaces.Count ? surfaces[surface].Value : null,
            VehicleSoundAliasField sound => sound.Value,
            _ => value
        };
    }

    internal void Write(object owner, object? value)
    {
        if (_index is { } index)
        {
            WriteSlot(owner, index, (string?)value);
            return;
        }

        if (Kind == VehicleFieldKind.SoundAlias)
        {
            var sound = (VehicleSoundAliasField?)_value.GetValue(owner);
            value = NewSoundAlias(sound, (string?)value);
        }

        _value.SetValue(owner, value);
        if (_pointer is not null)
        {
            // Only changed cells lose their imported addresses. The detached
            // linker derives the new XString or provider fixup from the value.
            _pointer.SetValue(owner, Activator.CreateInstance(_pointer.PropertyType));
        }
    }

    private void WriteSlot(object owner, int index, string? value)
    {
        if (_value.GetValue(owner) is IReadOnlyList<ScriptStringReference> tags)
        {
            var revised = tags.ToArray();
            revised[index] = new ScriptStringReference(0, value, ScriptStringHandle.Null, default);
            _value.SetValue(owner, Array.AsReadOnly(revised));
        }
        else if (_value.GetValue(owner) is IReadOnlyList<VehicleSoundAliasField> surfaces)
        {
            var revised = surfaces.ToArray();
            revised[index] = NewSoundAlias(revised[index], value);
            _value.SetValue(owner, Array.AsReadOnly(revised));
        }
    }

    private static VehicleSoundAliasField NewSoundAlias(VehicleSoundAliasField? current, string? value) =>
        new(current?.Offset ?? 0, default(XPointer<string>), default(XPointer<string>), value);

    internal static IReadOnlyList<VehicleField> CreateCatalog()
    {
        var fields = new List<VehicleField>();

        Add("General", null, "Type", "UseHintString", "Health", "QuadBarrel", "TexScrollScale");
        Add("Movement", null, "TopSpeed", "Accel", "RotRate", "RotAccel", "MaxBodyPitch", "MaxBodyRoll");
        Add("Movement", "FakeBody");
        Add("Movement", null, "BoostDuration", "BoostRechargeTime", "BoostAcceleration", "SuspensionTravel",
            "MaxSteeringAngle", "SteeringLerp", "MinSteeringScale", "MinSteeringSpeed");
        Add("Damage", null, "CollisionDamage", "CollisionSpeed", "PlayerProtected", "BulletDamage",
            "ArmorPiercingDamage", "GrenadeDamage", "ProjectileDamage", "ProjectileSplashDamage", "HeavyExplosiveDamage");
        Add("Physics", "Phys");
        Add("Camera", "KillcamOffset");
        Add("Camera", null, "CamLookEnabled", "CamLerp", "CamPitchInfluence",
            "CamRollInfluence", "CamFovIncrease", "CamFovOffset", "CamFovSpeed");
        Add("Turret", null, "TurretWeaponName", "TurretWeapon", "TurretHorizSpanLeft", "TurretHorizSpanRight",
            "TurretVertSpanUp", "TurretVertSpanDown", "TurretRotRate", "TurretSpinSound", "TurretStopSound");
        Add("Trophy", null, "TrophyEnabled", "TrophyRadius", "TrophyInactiveRadius", "TrophyAmmoCount", "TrophyReloadTime");
        PropertyInfo tags = RequireProperty(typeof(VehicleDefAsset), nameof(VehicleDefAsset.TrophyTags));
        for (int index = 0; index < VehicleDefAsset.ScriptStringCount; index++)
            fields.Add(new("Trophy", null, tags, VehicleFieldKind.Text, index: index, label: $"Trophy Tag {index + 1}"));
        Add("Compass", null, "CompassFriendlyIcon", "CompassEnemyIcon", "CompassIconWidth", "CompassIconHeight");
        Add("Audio", "EngineSounds");
        Add("Audio", "SuspensionSounds");
        Add("Audio", null, "CollisionSound", "CollisionBlendSpeed", "SpeedSound", "SpeedSoundBlendSpeed",
            "SurfaceSoundPrefix", "SurfaceSoundBlendSpeed", "SlideVolume", "SlideBlendSpeed", "InAirPitch");
        PropertyInfo sounds = RequireProperty(typeof(VehicleDefAsset), nameof(VehicleDefAsset.SurfaceSoundFields));
        for (int index = 0; index < VehicleDefAsset.SurfaceSoundCount; index++)
        {
            string surface = WordBoundary.Replace(((MaterialSurfaceType)index).ToString(), " ");
            fields.Add(new("Audio", null, sounds, VehicleFieldKind.SoundAlias,
                XAssetType.Sound, index, $"{surface} Surface Sound"));
        }

        return fields.AsReadOnly();

        void Add(string section, string? groupName, params string[] memberNames)
        {
            PropertyInfo? group = groupName is null ? null : RequireProperty(typeof(VehicleDefAsset), groupName);
            Type owner = group?.PropertyType ?? typeof(VehicleDefAsset);
            IEnumerable<PropertyInfo> members = memberNames.Length == 0
                ? owner.GetProperties().Where(property => property.CanWrite && !property.Name.EndsWith("Pointer", StringComparison.Ordinal))
                : memberNames.Select(memberName => RequireProperty(owner, memberName));
            foreach (PropertyInfo member in members)
            {
                Type type = member.PropertyType;
                XAssetType? referenceType = member.Name switch
                {
                    nameof(VehiclePhysDef.PhysPreset) => XAssetType.PhysPreset,
                    nameof(VehicleDefAsset.TurretWeapon) => XAssetType.Weapon,
                    nameof(VehicleDefAsset.CompassFriendlyIcon) or nameof(VehicleDefAsset.CompassEnemyIcon) => XAssetType.Material,
                    _ when type == typeof(VehicleSoundAliasField) => XAssetType.Sound,
                    _ => null
                };
                VehicleFieldKind kind = referenceType switch
                {
                    XAssetType.Sound => VehicleFieldKind.SoundAlias,
                    not null => VehicleFieldKind.AssetReference,
                    _ when type.IsEnum => VehicleFieldKind.Choice,
                    _ when type == typeof(float) => VehicleFieldKind.Float,
                    _ when type == typeof(string) => VehicleFieldKind.Text,
                    _ when type == typeof(int) => IsBoolean(member.Name) ? VehicleFieldKind.Boolean : VehicleFieldKind.Integer,
                    _ => throw new InvalidOperationException($"Unsupported vehicle field {member.Name}.")
                };
                string? label = groupName == nameof(VehicleDefAsset.KillcamOffset) ? $"Killcam Offset {member.Name}" : null;
                fields.Add(new(section, group, member, kind, referenceType, label: label));
            }
        }
    }

    private static bool IsBoolean(string name) => name is
        nameof(VehicleDefAsset.QuadBarrel) or
        nameof(VehicleDefAsset.PlayerProtected) or
        nameof(VehicleDefAsset.BulletDamage) or
        nameof(VehicleDefAsset.ArmorPiercingDamage) or
        nameof(VehicleDefAsset.GrenadeDamage) or
        nameof(VehicleDefAsset.ProjectileDamage) or
        nameof(VehicleDefAsset.ProjectileSplashDamage) or
        nameof(VehicleDefAsset.HeavyExplosiveDamage) or
        nameof(VehiclePhysDef.PhysicsEnabled) or
        nameof(VehicleDefAsset.CamLookEnabled) or
        nameof(VehicleDefAsset.TrophyEnabled);

    private static PropertyInfo RequireProperty(Type owner, string member) =>
        owner.GetProperty(member) ?? throw new InvalidOperationException($"Missing vehicle field {owner.Name}.{member}.");
}
