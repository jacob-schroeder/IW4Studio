using IW4.Assets.Assets.MapEnts;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Assets;

namespace IW4.Studio.Documents;

public sealed class MapEntsBuildData : IMapEntsBuildData
{
    private readonly byte[] _entityBytes;
    private readonly StageBuildData[] _stages;
    private readonly byte[] _pad;
    internal MapEntsBuildData(string? name, ReadOnlySpan<byte> entityBytes, MapTriggersBuildData triggers, IEnumerable<StageBuildData> stages, ReadOnlySpan<byte> pad)
    {
        ArgumentNullException.ThrowIfNull(triggers); Name = name; _entityBytes = entityBytes.ToArray(); Triggers = new MapTriggersBuildData(triggers.Models, triggers.Hulls, triggers.Slabs); _stages = stages.ToArray(); _pad = pad.ToArray();
    }
    public XAssetType AssetType => XAssetType.MapEnts; public string? Name { get; } public MapTriggersBuildData Triggers { get; }
    public IReadOnlyList<StageBuildData> Stages => Array.AsReadOnly(_stages);
    public byte[] GetEntityStringBytesCopy() => _entityBytes.ToArray(); public byte[] GetPad29To2BCopy() => _pad.ToArray();
    public MapEntsBuildData WithEntityStringBytes(
        ReadOnlySpan<byte> entityStringBytes) =>
        new(
            Name,
            entityStringBytes,
            Triggers,
            _stages,
            _pad);
    internal MapEntsBuildData Copy() => new(Name, _entityBytes, Triggers, _stages, _pad);
}

public sealed class AddonMapEntsBuildData : IAddonMapEntsBuildData
{
    private readonly byte[] _entityBytes;
    internal AddonMapEntsBuildData(string? name, ReadOnlySpan<byte> entityBytes, MapTriggersBuildData triggers) { ArgumentNullException.ThrowIfNull(triggers); Name = name; _entityBytes = entityBytes.ToArray(); Triggers = new MapTriggersBuildData(triggers.Models, triggers.Hulls, triggers.Slabs); }
    public XAssetType AssetType => XAssetType.AddonMapEnts; public string? Name { get; } public MapTriggersBuildData Triggers { get; }
    public byte[] GetEntityStringBytesCopy() => _entityBytes.ToArray(); internal AddonMapEntsBuildData Copy() => new(Name, _entityBytes, Triggers);
}

public sealed class MapEntsAuthoredSnapshot : ITargetZoneDetachedSemanticSnapshot
{
    internal MapEntsAuthoredSnapshot(MapEntsBuildData data) => Data = data.Copy();
    internal MapEntsBuildData Data { get; }
    public XAssetType AssetType => XAssetType.MapEnts;
    internal static MapEntsAuthoredSnapshot Import(TargetZoneRowSource source) => source.AuthoredDefinition?.SemanticSnapshot is MapEntsAuthoredSnapshot snapshot
        ? snapshot
        : throw new InvalidDataException("MapEnts requires a capture-time detached semantic baseline because nested source pointers may not own local bytes.");
    internal static MapEntsAuthoredSnapshot FromLoaded(MapEntsAsset asset) => new(new MapEntsBuildData(asset.Name, asset.EntityStringBytes.ToArray(), Convert(asset.Trigger), asset.Stages.Select(Convert), asset.Pad29To2B.ToArray()));
    internal static MapTriggersBuildData Convert(MapTriggers value) => new(value.Models.Select(model => new TriggerModelBuildData(model.Contents, model.HullCount, model.FirstHull)), value.Hulls.Select(hull => new TriggerHullBuildData(Float3(hull.Bounds.MidPoint.X, hull.Bounds.MidPoint.Y, hull.Bounds.MidPoint.Z), Float3(hull.Bounds.HalfSize.X, hull.Bounds.HalfSize.Y, hull.Bounds.HalfSize.Z), hull.Contents, hull.SlabCount, hull.FirstSlab)), value.Slabs.Select(slab => new TriggerSlabBuildData(Float3(slab.Dir.X, slab.Dir.Y, slab.Dir.Z), slab.MidPoint, slab.HalfSize)));
    internal static StageBuildData Convert(Stage value) => new(value.StageName, Float3(value.Origin.X, value.Origin.Y, value.Origin.Z), value.TriggerIndex, value.SunPrimaryLightIndex, value.Pad13);
    internal static Float3BuildData Float3(float x, float y, float z) => new(x, y, z);
}

public sealed class AddonMapEntsAuthoredSnapshot : ITargetZoneDetachedSemanticSnapshot
{
    internal AddonMapEntsAuthoredSnapshot(AddonMapEntsBuildData data) => Data = data.Copy();
    internal AddonMapEntsBuildData Data { get; }
    public XAssetType AssetType => XAssetType.AddonMapEnts;
    internal static AddonMapEntsAuthoredSnapshot Import(TargetZoneRowSource source) => source.AuthoredDefinition?.SemanticSnapshot is AddonMapEntsAuthoredSnapshot snapshot
        ? snapshot
        : throw new InvalidDataException("AddonMapEnts requires a capture-time detached semantic baseline because nested source pointers may not own local bytes.");
    internal static AddonMapEntsAuthoredSnapshot FromLoaded(AddonMapEntsAsset asset) => new(new AddonMapEntsBuildData(asset.Name, asset.EntityStringBytes.ToArray(), MapEntsAuthoredSnapshot.Convert(asset.Trigger)));
}

public sealed class MapEntsDraft
{
    private MapEntsBuildData _data;
    internal MapEntsDraft(MapEntsBuildData data) => _data = data.Copy();
    public string? Name => _data.Name; public byte[] GetEntityStringBytesCopy() => _data.GetEntityStringBytesCopy(); public MapTriggersBuildData Triggers => _data.Triggers; public IReadOnlyList<StageBuildData> Stages => _data.Stages; public byte[] GetPad29To2BCopy() => _data.GetPad29To2BCopy();
    public void ReplaceEntityStringBytes(ReadOnlySpan<byte> value) => _data = _data.WithEntityStringBytes(value);
    public void ReplaceTriggers(MapTriggersBuildData value) { ArgumentNullException.ThrowIfNull(value); _data = new MapEntsBuildData(Name, GetEntityStringBytesCopy(), value, Stages, GetPad29To2BCopy()); }
    public void ReplaceStages(IEnumerable<StageBuildData> value) { ArgumentNullException.ThrowIfNull(value); _data = new MapEntsBuildData(Name, GetEntityStringBytesCopy(), Triggers, value, GetPad29To2BCopy()); }
    internal MapEntsDraft Clone() => new(_data); internal MapEntsBuildData Export() => _data.Copy();
}

public sealed class AddonMapEntsDraft
{
    private AddonMapEntsBuildData _data;
    internal AddonMapEntsDraft(AddonMapEntsBuildData data) => _data = data.Copy();
    public string? Name => _data.Name; public byte[] GetEntityStringBytesCopy() => _data.GetEntityStringBytesCopy(); public MapTriggersBuildData Triggers => _data.Triggers;
    public void ReplaceEntityStringBytes(ReadOnlySpan<byte> value) => _data = new AddonMapEntsBuildData(Name, value, Triggers);
    public void ReplaceTriggers(MapTriggersBuildData value) { ArgumentNullException.ThrowIfNull(value); _data = new AddonMapEntsBuildData(Name, GetEntityStringBytesCopy(), value); }
    internal AddonMapEntsDraft Clone() => new(_data); internal AddonMapEntsBuildData Export() => _data.Copy();
}

public sealed class MapEntsAuthoringAdapter : AssetAuthoringAdapter<MapEntsAuthoredSnapshot, MapEntsDraft, MapEntsBuildData>
{
    private static readonly MapEntsBodyEmitter Validator = new();
    public override XAssetType AssetType => XAssetType.MapEnts; public override MapEntsAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => MapEntsAuthoredSnapshot.Import(source); public override MapEntsDraft CreateDraft(MapEntsAuthoredSnapshot snapshot) => new(snapshot.Data); public override MapEntsDraft CloneDraft(MapEntsDraft draft) => draft.Clone();
    public override IReadOnlyList<AssetValidationIssue> ValidateDraft(MapEntsDraft draft) => Validator.Validate(draft.Export()).Select(value => new AssetValidationIssue(value.Path, value.Message, AssetValidationSeverity.Error)).ToArray();
    public override bool SemanticallyEquals(MapEntsDraft left, MapEntsDraft right) => Same(left.Export(), right.Export());
    public override MapEntsBuildData ExportBuildData(MapEntsDraft draft) { MapEntsBuildData data = draft.Export(); if (Validator.Validate(data).Count != 0) throw new InvalidOperationException("MapEnts draft has validation errors and cannot produce build data."); return data; }
    private static bool Same(MapEntsBuildData left, MapEntsBuildData right) => left.Name == right.Name && left.GetEntityStringBytesCopy().SequenceEqual(right.GetEntityStringBytesCopy()) && left.GetPad29To2BCopy().SequenceEqual(right.GetPad29To2BCopy()) && SameTriggers(left.Triggers, right.Triggers) && left.Stages.SequenceEqual(right.Stages);
    internal static bool SameTriggers(MapTriggersBuildData left, MapTriggersBuildData right) => left.Models.SequenceEqual(right.Models) && left.Hulls.SequenceEqual(right.Hulls) && left.Slabs.SequenceEqual(right.Slabs);
}

public sealed class AddonMapEntsAuthoringAdapter : AssetAuthoringAdapter<AddonMapEntsAuthoredSnapshot, AddonMapEntsDraft, AddonMapEntsBuildData>
{
    private static readonly AddonMapEntsBodyEmitter Validator = new();
    public override XAssetType AssetType => XAssetType.AddonMapEnts; public override AddonMapEntsAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => AddonMapEntsAuthoredSnapshot.Import(source); public override AddonMapEntsDraft CreateDraft(AddonMapEntsAuthoredSnapshot snapshot) => new(snapshot.Data); public override AddonMapEntsDraft CloneDraft(AddonMapEntsDraft draft) => draft.Clone();
    public override IReadOnlyList<AssetValidationIssue> ValidateDraft(AddonMapEntsDraft draft) => Validator.Validate(draft.Export()).Select(value => new AssetValidationIssue(value.Path, value.Message, AssetValidationSeverity.Error)).ToArray();
    public override bool SemanticallyEquals(AddonMapEntsDraft left, AddonMapEntsDraft right) => left.Name == right.Name && left.GetEntityStringBytesCopy().SequenceEqual(right.GetEntityStringBytesCopy()) && MapEntsAuthoringAdapter.SameTriggers(left.Triggers, right.Triggers);
    public override AddonMapEntsBuildData ExportBuildData(AddonMapEntsDraft draft) { AddonMapEntsBuildData data = draft.Export(); if (Validator.Validate(data).Count != 0) throw new InvalidOperationException("AddonMapEnts draft has validation errors and cannot produce build data."); return data; }
}
