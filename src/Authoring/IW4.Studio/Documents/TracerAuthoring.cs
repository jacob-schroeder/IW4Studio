using IW4.Game.Assets.Material;
using IW4.Game.Assets.Tracer;
using IW4.Game.Pointers;
using IW4.Game.Zone;
using IW4.Linker.Contracts;

namespace IW4.Studio.Documents;

/// <summary>Detached native fields of one TracerDef.</summary>
public sealed class TracerDraft
{
    private readonly MaterialAsset? _importedMaterial;
    private readonly XPointer<MaterialAsset> _importedMaterialPointer;
    private TracerColor[] _colors;
    private XPointer<string> NamePointer { get; }

    internal TracerDraft(TracerDefAsset asset)
    {
        Name = asset.Name;
        NamePointer = asset.NamePointer;
        Material = _importedMaterial = asset.Material;
        MaterialPointer = _importedMaterialPointer = asset.MaterialPointer;
        DrawInterval = asset.DrawInterval;
        Speed = asset.Speed;
        BeamLength = asset.BeamLength;
        BeamWidth = asset.BeamWidth;
        ScrewRadius = asset.ScrewRadius;
        ScrewDistance = asset.ScrewDistance;
        _colors = asset.Colors?.ToArray() ?? [];
        Colors = Array.AsReadOnly(_colors);
    }

    public string? Name { get; }
    public MaterialAsset? Material { get; private set; }
    public XPointer<MaterialAsset> MaterialPointer { get; private set; }
    public uint DrawInterval { get; set; }
    public float Speed { get; set; }
    public float BeamLength { get; set; }
    public float BeamWidth { get; set; }
    public float ScrewRadius { get; set; }
    public float ScrewDistance { get; set; }
    public IReadOnlyList<TracerColor> Colors { get; private set; }

    public void SetMaterial(MaterialAsset? value)
    {
        if (LogicalMaterialName(_importedMaterial) is { } importedName &&
            string.Equals(importedName, LogicalMaterialName(value), StringComparison.OrdinalIgnoreCase))
        {
            Material = _importedMaterial;
            MaterialPointer = _importedMaterialPointer;
            return;
        }
        if (Material is not null && value is not null && SameMaterial(Material, value))
            return;

        Material = value;
        MaterialPointer = value is null ? default : new XPointer<MaterialAsset>(-1, XPointerResolutionMode.AliasCell);
    }

    public void SetColor(int index, TracerColor color) => _colors[index] = color;

    public TracerDraft Clone()
    {
        var clone = (TracerDraft)MemberwiseClone();
        clone._colors = _colors.ToArray();
        clone.Colors = Array.AsReadOnly(clone._colors);
        return clone;
    }

    public TracerDefAsset ToAsset() => new()
    {
        Name = Name,
        NamePointer = NamePointer,
        Material = Material,
        MaterialPointer = MaterialPointer,
        DrawInterval = DrawInterval,
        Speed = Speed,
        BeamLength = BeamLength,
        BeamWidth = BeamWidth,
        ScrewRadius = ScrewRadius,
        ScrewDistance = ScrewDistance,
        Colors = Array.AsReadOnly(_colors.ToArray())
    };

    public bool Matches(TracerDraft other) =>
        Name == other.Name && SameMaterialReference(other) &&
        DrawInterval == other.DrawInterval && SameBits(Speed, other.Speed) &&
        SameBits(BeamLength, other.BeamLength) && SameBits(BeamWidth, other.BeamWidth) &&
        SameBits(ScrewRadius, other.ScrewRadius) && SameBits(ScrewDistance, other.ScrewDistance) &&
        _colors.Length == other._colors.Length &&
        _colors.Zip(other._colors).All(pair => SameColor(pair.First, pair.Second));

    // Merge only the fields that changed in the local editor.
    public void ApplyEdits(TracerDraft before, TracerDraft after)
    {
        if (!before.SameMaterialReference(after))
        {
            Material = after.Material;
            MaterialPointer = after.MaterialPointer;
        }
        if (before.DrawInterval != after.DrawInterval) DrawInterval = after.DrawInterval;
        Speed = Merge(before.Speed, after.Speed, Speed);
        BeamLength = Merge(before.BeamLength, after.BeamLength, BeamLength);
        BeamWidth = Merge(before.BeamWidth, after.BeamWidth, BeamWidth);
        ScrewRadius = Merge(before.ScrewRadius, after.ScrewRadius, ScrewRadius);
        ScrewDistance = Merge(before.ScrewDistance, after.ScrewDistance, ScrewDistance);
        for (int index = 0; index < _colors.Length; index++)
        {
            TracerColor baseline = before._colors[index];
            TracerColor edited = after._colors[index];
            TracerColor current = _colors[index];
            _colors[index] = new TracerColor(
                Merge(baseline.Red, edited.Red, current.Red),
                Merge(baseline.Green, edited.Green, current.Green),
                Merge(baseline.Blue, edited.Blue, current.Blue),
                Merge(baseline.Alpha, edited.Alpha, current.Alpha));
        }
    }

    private bool SameMaterialReference(TracerDraft other) =>
        MaterialPointer == other.MaterialPointer && SameMaterial(Material, other.Material);

    private static bool SameMaterial(MaterialAsset? left, MaterialAsset? right) =>
        ReferenceEquals(left, right) ||
        (left?.SerializedAssetName is { } name && name == right?.SerializedAssetName);

    private static string? LogicalMaterialName(MaterialAsset? material) =>
        material?.SerializedAssetName is { Length: > 0 } name
            ? (name[0] == ',' ? name[1..] : name).Replace('\\', '/')
            : null;

    private static bool SameColor(TracerColor left, TracerColor right) =>
        SameBits(left.Red, right.Red) && SameBits(left.Green, right.Green) &&
        SameBits(left.Blue, right.Blue) && SameBits(left.Alpha, right.Alpha);

    private static float Merge(float before, float after, float current) =>
        SameBits(before, after) ? current : after;

    private static bool SameBits(float left, float right) =>
        BitConverter.SingleToInt32Bits(left) == BitConverter.SingleToInt32Bits(right);
}

internal sealed class TracerAdapter : AssetAuthoringAdapter<TracerDefAsset, TracerDraft>
{
    public override XAssetType AssetType => XAssetType.Tracer;
    public override TracerDraft CreateDraft(TracerDefAsset asset) => new(asset);
    public override TracerDraft CloneDraft(TracerDraft draft) => draft.Clone();
    public override TracerDefAsset CreateDefinition(TracerDraft draft) => draft.ToAsset();
    public override bool SemanticallyEquals(TracerDraft left, TracerDraft right) => left.Matches(right);

    public override IReadOnlyList<AssetValidationIssue> Validate(TracerDraft draft)
    {
        var issues = new List<AssetValidationIssue>();
        Finite(nameof(draft.Speed), draft.Speed);
        Finite(nameof(draft.BeamLength), draft.BeamLength);
        Finite(nameof(draft.BeamWidth), draft.BeamWidth);
        Finite(nameof(draft.ScrewRadius), draft.ScrewRadius);
        Finite(nameof(draft.ScrewDistance), draft.ScrewDistance);
        if (draft.Colors.Count != TracerDefAsset.ColorCount)
            Error(nameof(draft.Colors), $"Expected {TracerDefAsset.ColorCount} RGBA rows.");
        for (int index = 0; index < draft.Colors.Count; index++)
        {
            TracerColor color = draft.Colors[index];
            string row = $"{nameof(draft.Colors)}[{index}]";
            Finite($"{row}.{nameof(color.Red)}", color.Red);
            Finite($"{row}.{nameof(color.Green)}", color.Green);
            Finite($"{row}.{nameof(color.Blue)}", color.Blue);
            Finite($"{row}.{nameof(color.Alpha)}", color.Alpha);
        }

        if (draft.MaterialPointer.Raw != 0 &&
            draft.MaterialPointer.ResolutionMode != XPointerResolutionMode.AliasCell)
        {
            Error(nameof(draft.Material), "The material pointer must use AliasCell resolution.");
        }
        if (draft.Material is { } material)
        {
            try
            {
                _ = AssetKey.FromDefinition(material);
            }
            catch (ArgumentException exception)
            {
                Error(nameof(draft.Material), exception.Message);
            }
        }
        else if (draft.MaterialPointer.Raw != 0)
        {
            Error(nameof(draft.Material), "The material pointer has no named provider.");
        }
        return issues;

        void Finite(string field, float value)
        {
            if (!float.IsFinite(value))
                Error(field, "Enter a finite number.");
        }

        void Error(string field, string message) =>
            issues.Add(new AssetValidationIssue(field, message, AssetValidationSeverity.Error));
    }
}
