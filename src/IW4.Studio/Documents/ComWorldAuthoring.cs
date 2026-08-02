using System.Text.Json;
using IW4.Assets.Assets.ComWorld;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Assets;

namespace IW4.Studio.Documents;

/// <summary>Detached ComMap authoring state.  It contains source-authored
/// light parameters and XStrings only, never renderer light-grid state.</summary>
public sealed class ComWorldAuthoredSnapshot : ITargetZoneDetachedSemanticSnapshot
{
    internal ComWorldAuthoredSnapshot(ComWorldBuildData data) => Data = data.Copy();

    internal ComWorldBuildData Data { get; }

    public XAssetType AssetType => XAssetType.ComMap;

    internal static ComWorldAuthoredSnapshot Import(TargetZoneRowSource source) =>
        source.AuthoredDefinition?.SemanticSnapshot is ComWorldAuthoredSnapshot snapshot
            ? snapshot
            : throw new InvalidDataException("ComMap editing requires a capture-time detached semantic snapshot.");

    internal static ComWorldAuthoredSnapshot FromLoaded(ComWorldAsset asset) =>
        new(ComWorldBuildData.FromLoaded(asset));
}

public sealed class ComWorldBuildData : IComWorldBuildData
{
    private readonly ComPrimaryLightBuildData[] _primaryLights;

    public ComWorldBuildData(string? name, int isInUse, IEnumerable<ComPrimaryLightBuildData> primaryLights)
    {
        ArgumentNullException.ThrowIfNull(primaryLights);
        Name = name;
        IsInUse = isInUse;
        _primaryLights = primaryLights.ToArray();
    }

    public XAssetType AssetType => XAssetType.ComMap;
    public string? Name { get; }
    public int IsInUse { get; }
    public IReadOnlyList<ComPrimaryLightBuildData> PrimaryLights => Array.AsReadOnly(_primaryLights);

    internal ComWorldBuildData Copy() => new(Name, IsInUse, _primaryLights);

    internal static ComWorldBuildData FromLoaded(ComWorldAsset asset) => new(
        asset.Name,
        asset.IsInUse,
        asset.PrimaryLights.Select(light => new ComPrimaryLightBuildData(
            light.Type,
            light.CanUseShadowMap,
            light.Exponent,
            light.Unused,
            new Float3BuildData(light.Color.X, light.Color.Y, light.Color.Z),
            new Float3BuildData(light.Dir.X, light.Dir.Y, light.Dir.Z),
            new Float3BuildData(light.Origin.X, light.Origin.Y, light.Origin.Z),
            light.Radius,
            light.CosHalfFovOuter,
            light.CosHalfFovInner,
            light.CosHalfFovExpanded,
            light.RotationLimit,
            light.TranslationLimit,
            light.DefName)));
}

/// <summary>Purpose-built draft for the ComMap light table.  Name remains
/// immutable because it is the serialized top-level row identity.</summary>
public sealed class ComWorldDraft
{
    private ComPrimaryLightBuildData[] _primaryLights;

    internal ComWorldDraft(ComWorldBuildData data)
    {
        Name = data.Name;
        IsInUse = data.IsInUse;
        _primaryLights = data.PrimaryLights.ToArray();
    }

    public string? Name { get; }
    public int IsInUse { get; private set; }
    public IReadOnlyList<ComPrimaryLightBuildData> PrimaryLights => Array.AsReadOnly(_primaryLights.ToArray());

    public void SetIsInUse(int value) => IsInUse = value;

    public void ReplacePrimaryLights(IEnumerable<ComPrimaryLightBuildData> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _primaryLights = values.ToArray();
    }

    public void SetPrimaryLight(int index, ComPrimaryLightBuildData value)
    {
        if ((uint)index >= (uint)_primaryLights.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        _primaryLights[index] = value;
    }

    internal ComWorldDraft Clone() => new(new ComWorldBuildData(Name, IsInUse, _primaryLights));
    internal ComWorldBuildData ToBuildData() => new(Name, IsInUse, _primaryLights);
}

public sealed class ComWorldAuthoringAdapter
    : AssetAuthoringAdapter<ComWorldAuthoredSnapshot, ComWorldDraft, ComWorldBuildData>
{
    private static readonly ComWorldBodyEmitter Validator = new();

    public override XAssetType AssetType => XAssetType.ComMap;
    public override ComWorldAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => ComWorldAuthoredSnapshot.Import(source);
    public override ComWorldDraft CreateDraft(ComWorldAuthoredSnapshot snapshot) => new(snapshot.Data);
    public override ComWorldDraft CloneDraft(ComWorldDraft draft) => draft.Clone();
    public override IReadOnlyList<AssetValidationIssue> ValidateDraft(ComWorldDraft draft) =>
        Validator.Validate(draft.ToBuildData()).Select(issue => new AssetValidationIssue(issue.Path, issue.Message, AssetValidationSeverity.Error)).ToArray();
    public override bool SemanticallyEquals(ComWorldDraft left, ComWorldDraft right) =>
        JsonSerializer.Serialize(left.ToBuildData()) == JsonSerializer.Serialize(right.ToBuildData());

    public override ComWorldBuildData ExportBuildData(ComWorldDraft draft)
    {
        ComWorldBuildData data = draft.ToBuildData();
        if (Validator.Validate(data).Count != 0)
            throw new InvalidOperationException("ComMap draft has validation errors and cannot produce build data.");
        return data;
    }
}
