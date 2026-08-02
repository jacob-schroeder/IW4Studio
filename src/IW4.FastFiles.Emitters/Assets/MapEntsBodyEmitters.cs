using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

public sealed class MapEntsBodyEmitter : MapEntsBodyEmitterBase
{
    public override XAssetType AssetType => XAssetType.MapEnts;
    protected override bool HasStages => true;
    protected override MapEmissionInput GetInput(IXAssetBuildData value) => value is IMapEntsBuildData data
        ? new(data.Name, data.GetEntityStringBytesCopy(), data.Triggers, data.Stages, data.GetPad29To2BCopy())
        : throw new InvalidDataException("MapEnts build data does not implement IMapEntsBuildData.");
}

public sealed class AddonMapEntsBodyEmitter : MapEntsBodyEmitterBase
{
    public override XAssetType AssetType => XAssetType.AddonMapEnts;
    protected override bool HasStages => false;
    protected override MapEmissionInput GetInput(IXAssetBuildData value) => value is IAddonMapEntsBuildData data
        ? new(data.Name, data.GetEntityStringBytesCopy(), data.Triggers, [], [])
        : throw new InvalidDataException("AddonMapEnts build data does not implement IAddonMapEntsBuildData.");
}

public abstract class MapEntsBodyEmitterBase : IXAssetBodyEmitter
{
    protected sealed record MapEmissionInput(string? Name, byte[] EntityBytes, MapTriggersBuildData Triggers, IReadOnlyList<StageBuildData> Stages, byte[] Pad);
    public abstract XAssetType AssetType { get; }
    protected abstract bool HasStages { get; }
    protected abstract MapEmissionInput GetInput(IXAssetBuildData value);

    public IReadOnlyList<EmissionError> Validate(IXAssetBuildData buildData, int? rowIndex = null)
    {
        var diagnostics = AssetBodyEmitterHelpers.ValidateIdentity(buildData, AssetType, rowIndex);
        MapEmissionInput data;
        try { data = GetInput(buildData); }
        catch (InvalidDataException exception) { diagnostics.Add(new("body", exception.Message, rowIndex, AssetType)); return diagnostics; }
        if (data.Name is { } name && !AssetBodyEmitterHelpers.IsLatin1CString(name)) diagnostics.Add(new("name", "MapEnts name must be a Latin-1 C string.", rowIndex, AssetType));
        if (data.EntityBytes.Length > 0x4000000) diagnostics.Add(new("entityBytes", "Entity bytes exceed the bounded source-emission limit.", rowIndex, AssetType));
        ValidateTriggers(data.Triggers, diagnostics, rowIndex);
        if (HasStages)
        {
            if (data.Stages.Count > byte.MaxValue) diagnostics.Add(new("stages", "MapEnts StageCount is an unsigned byte.", rowIndex, AssetType));
            if (data.Pad.Length != 3) diagnostics.Add(new("pad29To2B", "MapEnts must preserve exactly three tail padding bytes.", rowIndex, AssetType));
            for (int index = 0; index < data.Stages.Count; index++)
            {
                StageBuildData stage = data.Stages[index];
                if (stage.Name is { } stageName && !AssetBodyEmitterHelpers.IsLatin1CString(stageName)) diagnostics.Add(new($"stages[{index}].name", "Stage name must be a Latin-1 C string.", rowIndex, AssetType));
                // The Stage field is an opaque uint16 trigger identity, not an
                // index into MapTriggers.models.
            }
        }
        return diagnostics;
    }

    public AssetBodyEmission Plan(IXAssetBuildData buildData, EmissionPlan plan, int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(plan); AssetBodyEmitterHelpers.RequireNoDiagnostics(Validate(buildData, rowIndex));
        MapEmissionInput data = GetInput(buildData); var segments = new List<EmissionBlockSegment>();
        int rootSize = HasStages ? 0x2C : 0x24;
        plan.Push(XFileBlockType.TEMP); EmissionAddress root = plan.Allocate(rootSize, 4); plan.Push(XFileBlockType.LARGE);
        PlannedString? name = AssetBodyEmitterHelpers.PlanString(data.Name, plan, segments, plan.StringAliases);
        EmissionAddress? entity = data.EntityBytes.Length == 0 ? null : plan.Allocate(data.EntityBytes.Length);
        if (entity is { } entityAddress) segments.Add(new(entityAddress, data.EntityBytes));
        EmissionAddress? models = AllocateTable(data.Triggers.Models.Count, 0x08, plan);
        EmissionAddress? hulls = AllocateTable(data.Triggers.Hulls.Count, 0x20, plan);
        EmissionAddress? slabs = AllocateTable(data.Triggers.Slabs.Count, 0x14, plan);
        EmissionAddress? stages = HasStages ? AllocateTable(data.Stages.Count, 0x14, plan) : null;
        PlannedString?[] stageNames = HasStages ? data.Stages.Select(stage => AssetBodyEmitterHelpers.PlanString(stage.Name, plan, segments, plan.StringAliases)).ToArray() : [];
        plan.Pop(XFileBlockType.LARGE); plan.Pop(XFileBlockType.TEMP);
        if (models is { } address) segments.Add(new(address, WriteModels(data.Triggers.Models)));
        if (hulls is { } address2) segments.Add(new(address2, WriteHulls(data.Triggers.Hulls)));
        if (slabs is { } address3) segments.Add(new(address3, WriteSlabs(data.Triggers.Slabs)));
        if (stages is { } address4) segments.Add(new(address4, WriteStages(data.Stages, stageNames)));
        var rootWriter = new XSourceWriter();
        rootWriter.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(name)); rootWriter.WriteInt32(entity is null ? 0 : -1); rootWriter.WriteInt32(data.EntityBytes.Length);
        rootWriter.WriteUInt32((uint)data.Triggers.Models.Count); rootWriter.WriteInt32(models is null ? 0 : -1);
        rootWriter.WriteUInt32((uint)data.Triggers.Hulls.Count); rootWriter.WriteInt32(hulls is null ? 0 : -1);
        rootWriter.WriteUInt32((uint)data.Triggers.Slabs.Count); rootWriter.WriteInt32(slabs is null ? 0 : -1);
        if (HasStages) { rootWriter.WriteInt32(stages is null ? 0 : -1); rootWriter.WriteByte((byte)data.Stages.Count); rootWriter.WriteBytes(data.Pad); }
        segments.Add(new(root, rootWriter.ToArray()));
        return new AssetBodyEmission(AssetType, root, segments);
    }

    private static EmissionAddress? AllocateTable(int count, int stride, EmissionPlan plan) => count == 0 ? null : plan.Allocate(checked(count * stride), 4);
    private static byte[] WriteModels(IReadOnlyList<TriggerModelBuildData> values) { var writer = new XSourceWriter(); foreach (var value in values) { writer.WriteInt32(value.Contents); writer.WriteUInt16(value.HullCount); writer.WriteUInt16(value.FirstHull); } return writer.ToArray(); }
    private static byte[] WriteHulls(IReadOnlyList<TriggerHullBuildData> values) { var writer = new XSourceWriter(); foreach (var value in values) { WriteFloat3(writer, value.MidPoint); WriteFloat3(writer, value.HalfSize); writer.WriteInt32(value.Contents); writer.WriteUInt16(value.SlabCount); writer.WriteUInt16(value.FirstSlab); } return writer.ToArray(); }
    private static byte[] WriteSlabs(IReadOnlyList<TriggerSlabBuildData> values) { var writer = new XSourceWriter(); foreach (var value in values) { WriteFloat3(writer, value.Dir); writer.WriteSingle(value.MidPoint); writer.WriteSingle(value.HalfSize); } return writer.ToArray(); }
    private static byte[] WriteStages(IReadOnlyList<StageBuildData> values, IReadOnlyList<PlannedString?> names) { var writer = new XSourceWriter(); for (int index = 0; index < values.Count; index++) { StageBuildData value = values[index]; writer.WriteInt32(AssetBodyEmitterHelpers.SourcePointer(names[index])); WriteFloat3(writer, value.Origin); writer.WriteUInt16(value.TriggerIndex); writer.WriteByte(value.SunPrimaryLightIndex); writer.WriteByte(value.Pad13); } return writer.ToArray(); }
    private static void WriteFloat3(XSourceWriter writer, Float3BuildData value) { writer.WriteSingle(value.X); writer.WriteSingle(value.Y); writer.WriteSingle(value.Z); }
    private static void ValidateTriggers(MapTriggersBuildData triggers, List<EmissionError> diagnostics, int? rowIndex)
    {
        for (int index = 0; index < triggers.Models.Count; index++) { TriggerModelBuildData value = triggers.Models[index]; if ((uint)value.FirstHull + value.HullCount > triggers.Hulls.Count) diagnostics.Add(new($"triggers.models[{index}]", "Model hull range exceeds the ordered hull table.", rowIndex, XAssetType.MapEnts)); }
        for (int index = 0; index < triggers.Hulls.Count; index++) { TriggerHullBuildData value = triggers.Hulls[index]; if ((uint)value.FirstSlab + value.SlabCount > triggers.Slabs.Count) diagnostics.Add(new($"triggers.hulls[{index}]", "Hull slab range exceeds the ordered slab table.", rowIndex, XAssetType.MapEnts)); }
    }
}
