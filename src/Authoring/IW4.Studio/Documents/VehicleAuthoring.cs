using IW4.Game.Assets;
using IW4.Game.Assets.Vehicle;
using IW4.Game.Zone;
using IW4.Linker.Contracts;

namespace IW4.Studio.Documents;

/// <summary>Detached edits to a VehicleDef, with unchanged native values retained verbatim.</summary>
public sealed class VehicleDraft
{
    private readonly VehicleDefAsset _source;
    private readonly Dictionary<VehicleField, object?> _edits;

    internal VehicleDraft(VehicleDefAsset source)
        : this(source, [])
    {
    }

    private VehicleDraft(VehicleDefAsset source, Dictionary<VehicleField, object?> edits)
    {
        _source = source;
        _edits = edits;
    }

    public static IReadOnlyList<VehicleField> Fields { get; } = VehicleField.CreateCatalog();

    private static readonly Dictionary<string, VehicleField> FieldsByPath =
        Fields.ToDictionary(field => field.Path, StringComparer.Ordinal);

    public VehicleDraft Clone() => new(_source, new(_edits));

    public object? GetValue(VehicleField field)
    {
        object? native = Read(field);
        return field.Kind switch
        {
            VehicleFieldKind.Boolean => native is int flag && flag != 0,
            VehicleFieldKind.Choice => native?.ToString(),
            VehicleFieldKind.AssetReference => (native as BaseAsset)?.SerializedAssetName?.TrimStart(','),
            _ => native
        };
    }

    public void SetValue(VehicleField field, object? value)
    {
        object? native = (field.Kind, value) switch
        {
            (VehicleFieldKind.Text or VehicleFieldKind.SoundAlias, null or "") =>
                field.Read(_source) is "" ? string.Empty : null,
            (VehicleFieldKind.Text or VehicleFieldKind.SoundAlias, string text) => text,
            (VehicleFieldKind.Integer, int integer) => integer,
            (VehicleFieldKind.Float, float number) => number,
            (VehicleFieldKind.Boolean, bool flag) => flag
                ? (field.Read(_source) is int original && original != 0 ? original : 1)
                : 0,
            (VehicleFieldKind.Choice, string option) when field.Options.Contains(option) =>
                Enum.Parse(field.NativeType, option),
            _ => throw new ArgumentException($"Invalid value for {field.Label}.", nameof(value))
        };
        SetNative(field, native);
    }

    internal void SetReference(VehicleField field, BaseAsset? definition)
    {
        if (field.Kind != VehicleFieldKind.AssetReference ||
            (definition is not null && definition.SerializedAssetType != field.ReferenceType))
        {
            throw new ArgumentException($"Invalid reference for {field.Label}.", nameof(definition));
        }

        AssetKey? key = definition is null ? null : AssetKey.FromDefinition(definition);
        SetNative(field, definition);
        string? nameFieldPath = field.Path switch
        {
            "Phys.PhysPreset" => "Phys.PhysPresetName",
            "TurretWeapon" => "TurretWeaponName",
            _ => null
        };
        if (nameFieldPath is not null)
        {
            VehicleField nameField = FieldsByPath[nameFieldPath];
            string? name = definition?.SerializedAssetName?.TrimStart(',');
            if (key is null || !MatchesName(key.Value, Read(nameField) as string))
                SetNative(nameField, name);
        }
    }

    public void ApplyEdits(VehicleDraft before, VehicleDraft after)
    {
        foreach (VehicleField field in Fields)
        {
            object? proposed = after.Read(field);
            if (!Same(before.Read(field), proposed))
                SetNative(field, proposed);
        }
    }

    internal bool Matches(VehicleDraft other) =>
        Fields.All(field => Same(Read(field), other.Read(field)));

    internal VehicleDefAsset ToAsset()
    {
        var copy = new VehicleDefAsset();
        VehicleField.CopyValues(_source, copy);
        foreach (var group in _edits.GroupBy(edit => edit.Key.Group))
        {
            object owner = copy;
            if (group.Key is { } groupProperty)
            {
                object original = groupProperty.GetValue(_source) ??
                    throw new InvalidDataException($"Vehicle.{groupProperty.Name} is missing.");
                owner = Activator.CreateInstance(groupProperty.PropertyType) ??
                    throw new InvalidDataException($"Vehicle.{groupProperty.Name} cannot be copied.");
                VehicleField.CopyValues(original, owner);
                groupProperty.SetValue(copy, owner);
            }
            foreach ((VehicleField field, object? value) in group)
                field.Write(owner, value);
        }
        return copy;
    }

    internal IReadOnlyList<AssetValidationIssue> Validate()
    {
        var issues = new List<AssetValidationIssue>();
        foreach ((VehicleField field, object? value) in _edits)
        {
            string? message = value switch
            {
                float number when !float.IsFinite(number) => "Enter a finite number.",
                string text when text.Contains('\0') => "Text cannot contain NUL.",
                string text when text.Any(character => character > byte.MaxValue) =>
                    "Text must be representable as Latin-1.",
                string alias when field.Kind == VehicleFieldKind.SoundAlias &&
                    !IsValidSoundAlias(alias) => "Enter a valid sound alias name.",
                _ => null
            };
            if (message is not null)
                issues.Add(new AssetValidationIssue(field.Path, message, AssetValidationSeverity.Error));
        }
        return issues;
    }

    private object? Read(VehicleField field) =>
        _edits.TryGetValue(field, out object? value) ? value : field.Read(_source);

    private void SetNative(VehicleField field, object? value)
    {
        if (Same(field.Read(_source), value))
            _edits.Remove(field);
        else
            _edits[field] = value;
    }

    private static bool Same(object? left, object? right) => (left, right) switch
    {
        (float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b),
        (BaseAsset a, BaseAsset b) => ReferenceEquals(a, b) ||
            a.SerializedAssetType == b.SerializedAssetType &&
            MatchesName(AssetKey.FromDefinition(a), b.SerializedAssetName),
        _ => Equals(left, right)
    };

    private static bool MatchesName(AssetKey key, string? name)
    {
        if (name is null)
            return false;
        try
        {
            return AssetKey.FromWireName(key.Family, name) == key;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsValidSoundAlias(string name)
    {
        try
        {
            _ = AssetKey.FromWireName(CanonicalAssetFamily.FromSerializedType(XAssetType.Sound), name);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

internal sealed class VehicleAdapter : AssetAuthoringAdapter<VehicleDefAsset, VehicleDraft>
{
    public override XAssetType AssetType => XAssetType.Vehicle;
    public override VehicleDraft CreateDraft(VehicleDefAsset definition) => new(definition);
    public override VehicleDraft CloneDraft(VehicleDraft draft) => draft.Clone();
    public override VehicleDefAsset CreateDefinition(VehicleDraft draft) => draft.ToAsset();
    public override bool SemanticallyEquals(VehicleDraft left, VehicleDraft right) => left.Matches(right);
    public override IReadOnlyList<AssetValidationIssue> Validate(VehicleDraft draft) => draft.Validate();
}
