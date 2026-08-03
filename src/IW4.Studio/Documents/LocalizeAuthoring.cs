using IW4.Assets.Assets.Localize;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Assets;

namespace IW4.Studio.Documents;

/// <summary>Immutable detached Localize source. Name is display-only because it is row identity.</summary>
public sealed class LocalizeAuthoredSnapshot : ITargetZoneDetachedSemanticSnapshot
{
    internal LocalizeAuthoredSnapshot(string? name, string? value)
    {
        Name = name;
        Value = value;
    }

    public string? Name { get; }
    public string? Value { get; }
    public XAssetType AssetType => XAssetType.Localize;

    internal static LocalizeAuthoredSnapshot Import(TargetZoneRowSource source)
    {
        if (source.SerializedType != XAssetType.Localize || source.State != TargetZoneRowSourceState.Definition ||
            source.AuthoredDefinition?.SemanticSnapshot is not LocalizeAuthoredSnapshot snapshot)
        {
            throw new InvalidDataException("Localize editing requires a capture-time detached semantic snapshot; source-fragment replay is not an authoring input.");
        }
        return new LocalizeAuthoredSnapshot(snapshot.Name, snapshot.Value);
    }

    internal static LocalizeAuthoredSnapshot FromLoaded(LocalizeAsset asset) =>
        new(asset?.Name, asset?.Value);
}

public sealed class LocalizeDraft
{
    internal LocalizeDraft(string? name, string? value)
    {
        Name = name;
        Value = value;
    }

    /// <summary>Locked: changing this string would change the catalog identity/target row name.</summary>
    public string? Name { get; }
    public string? Value { get; private set; }
    public void SetValue(string? value) => Value = value;
    internal LocalizeDraft Clone() => new(Name, Value);
}

public sealed class LocalizeBuildData : ILocalizeBuildData
{
    internal LocalizeBuildData(LocalizeDraft draft)
    {
        Name = draft.Name;
        Value = draft.Value;
    }

    public XAssetType AssetType => XAssetType.Localize;
    public string? Name { get; }
    public string? Value { get; }
}

public sealed class LocalizeAuthoringAdapter : AssetAuthoringAdapter<LocalizeAuthoredSnapshot, LocalizeDraft, LocalizeBuildData>
{
    public override XAssetType AssetType => XAssetType.Localize;
    public override LocalizeAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => LocalizeAuthoredSnapshot.Import(source);
    public override LocalizeDraft CreateDraft(LocalizeAuthoredSnapshot authoredSnapshot) => new(authoredSnapshot.Name, authoredSnapshot.Value);
    public override LocalizeDraft CloneDraft(LocalizeDraft draft) => draft.Clone();

    public override IReadOnlyList<AssetValidationIssue> ValidateDraft(LocalizeDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var issues = new List<AssetValidationIssue>();
        ValidateString(draft.Name, "name", issues);
        ValidateString(draft.Value, "value", issues);
        return Array.AsReadOnly(issues.ToArray());
    }

    public override bool SemanticallyEquals(LocalizeDraft baseline, LocalizeDraft current) =>
        string.Equals(baseline.Name, current.Name, StringComparison.Ordinal) &&
        string.Equals(baseline.Value, current.Value, StringComparison.Ordinal);

    public override LocalizeBuildData ExportBuildData(LocalizeDraft draft)
    {
        if (ValidateDraft(draft).Any(issue => issue.Severity == AssetValidationSeverity.Error))
            throw new InvalidOperationException("Localize draft has validation errors and cannot produce build data.");
        return new LocalizeBuildData(draft);
    }

    internal static void ValidateString(string? value, string path, List<AssetValidationIssue> issues)
    {
        (bool hasEmbeddedNull, bool hasNonLatin1) = InspectString(value);
        if (hasEmbeddedNull)
            issues.Add(new AssetValidationIssue(path, "XString values cannot contain embedded null characters.", AssetValidationSeverity.Error));
        if (hasNonLatin1)
            issues.Add(new AssetValidationIssue(path, "XString values must be representable in Latin-1; no replacement is performed.", AssetValidationSeverity.Error));
    }

    internal static bool IsStringValid(string? value)
    {
        (bool hasEmbeddedNull, bool hasNonLatin1) = InspectString(value);
        return !hasEmbeddedNull && !hasNonLatin1;
    }

    private static (bool HasEmbeddedNull, bool HasNonLatin1) InspectString(
        string? value)
    {
        bool hasEmbeddedNull = false;
        bool hasNonLatin1 = false;
        if (value is null)
            return (hasEmbeddedNull, hasNonLatin1);

        foreach (char character in value)
        {
            hasEmbeddedNull |= character == '\0';
            hasNonLatin1 |= character > byte.MaxValue;
            if (hasEmbeddedNull && hasNonLatin1)
                break;
        }

        return (hasEmbeddedNull, hasNonLatin1);
    }
}
