using System.Text.Json;
using IW4.Assets.Assets.Fx;
using IW4.Assets.Assets.ImpactFx;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Assets;

namespace IW4.Studio.Documents;

/// <summary>Detached effect graph snapshots. The source representation carries
/// no runtime pointers or derived render/playback handles.</summary>
public sealed class FxAuthoredSnapshot : ITargetZoneDetachedSemanticSnapshot
{
    internal FxAuthoredSnapshot(FxEffectDefBuildData data) => Data = data.Copy();
    internal FxEffectDefBuildData Data { get; }
    public XAssetType AssetType => XAssetType.Fx;
    internal static FxAuthoredSnapshot Import(TargetZoneRowSource source) =>
        source.AuthoredDefinition?.SemanticSnapshot is FxAuthoredSnapshot value
            ? value
            : throw new InvalidDataException(
                "Fx editing requires a capture-time detached semantic snapshot because element pointers can be aliases.");
    internal static FxAuthoredSnapshot FromLoaded(FxEffectDefAsset value) =>
        FromLoaded(value, new DetachedAssetSemanticGraphClone());
    internal static FxAuthoredSnapshot FromLoaded(
        FxEffectDefAsset value,
        DetachedAssetSemanticGraphClone graph) =>
        new(FxEffectDefBuildData.FromLoaded(value, graph));
}
public sealed class FxEffectDefBuildData : IFxEffectDefBuildData
{
    private readonly FxElementBuildData[] _elements;
    internal FxEffectDefBuildData(string? name, int flags, int totalSize, int msecLoopingLife, int looping, int oneShot, int emission, IEnumerable<FxElementBuildData> elements) { Name = name; Flags = flags; TotalSize = totalSize; MsecLoopingLife = msecLoopingLife; ElemDefCountLooping = looping; ElemDefCountOneShot = oneShot; ElemDefCountEmission = emission; _elements = elements.ToArray(); }
    public XAssetType AssetType => XAssetType.Fx; public string? Name { get; } public int Flags { get; } public int TotalSize { get; } public int MsecLoopingLife { get; } public int ElemDefCountLooping { get; } public int ElemDefCountOneShot { get; } public int ElemDefCountEmission { get; } public IReadOnlyList<FxElementBuildData> Elements => Array.AsReadOnly(_elements); internal FxEffectDefBuildData Copy() => new(Name, Flags, TotalSize, MsecLoopingLife, ElemDefCountLooping, ElemDefCountOneShot, ElemDefCountEmission, _elements);
    internal static FxEffectDefBuildData FromLoaded(FxEffectDefAsset value) =>
        FromLoaded(value, new DetachedAssetSemanticGraphClone());
    internal static FxEffectDefBuildData FromLoaded(
        FxEffectDefAsset value,
        DetachedAssetSemanticGraphClone graph) =>
        new(
            value.Name,
            value.Flags,
            value.TotalSize,
            value.MsecLoopingLife,
            value.ElemDefCountLooping,
            value.ElemDefCountOneShot,
            value.ElemDefCountEmission,
            value.ElemDefs.Select(element => Element(element, graph)));
    private static FxElementBuildData Element(
        FxElemDef value,
        DetachedAssetSemanticGraphClone graph) =>
        new(
            value.Flags,
            new(value.Spawn.LoopingIntervalMsec, value.Spawn.Count),
            Range(value.SpawnRange),
            Range(value.FadeInRange),
            Range(value.FadeOutRange),
            value.SpawnFrustumCullRadius,
            new(value.SpawnDelayMsec.Base, value.SpawnDelayMsec.Amplitude),
            new(value.LifeSpanMsec.Base, value.LifeSpanMsec.Amplitude),
            value.SpawnOrigin.Select(Range).ToArray(),
            Range(value.SpawnOffsetRadius),
            Range(value.SpawnOffsetHeight),
            value.SpawnAngles.Select(Range).ToArray(),
            value.AngularVelocity.Select(Range).ToArray(),
            Range(value.InitialRotation),
            Range(value.Gravity),
            Range(value.ReflectionFactor),
            new(
                value.Atlas.Behavior,
                value.Atlas.Index,
                value.Atlas.Fps,
                value.Atlas.LoopCount,
                value.Atlas.ColIndexBits,
                value.Atlas.RowIndexBits,
                value.Atlas.EntryCount),
            (byte)value.ElemType,
            value.VisualCount,
            value.VelIntervalCount,
            value.VisStateIntervalCount,
            value.VelSamples.Select(Velocity).ToArray(),
            value.VisSamples.Select(VisualSample).ToArray(),
            Visuals(value, graph).ToArray(),
            value.MarkVisualArray.Select(mark => Mark(mark, graph)).ToArray(),
            Bounds(value.CollBounds),
            Link(XAssetType.Fx, value.EffectOnImpact.Name),
            Link(XAssetType.Fx, value.EffectOnDeath.Name),
            Link(XAssetType.Fx, value.EffectEmitted.Name),
            Range(value.EmitDist),
            Range(value.EmitDistVariance),
            Extended(value.Extended),
            value.SortOrder,
            value.LightingFrac,
            value.UseItemClip,
            value.FadeInfo);
    private static IEnumerable<FxVisualBuildData> Visuals(
        FxElemDef value,
        DetachedAssetSemanticGraphClone graph)
    {
        if (value.ElemType == FxElemType.Decal)
            return [];
        IEnumerable<FxElemDefVisuals> values =
            value.VisualArray.Count == 0 ? [value.Visuals] : value.VisualArray;
        return values.Select(visual => Visual(visual, graph));
    }
    private static FxVisualBuildData Visual(
        FxElemDefVisuals value,
        DetachedAssetSemanticGraphClone graph) =>
        value.Visual switch
        {
            FxMaterialVisual material => new(
                FxVisualBuildKind.Material,
                materialReference: External(
                    XAssetType.Material,
                    material.IncomingMaterial?.Info.Name ??
                    material.Material?.Info.Name),
                materialLink: MaterialLink(
                    material.MaterialPointer.Untyped,
                    material.IncomingMaterial,
                    material.Material,
                    graph)),
            FxModelVisual model => new(
                FxVisualBuildKind.Model,
                modelReference: External(
                    XAssetType.XModel,
                    model.IncomingModel?.Name ?? model.Model?.Name),
                modelLink: XModelLink(
                    model.ModelPointer.Untyped,
                    model.IncomingModel,
                    model.Model,
                    graph)),
            FxSoundVisual sound => new(
                FxVisualBuildKind.Sound,
                soundReference: Link(XAssetType.Sound, sound.SoundName)),
            FxEffectVisual effect => new(
                FxVisualBuildKind.Effect,
                effectReference: Link(XAssetType.Fx, effect.EffectDef.Name)),
            FxNoChildVisual noChild => new(
                FxVisualBuildKind.NoChild,
                reserved: noChild.Reserved),
            _ => throw new InvalidDataException(
                "Fx visual union has no detached authoring arm.")
        };
    private static FxMarkVisualBuildData Mark(
        FxElemMarkVisuals value,
        DetachedAssetSemanticGraphClone graph) =>
        new(
            External(
                XAssetType.Material,
                value.IncomingMaterial0?.Info.Name ?? value.Material0?.Info.Name),
            External(
                XAssetType.Material,
                value.IncomingMaterial1?.Info.Name ?? value.Material1?.Info.Name),
            MaterialLink(
                value.Material0Pointer.Untyped,
                value.IncomingMaterial0,
                value.Material0,
                graph),
            MaterialLink(
                value.Material1Pointer.Untyped,
                value.IncomingMaterial1,
                value.Material1,
                graph));
    private static NestedXAssetBuildLink? MaterialLink(
        XPointerReference pointer,
        MaterialAsset? incoming,
        MaterialAsset? canonical,
        DetachedAssetSemanticGraphClone graph)
    {
        string? name = incoming?.Info.Name ?? canonical?.Info.Name;
        if (pointer.Type == PointerType.Null || name is null)
            return null;
        return new NestedXAssetBuildLink(
            new SymbolicXAssetReference(XAssetType.Material, name),
            SourceForm(pointer.Type),
            incoming is null
                ? null
                : MaterialAuthoredSnapshot.FromLoaded(
                    incoming,
                    graph.XModels.Materials).Data,
            pointer.Type == PointerType.Offset ? pointer.Raw : null);
    }
    private static NestedXAssetBuildLink? XModelLink(
        XPointerReference pointer,
        XModelAsset? incoming,
        XModelAsset? canonical,
        DetachedAssetSemanticGraphClone graph)
    {
        string? name = incoming?.Name ?? canonical?.Name;
        if (pointer.Type == PointerType.Null || name is null)
            return null;
        return new NestedXAssetBuildLink(
            new SymbolicXAssetReference(XAssetType.XModel, name),
            SourceForm(pointer.Type),
            incoming is null
                ? null
                : XModelAuthoredSnapshot.FromLoaded(incoming, graph.XModels).Data,
            pointer.Type == PointerType.Offset ? pointer.Raw : null);
    }
    private static NestedXAssetPointerSourceForm SourceForm(PointerType type) =>
        type switch
        {
            PointerType.Inline => NestedXAssetPointerSourceForm.Inline,
            PointerType.Insert => NestedXAssetPointerSourceForm.Insert,
            PointerType.Offset => NestedXAssetPointerSourceForm.PackedAlias,
            _ => throw new InvalidDataException(
                $"Unsupported nested Fx pointer source form {type}.")
        };
    private static FxExtendedBuildData? Extended(FxElemExtendedDef? value) => value is null ? null : value.Kind switch { FxElemExtendedDefKind.Trail when value.TrailDef is { } trail => new(FxExtendedBuildKind.Trail, new FxTrailBuildData(trail.ScrollTimeMsec, trail.RepeatDist, trail.InvSplitDist, trail.InvSplitArcDist, trail.InvSplitTime, trail.Verts.Select(v => new FxTrailVertexBuildData(v.Pos0, v.Pos1, v.Normal0, v.Normal1, v.TexCoord)).ToArray(), trail.Inds)), FxElemExtendedDefKind.SparkFountain when value.SparkFountainDef is { } spark => new(FxExtendedBuildKind.SparkFountain, sparkFountain: new FxSparkFountainBuildData(spark.Gravity, spark.BounceFrac, spark.BounceRand, spark.SparkSpacing, spark.SparkLength, spark.SparkCount, spark.LoopTime, spark.VelMin, spark.VelMax, spark.VelConeFrac, spark.RestSpeed, spark.BoostTime, spark.BoostFactor)), FxElemExtendedDefKind.DefaultBytePayload => new(FxExtendedBuildKind.DefaultBytePayload, defaultBytePayload: value.DefaultBytePayload ?? 0), _ => throw new InvalidDataException("Fx extended union has no detached authoring arm.") };
    private static FxVelocitySampleBuildData Velocity(FxElemVelStateSample value) => new(Frame(value.Local), Frame(value.World)); private static FxVelocityInFrameBuildData Frame(FxElemVelStateInFrame value) => new(Vector(value.Velocity.Base), Vector(value.Velocity.Amplitude), Vector(value.TotalDelta.Base), Vector(value.TotalDelta.Amplitude)); private static FxVisualStateSampleBuildData VisualSample(FxElemVisStateSample value) => new(State(value.Base), State(value.Amplitude)); private static FxVisualStateBuildData State(FxElemVisualState value) => new(new FxColorBuildData(value.Color.R, value.Color.G, value.Color.B, value.Color.A), value.RotationDelta, value.RotationTotal, value.Size0, value.Size1, value.Scale); private static FxFloatRangeBuildData Range(FxFloatRange value) => new(value.Base, value.Amplitude); private static FxVec3BuildData Vector(Vec3 value) => new(value.X, value.Y, value.Z); private static FxBoundsBuildData Bounds(Bounds value) => new(Vector(value.MidPoint), Vector(value.HalfSize)); private static SymbolicXAssetReference? Link(XAssetType type, string? value) => value is null ? null : new(type, value); private static SymbolicXAssetReference? External(XAssetType type, string? value) => value is null ? null : new(type, value.StartsWith(",", StringComparison.Ordinal) ? value : $",{value}");
}
public sealed class FxDraft
{
    private FxEffectDefBuildData _data; internal FxDraft(FxEffectDefBuildData value) => _data = value.Copy(); public FxEffectDefBuildData Data => _data.Copy(); public void ReplaceElements(IEnumerable<FxElementBuildData> values) { ArgumentNullException.ThrowIfNull(values); _data = new FxEffectDefBuildData(_data.Name, _data.Flags, _data.TotalSize, _data.MsecLoopingLife, _data.ElemDefCountLooping, _data.ElemDefCountOneShot, _data.ElemDefCountEmission, values); } public void SetFlags(int value) => _data = new FxEffectDefBuildData(_data.Name, value, _data.TotalSize, _data.MsecLoopingLife, _data.ElemDefCountLooping, _data.ElemDefCountOneShot, _data.ElemDefCountEmission, _data.Elements); internal FxDraft Clone() => new(_data);
}
public sealed class FxAuthoringAdapter : AssetAuthoringAdapter<FxAuthoredSnapshot, FxDraft, FxEffectDefBuildData>
{
    private static readonly FxEffectDefBodyEmitter Validator = new(); public override XAssetType AssetType => XAssetType.Fx; public override FxAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => FxAuthoredSnapshot.Import(source); public override FxDraft CreateDraft(FxAuthoredSnapshot snapshot) => new(snapshot.Data); public override FxDraft CloneDraft(FxDraft draft) => draft.Clone(); public override IReadOnlyList<AssetValidationIssue> ValidateDraft(FxDraft draft) => Validator.Validate(draft.Data).Select(value => new AssetValidationIssue(value.Path, value.Message, AssetValidationSeverity.Error)).ToArray(); public override bool SemanticallyEquals(FxDraft left, FxDraft right) => JsonSerializer.Serialize(left.Data) == JsonSerializer.Serialize(right.Data); public override FxEffectDefBuildData ExportBuildData(FxDraft draft) { FxEffectDefBuildData data = draft.Data; if (Validator.Validate(data).Count != 0) throw new InvalidOperationException("Fx draft has validation errors and cannot produce build data."); return data; }
}

public sealed class ImpactFxAuthoredSnapshot : ITargetZoneDetachedSemanticSnapshot
{
    internal ImpactFxAuthoredSnapshot(FxImpactTableBuildData data) => Data = data.Copy(); internal FxImpactTableBuildData Data { get; } public XAssetType AssetType => XAssetType.ImpactFx; internal static ImpactFxAuthoredSnapshot Import(TargetZoneRowSource source) => source.AuthoredDefinition?.SemanticSnapshot is ImpactFxAuthoredSnapshot value ? value : throw new InvalidDataException("ImpactFx editing requires a capture-time detached semantic snapshot because fixed matrix links can be aliases."); internal static ImpactFxAuthoredSnapshot FromLoaded(FxImpactTableAsset value) => new(FxImpactTableBuildData.FromLoaded(value));
}
public sealed class FxImpactTableBuildData : IFxImpactTableBuildData
{
    private readonly FxImpactEntryBuildData[] _entries;
    internal FxImpactTableBuildData(
        string? name,
        IEnumerable<FxImpactEntryBuildData> entries)
    {
        Name = name;
        _entries = entries.Select(Copy).ToArray();
    }
    public XAssetType AssetType => XAssetType.ImpactFx;
    public string? Name { get; }
    public IReadOnlyList<FxImpactEntryBuildData> Entries =>
        Array.AsReadOnly(_entries.Select(Copy).ToArray());
    internal FxImpactTableBuildData Copy() => new(Name, _entries);
    internal static FxImpactTableBuildData FromLoaded(
        FxImpactTableAsset value) =>
        new(
            value.Name,
            value.Entries.Select(entry => new FxImpactEntryBuildData(
                entry.SurfaceEffects.Select(effect => Link(effect?.Name)).ToArray(),
                entry.FleshEffects.Select(effect => Link(effect?.Name)).ToArray(),
                entry.SurfaceEffectPointers.Zip(
                    entry.SurfaceEffects,
                    static (pointer, effect) =>
                        ImportedLink(pointer.Untyped, effect)).ToArray(),
                entry.FleshEffectPointers.Zip(
                    entry.FleshEffects,
                    static (pointer, effect) =>
                        ImportedLink(pointer.Untyped, effect)).ToArray())));
    private static FxImpactEntryBuildData Copy(
        FxImpactEntryBuildData value) =>
        new(
            value.SurfaceEffects,
            value.FleshEffects,
            value.SurfaceEffectLinks,
            value.FleshEffectLinks);
    private static NestedXAssetBuildLink? ImportedLink(
        XPointerReference pointer,
        FxEffectDefAsset? effect)
    {
        if (pointer.Type != PointerType.Offset || effect?.Name is not { } name)
            return null;
        return new NestedXAssetBuildLink(
            new SymbolicXAssetReference(XAssetType.Fx, name),
            NestedXAssetPointerSourceForm.PackedAlias,
            ImportedPackedRaw: pointer.Raw);
    }
    private static SymbolicXAssetReference? Link(string? value) =>
        value is null
            ? null
            : new(
                XAssetType.Fx,
                value.StartsWith(",", StringComparison.Ordinal)
                    ? value
                    : $",{value}");
}
public sealed class ImpactFxDraft { private FxImpactTableBuildData _data; internal ImpactFxDraft(FxImpactTableBuildData value) => _data = value.Copy(); public FxImpactTableBuildData Data => _data.Copy(); public void ReplaceEntries(IEnumerable<FxImpactEntryBuildData> values) { ArgumentNullException.ThrowIfNull(values); _data = new FxImpactTableBuildData(_data.Name, values); } internal ImpactFxDraft Clone() => new(_data); }
public sealed class ImpactFxAuthoringAdapter : AssetAuthoringAdapter<ImpactFxAuthoredSnapshot, ImpactFxDraft, FxImpactTableBuildData>
{
    private static readonly FxImpactTableBodyEmitter Validator = new(); public override XAssetType AssetType => XAssetType.ImpactFx; public override ImpactFxAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => ImpactFxAuthoredSnapshot.Import(source); public override ImpactFxDraft CreateDraft(ImpactFxAuthoredSnapshot snapshot) => new(snapshot.Data); public override ImpactFxDraft CloneDraft(ImpactFxDraft draft) => draft.Clone(); public override IReadOnlyList<AssetValidationIssue> ValidateDraft(ImpactFxDraft draft) => Validator.Validate(draft.Data).Select(value => new AssetValidationIssue(value.Path, value.Message, AssetValidationSeverity.Error)).ToArray(); public override bool SemanticallyEquals(ImpactFxDraft left, ImpactFxDraft right) => JsonSerializer.Serialize(left.Data) == JsonSerializer.Serialize(right.Data); public override FxImpactTableBuildData ExportBuildData(ImpactFxDraft draft) { FxImpactTableBuildData data = draft.Data; if (Validator.Validate(data).Count != 0) throw new InvalidOperationException("ImpactFx draft has validation errors and cannot produce build data."); return data; }
}
