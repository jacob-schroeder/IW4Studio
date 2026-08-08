using IW4.Assets.Assets.Leaderboard;
using IW4.Assets.Assets;
using IW4.Assets.Assets.RawFile;
using IW4.Assets.Assets.Localize;
using IW4.Assets.Assets.StringTable;
using IW4.Assets.Assets.StructuredData;
using IW4.Assets.Assets.Physics;
using IW4.Assets.Assets.Sound;
using IW4.Assets.Assets.Tracer;
using IW4.Assets.Assets.LightDef;
using IW4.Assets.Assets.MapEnts;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Font;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XAnim;
using IW4.Assets.Assets.XModel;
using IW4.Assets.Assets.Fx;
using IW4.Assets.Assets.ImpactFx;
using IW4.Assets.Assets.ComWorld;
using IW4.Assets.Assets.GameMap;
using IW4.Assets.Assets.FxMap;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Vehicle;
using WeaponAsset = IW4.Assets.Assets.Weapon.WeaponAsset;
using IW4.Assets.Assets.Menu;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Assets;

namespace IW4.Studio.Documents;

internal static class TargetZoneDetachedSnapshotRequirements
{
    public static InvalidDataException Missing(XAssetType assetType) =>
        new(
            $"{assetType} editing requires its capture-time detached semantic snapshot; " +
            "serialized source fragments are not an authoring input.");
}

/// <summary>Detached PhysPreset state.  The name is immutable because it is
/// the serialized row identity; all supported scalar fields remain editable.</summary>
public sealed class PhysPresetAuthoredSnapshot : ITargetZoneDetachedSemanticSnapshot
{
    internal PhysPresetAuthoredSnapshot(
        string? name, int type, float mass, float bounce, float friction,
        float bulletForceScale, float explosiveForceScale, string? sndAliasPrefix,
        float piecesSpreadFraction, float piecesUpwardVelocity,
        byte tempDefaultToCylinder, byte perSurfaceSndAlias, ushort pad2A)
    {
        Name = name; Type = type; Mass = mass; Bounce = bounce; Friction = friction;
        BulletForceScale = bulletForceScale; ExplosiveForceScale = explosiveForceScale;
        SndAliasPrefix = sndAliasPrefix; PiecesSpreadFraction = piecesSpreadFraction;
        PiecesUpwardVelocity = piecesUpwardVelocity; TempDefaultToCylinder = tempDefaultToCylinder;
        PerSurfaceSndAlias = perSurfaceSndAlias; Pad2A = pad2A;
    }

    public XAssetType AssetType => XAssetType.PhysPreset;
    public string? Name { get; }
    public int Type { get; }
    public float Mass { get; }
    public float Bounce { get; }
    public float Friction { get; }
    public float BulletForceScale { get; }
    public float ExplosiveForceScale { get; }
    public string? SndAliasPrefix { get; }
    public float PiecesSpreadFraction { get; }
    public float PiecesUpwardVelocity { get; }
    public byte TempDefaultToCylinder { get; }
    public byte PerSurfaceSndAlias { get; }
    public ushort Pad2A { get; }

    internal static PhysPresetAuthoredSnapshot Import(TargetZoneRowSource source)
    {
        RequireDefinition(source, XAssetType.PhysPreset);
        if (source.AuthoredDefinition!.SemanticSnapshot is PhysPresetAuthoredSnapshot captured)
            return captured;

        throw TargetZoneDetachedSnapshotRequirements.Missing(XAssetType.PhysPreset);
    }

    internal static PhysPresetAuthoredSnapshot FromLoaded(PhysPresetAsset asset) => new(
        asset.Name, asset.Type, asset.Mass, asset.Bounce, asset.Friction,
        asset.BulletForceScale, asset.ExplosiveForceScale, asset.SndAliasPrefix,
        asset.PiecesSpreadFraction, asset.PiecesUpwardVelocity, asset.TempDefaultToCylinder,
        asset.PerSurfaceSndAlias, asset.Pad2A);

    private static void RequireDefinition(TargetZoneRowSource source, XAssetType type)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.SerializedType != type || source.State != TargetZoneRowSourceState.Definition || source.AuthoredDefinition is null)
            throw new InvalidDataException($"Only an authored {type} definition can open a detached draft.");
    }
}

public sealed class PhysPresetDraft
{
    internal PhysPresetDraft(PhysPresetAuthoredSnapshot source)
    {
        Name = source.Name; Type = source.Type; Mass = source.Mass; Bounce = source.Bounce;
        Friction = source.Friction; BulletForceScale = source.BulletForceScale;
        ExplosiveForceScale = source.ExplosiveForceScale; SndAliasPrefix = source.SndAliasPrefix;
        PiecesSpreadFraction = source.PiecesSpreadFraction; PiecesUpwardVelocity = source.PiecesUpwardVelocity;
        TempDefaultToCylinder = source.TempDefaultToCylinder; PerSurfaceSndAlias = source.PerSurfaceSndAlias;
        Pad2A = source.Pad2A;
    }
    public string? Name { get; }
    public int Type { get; private set; }
    public float Mass { get; private set; }
    public float Bounce { get; private set; }
    public float Friction { get; private set; }
    public float BulletForceScale { get; private set; }
    public float ExplosiveForceScale { get; private set; }
    public string? SndAliasPrefix { get; private set; }
    public float PiecesSpreadFraction { get; private set; }
    public float PiecesUpwardVelocity { get; private set; }
    public byte TempDefaultToCylinder { get; private set; }
    public byte PerSurfaceSndAlias { get; private set; }
    public ushort Pad2A { get; private set; }
    public void SetType(int value) => Type = value;
    public void SetMass(float value) => Mass = value;
    public void SetBounce(float value) => Bounce = value;
    public void SetFriction(float value) => Friction = value;
    public void SetBulletForceScale(float value) => BulletForceScale = value;
    public void SetExplosiveForceScale(float value) => ExplosiveForceScale = value;
    public void SetSndAliasPrefix(string? value) => SndAliasPrefix = value;
    public void SetPiecesSpreadFraction(float value) => PiecesSpreadFraction = value;
    public void SetPiecesUpwardVelocity(float value) => PiecesUpwardVelocity = value;
    public void SetTempDefaultToCylinder(byte value) => TempDefaultToCylinder = value;
    public void SetPerSurfaceSndAlias(byte value) => PerSurfaceSndAlias = value;
    public void SetPad2A(ushort value) => Pad2A = value;
    internal PhysPresetDraft Clone() => new(ToSnapshot());
    internal PhysPresetAuthoredSnapshot ToSnapshot() => new(Name, Type, Mass, Bounce, Friction,
        BulletForceScale, ExplosiveForceScale, SndAliasPrefix, PiecesSpreadFraction,
        PiecesUpwardVelocity, TempDefaultToCylinder, PerSurfaceSndAlias, Pad2A);
}

public sealed class PhysPresetBuildData : IPhysPresetBuildData
{
    internal PhysPresetBuildData(PhysPresetDraft draft)
    {
        PhysPresetAuthoredSnapshot data = draft.ToSnapshot();
        Name = data.Name; Type = data.Type; Mass = data.Mass; Bounce = data.Bounce; Friction = data.Friction;
        BulletForceScale = data.BulletForceScale; ExplosiveForceScale = data.ExplosiveForceScale;
        SndAliasPrefix = data.SndAliasPrefix; PiecesSpreadFraction = data.PiecesSpreadFraction;
        PiecesUpwardVelocity = data.PiecesUpwardVelocity; TempDefaultToCylinder = data.TempDefaultToCylinder;
        PerSurfaceSndAlias = data.PerSurfaceSndAlias; Pad2A = data.Pad2A;
    }
    public XAssetType AssetType => XAssetType.PhysPreset;
    public string? Name { get; } public int Type { get; } public float Mass { get; } public float Bounce { get; }
    public float Friction { get; } public float BulletForceScale { get; } public float ExplosiveForceScale { get; }
    public string? SndAliasPrefix { get; } public float PiecesSpreadFraction { get; } public float PiecesUpwardVelocity { get; }
    public byte TempDefaultToCylinder { get; } public byte PerSurfaceSndAlias { get; } public ushort Pad2A { get; }
    internal static PhysPresetBuildData FromLoaded(PhysPresetAsset asset) =>
        new(new PhysPresetDraft(PhysPresetAuthoredSnapshot.FromLoaded(asset)));
}

public sealed class PhysPresetAuthoringAdapter : AssetAuthoringAdapter<PhysPresetAuthoredSnapshot, PhysPresetDraft, PhysPresetBuildData>
{
    private static readonly PhysPresetBodyEmitter Validator = new();
    public override XAssetType AssetType => XAssetType.PhysPreset;
    public override PhysPresetAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => PhysPresetAuthoredSnapshot.Import(source);
    public override PhysPresetDraft CreateDraft(PhysPresetAuthoredSnapshot snapshot) => new(snapshot);
    public override PhysPresetDraft CloneDraft(PhysPresetDraft draft) => draft.Clone();
    public override IReadOnlyList<AssetValidationIssue> ValidateDraft(PhysPresetDraft draft) =>
        Validator.Validate(new PhysPresetBuildData(draft)).Select(ToIssue).ToArray();
    public override bool SemanticallyEquals(PhysPresetDraft left, PhysPresetDraft right) =>
        left.Name == right.Name && left.Type == right.Type && BitsEqual(left.Mass, right.Mass) &&
        BitsEqual(left.Bounce, right.Bounce) && BitsEqual(left.Friction, right.Friction) &&
        BitsEqual(left.BulletForceScale, right.BulletForceScale) && BitsEqual(left.ExplosiveForceScale, right.ExplosiveForceScale) &&
        left.SndAliasPrefix == right.SndAliasPrefix && BitsEqual(left.PiecesSpreadFraction, right.PiecesSpreadFraction) &&
        BitsEqual(left.PiecesUpwardVelocity, right.PiecesUpwardVelocity) && left.TempDefaultToCylinder == right.TempDefaultToCylinder &&
        left.PerSurfaceSndAlias == right.PerSurfaceSndAlias && left.Pad2A == right.Pad2A;
    public override PhysPresetBuildData ExportBuildData(PhysPresetDraft draft) => Export(draft, Validator, "PhysPreset");
    private static bool BitsEqual(float left, float right) => BitConverter.SingleToInt32Bits(left) == BitConverter.SingleToInt32Bits(right);
    private static AssetValidationIssue ToIssue(IW4.FastFiles.Emitters.Emission.EmissionError value) => new(value.Path, value.Message, AssetValidationSeverity.Error);
    private static PhysPresetBuildData Export(PhysPresetDraft draft, PhysPresetBodyEmitter validator, string type)
    {
        var data = new PhysPresetBuildData(draft);
        if (validator.Validate(data).Count != 0) throw new InvalidOperationException($"{type} draft has validation errors and cannot produce build data.");
        return data;
    }
}

public sealed class SndCurveAuthoredSnapshot : ITargetZoneDetachedSemanticSnapshot
{
    private readonly SndCurveKnotBuildData[] _knots;
    internal SndCurveAuthoredSnapshot(string? filename, ushort knotCount, ushort padding, IEnumerable<SndCurveKnotBuildData> knots)
    {
        Filename = filename; KnotCount = knotCount; Padding = padding; _knots = knots.ToArray();
    }
    public XAssetType AssetType => XAssetType.SndCurve;
    public string? Filename { get; }
    public ushort KnotCount { get; }
    public ushort Padding { get; }
    public IReadOnlyList<SndCurveKnotBuildData> Knots => Array.AsReadOnly(_knots);
    internal static SndCurveAuthoredSnapshot Import(TargetZoneRowSource source)
    {
        RequireDefinition(source, XAssetType.SndCurve);
        if (source.AuthoredDefinition!.SemanticSnapshot is SndCurveAuthoredSnapshot captured) return captured;
        throw TargetZoneDetachedSnapshotRequirements.Missing(XAssetType.SndCurve);
    }
    internal static SndCurveAuthoredSnapshot FromLoaded(SndCurve asset) => new(asset.Filename, asset.KnotCount, asset.Padding,
        asset.Knots.Select(knot => new SndCurveKnotBuildData(knot.X, knot.Y)));
    private static void RequireDefinition(TargetZoneRowSource source, XAssetType type)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.SerializedType != type || source.State != TargetZoneRowSourceState.Definition || source.AuthoredDefinition is null) throw new InvalidDataException($"Only an authored {type} definition can open a detached draft.");
    }
}

public sealed class SndCurveDraft
{
    private readonly SndCurveKnotBuildData[] _knots;
    internal SndCurveDraft(SndCurveAuthoredSnapshot source) { Filename = source.Filename; KnotCount = source.KnotCount; Padding = source.Padding; _knots = source.Knots.ToArray(); }
    public string? Filename { get; }
    public ushort KnotCount { get; private set; }
    public ushort Padding { get; private set; }
    public IReadOnlyList<SndCurveKnotBuildData> Knots => Array.AsReadOnly(_knots.ToArray());
    public void SetKnotCount(ushort value) => KnotCount = value;
    public void SetPadding(ushort value) => Padding = value;
    public void SetKnot(int index, SndCurveKnotBuildData value) { if ((uint)index >= _knots.Length) throw new ArgumentOutOfRangeException(nameof(index)); _knots[index] = value; }
    internal SndCurveDraft Clone() => new(new SndCurveAuthoredSnapshot(Filename, KnotCount, Padding, _knots));
}

public sealed class SndCurveBuildData : ISndCurveBuildData
{
    private readonly SndCurveKnotBuildData[] _knots;
    internal SndCurveBuildData(SndCurveDraft draft) { Filename = draft.Filename; KnotCount = draft.KnotCount; Padding = draft.Padding; _knots = draft.Knots.ToArray(); }
    internal static SndCurveBuildData FromLoaded(SndCurve asset) =>
        new(new SndCurveDraft(SndCurveAuthoredSnapshot.FromLoaded(asset)));
    public XAssetType AssetType => XAssetType.SndCurve; public string? Filename { get; } public ushort KnotCount { get; } public ushort Padding { get; }
    public IReadOnlyList<SndCurveKnotBuildData> Knots => Array.AsReadOnly(_knots);
}

public sealed class SndCurveAuthoringAdapter : AssetAuthoringAdapter<SndCurveAuthoredSnapshot, SndCurveDraft, SndCurveBuildData>
{
    private static readonly SndCurveBodyEmitter Validator = new();
    public override XAssetType AssetType => XAssetType.SndCurve;
    public override SndCurveAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => SndCurveAuthoredSnapshot.Import(source);
    public override SndCurveDraft CreateDraft(SndCurveAuthoredSnapshot snapshot) => new(snapshot);
    public override SndCurveDraft CloneDraft(SndCurveDraft draft) => draft.Clone();
    public override IReadOnlyList<AssetValidationIssue> ValidateDraft(SndCurveDraft draft) => Validator.Validate(new SndCurveBuildData(draft)).Select(value => new AssetValidationIssue(value.Path, value.Message, AssetValidationSeverity.Error)).ToArray();
    public override bool SemanticallyEquals(SndCurveDraft left, SndCurveDraft right) => left.Filename == right.Filename && left.KnotCount == right.KnotCount && left.Padding == right.Padding && left.Knots.Count == right.Knots.Count && left.Knots.Zip(right.Knots).All(pair => BitConverter.SingleToInt32Bits(pair.First.X) == BitConverter.SingleToInt32Bits(pair.Second.X) && BitConverter.SingleToInt32Bits(pair.First.Y) == BitConverter.SingleToInt32Bits(pair.Second.Y));
    public override SndCurveBuildData ExportBuildData(SndCurveDraft draft) { var data = new SndCurveBuildData(draft); if (Validator.Validate(data).Count != 0) throw new InvalidOperationException("SndCurve draft has validation errors and cannot produce build data."); return data; }
}

public sealed class LeaderboardColumnDraft : ILeaderboardColumnBuildData
{
    private readonly byte[] _pad0DTo0F;
    public LeaderboardColumnDraft(string? name, int id, int propertyId, byte hiddenRaw, ReadOnlySpan<byte> pad0DTo0F, string? statName, int type, int precision, int aggregation)
    {
        Name = name; Id = id; PropertyId = propertyId; HiddenRaw = hiddenRaw; StatName = statName;
        Type = type; Precision = precision; Aggregation = aggregation; _pad0DTo0F = pad0DTo0F.ToArray();
    }
    public string? Name { get; }
    public int Id { get; }
    public int PropertyId { get; }
    public byte HiddenRaw { get; }
    public string? StatName { get; }
    public int Type { get; }
    public int Precision { get; }
    public int Aggregation { get; }
    public byte[] GetPad0DTo0FCopy() => _pad0DTo0F.ToArray();
    internal LeaderboardColumnDraft CopyDetached() => new(Name, Id, PropertyId, HiddenRaw, _pad0DTo0F, StatName, Type, Precision, Aggregation);
}

public sealed class LeaderboardAuthoredSnapshot : ITargetZoneDetachedSemanticSnapshot
{
    private readonly LeaderboardColumnDraft[] _columns;
    internal LeaderboardAuthoredSnapshot(string? name, int id, int xpColumnId, int prestigeColumnId, IEnumerable<LeaderboardColumnDraft> columns) { Name = name; Id = id; XpColumnId = xpColumnId; PrestigeColumnId = prestigeColumnId; _columns = columns.Select(column => column.CopyDetached()).ToArray(); }
    public XAssetType AssetType => XAssetType.LeaderboardDef; public string? Name { get; } public int Id { get; } public int XpColumnId { get; } public int PrestigeColumnId { get; }
    public IReadOnlyList<LeaderboardColumnDraft> Columns => Array.AsReadOnly(_columns.Select(column => column.CopyDetached()).ToArray());
    internal static LeaderboardAuthoredSnapshot Import(TargetZoneRowSource source)
    {
        RequireDefinition(source, XAssetType.LeaderboardDef);
        if (source.AuthoredDefinition!.SemanticSnapshot is LeaderboardAuthoredSnapshot captured) return captured;
        throw TargetZoneDetachedSnapshotRequirements.Missing(XAssetType.LeaderboardDef);
    }
    internal static LeaderboardAuthoredSnapshot FromLoaded(LeaderboardDefAsset asset) => new(asset.Name, asset.Id, asset.XpColumnId, asset.PrestigeColumnId, asset.Columns.Select(column => new LeaderboardColumnDraft(column.Name, column.Id, column.PropertyId, column.HiddenRaw, column.Pad0DTo0F, column.StatName, (int)column.Type, column.Precision, (int)column.Aggregation)));
    private static void RequireDefinition(TargetZoneRowSource source, XAssetType type) { ArgumentNullException.ThrowIfNull(source); if (source.SerializedType != type || source.State != TargetZoneRowSourceState.Definition || source.AuthoredDefinition is null) throw new InvalidDataException($"Only an authored {type} definition can open a detached draft."); }
}

public sealed class LeaderboardDraft
{
    private readonly List<LeaderboardColumnDraft> _columns;
    internal LeaderboardDraft(LeaderboardAuthoredSnapshot source) { Name = source.Name; Id = source.Id; XpColumnId = source.XpColumnId; PrestigeColumnId = source.PrestigeColumnId; _columns = source.Columns.Select(column => column.CopyDetached()).ToList(); }
    public string? Name { get; } public int Id { get; private set; } public int XpColumnId { get; private set; } public int PrestigeColumnId { get; private set; }
    public IReadOnlyList<LeaderboardColumnDraft> Columns => _columns.Select(column => column.CopyDetached()).ToArray();
    public void SetIds(int id, int xpColumnId, int prestigeColumnId) { Id = id; XpColumnId = xpColumnId; PrestigeColumnId = prestigeColumnId; }
    public void SetColumn(int index, LeaderboardColumnDraft column) { ArgumentNullException.ThrowIfNull(column); if ((uint)index >= (uint)_columns.Count) throw new ArgumentOutOfRangeException(nameof(index)); _columns[index] = column.CopyDetached(); }
    internal LeaderboardDraft Clone() => new(new LeaderboardAuthoredSnapshot(Name, Id, XpColumnId, PrestigeColumnId, _columns));
}

public sealed class LeaderboardBuildData : ILeaderboardBuildData
{
    private readonly ILeaderboardColumnBuildData[] _columns;
    internal LeaderboardBuildData(LeaderboardDraft draft) { Name = draft.Name; Id = draft.Id; XpColumnId = draft.XpColumnId; PrestigeColumnId = draft.PrestigeColumnId; _columns = draft.Columns.Select(column => (ILeaderboardColumnBuildData)column.CopyDetached()).ToArray(); }
    public XAssetType AssetType => XAssetType.LeaderboardDef; public string? Name { get; } public int Id { get; } public int XpColumnId { get; } public int PrestigeColumnId { get; }
    public IReadOnlyList<ILeaderboardColumnBuildData> Columns => Array.AsReadOnly(_columns);
}

public sealed class LeaderboardAuthoringAdapter : AssetAuthoringAdapter<LeaderboardAuthoredSnapshot, LeaderboardDraft, LeaderboardBuildData>
{
    private static readonly LeaderboardBodyEmitter Validator = new();
    public override XAssetType AssetType => XAssetType.LeaderboardDef;
    public override LeaderboardAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => LeaderboardAuthoredSnapshot.Import(source);
    public override LeaderboardDraft CreateDraft(LeaderboardAuthoredSnapshot snapshot) => new(snapshot);
    public override LeaderboardDraft CloneDraft(LeaderboardDraft draft) => draft.Clone();
    public override IReadOnlyList<AssetValidationIssue> ValidateDraft(LeaderboardDraft draft) => Validator.Validate(new LeaderboardBuildData(draft)).Select(value => new AssetValidationIssue(value.Path, value.Message, AssetValidationSeverity.Error)).ToArray();
    public override bool SemanticallyEquals(LeaderboardDraft left, LeaderboardDraft right) => left.Name == right.Name && left.Id == right.Id && left.XpColumnId == right.XpColumnId && left.PrestigeColumnId == right.PrestigeColumnId && left.Columns.Count == right.Columns.Count && left.Columns.Zip(right.Columns).All(pair => pair.First.Name == pair.Second.Name && pair.First.Id == pair.Second.Id && pair.First.PropertyId == pair.Second.PropertyId && pair.First.HiddenRaw == pair.Second.HiddenRaw && pair.First.GetPad0DTo0FCopy().SequenceEqual(pair.Second.GetPad0DTo0FCopy()) && pair.First.StatName == pair.Second.StatName && pair.First.Type == pair.Second.Type && pair.First.Precision == pair.Second.Precision && pair.First.Aggregation == pair.Second.Aggregation);
    public override LeaderboardBuildData ExportBuildData(LeaderboardDraft draft) { var data = new LeaderboardBuildData(draft); if (Validator.Validate(data).Count != 0) throw new InvalidOperationException("LeaderboardDef draft has validation errors and cannot produce build data."); return data; }
}

/// <summary>Capture-time conversion is deliberately restricted to immutable,
/// detached values.  It never stores a runtime asset, block address, pool
/// provider, or loader context in the authoring baseline.</summary>
internal sealed class DetachedAssetSemanticGraphClone
{
    internal MenuGraphClone Menus { get; } = new();
    internal WeaponGraphClone Weapons { get; } = new();
    internal XModelGraphClone XModels { get; } = new();
    internal MaterialGraphClone Materials => XModels.Materials;

    internal IReadOnlyList<MaterialGraphDefinitionCapture>
        MaterialDefinitionCaptures =>
            Materials.DefinitionCaptures;

    internal void BeginTopLevelRow(int serializedIndex) =>
        Materials.BeginTopLevelRow(serializedIndex);
}

internal static class DetachedAssetSemanticSnapshotFactory
{
    public static ITargetZoneDetachedSemanticSnapshot? Capture(
        XAssetType type,
        BaseAsset? asset) =>
        Capture(type, asset, new DetachedAssetSemanticGraphClone());

    public static ITargetZoneDetachedSemanticSnapshot? Capture(
        XAssetType type,
        BaseAsset? asset,
        DetachedAssetSemanticGraphClone graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return (type, asset) switch
        {
        (XAssetType.RawFile, RawFileAsset rawFile) => RawFileAuthoredSnapshot.FromLoaded(rawFile),
        (XAssetType.Localize, LocalizeAsset localize) => LocalizeAuthoredSnapshot.FromLoaded(localize),
        (XAssetType.StringTable, StringTableAsset stringTable) => StringTableAuthoredSnapshot.FromLoaded(stringTable),
        (XAssetType.StructuredDataDef, StructuredDataDefSetAsset structuredData) => StructuredDataAuthoredSnapshot.FromLoaded(structuredData),
        (XAssetType.PhysPreset, PhysPresetAsset phys) => PhysPresetAuthoredSnapshot.FromLoaded(phys),
        (XAssetType.PhysCollmap, PhysCollmapAsset collmap) => PhysCollmapAuthoredSnapshot.FromLoaded(collmap),
        (XAssetType.XAnim, XAnimPartsAsset xanim) => XAnimAuthoredSnapshot.FromLoaded(xanim),
        (XAssetType.XModel, XModelAsset model) => XModelAuthoredSnapshot.FromLoaded(model, graph.XModels),
        (XAssetType.Sound, SoundAliasListAsset sound) => SoundAuthoredSnapshot.FromLoaded(sound),
        (XAssetType.Fx, FxEffectDefAsset fx) => FxAuthoredSnapshot.FromLoaded(fx, graph),
        (XAssetType.ImpactFx, FxImpactTableAsset impact) => ImpactFxAuthoredSnapshot.FromLoaded(impact),
        (XAssetType.SndCurve, SndCurve curve) => SndCurveAuthoredSnapshot.FromLoaded(curve),
        (XAssetType.LeaderboardDef, LeaderboardDefAsset leaderboard) => LeaderboardAuthoredSnapshot.FromLoaded(leaderboard),
        (XAssetType.Tracer, TracerDefAsset tracer) =>
            TracerAuthoredSnapshot.FromLoaded(tracer, graph.Materials),
        (XAssetType.LightDef, LightDefAsset light) => LightDefAuthoredSnapshot.FromLoaded(light),
        (XAssetType.ComMap, ComWorldAsset comMap) => ComWorldAuthoredSnapshot.FromLoaded(comMap),
        (XAssetType.GameMapSp, GameWorldSpAsset gameMapSp) => GameWorldSpAuthoredSnapshot.FromLoaded(gameMapSp),
        (XAssetType.GameMapMp, GameWorldMpAsset gameMapMp) => GameWorldMpAuthoredSnapshot.FromLoaded(gameMapMp),
        (XAssetType.FxMap, FxWorldAsset fxMap) =>
            FxWorldAuthoredSnapshot.FromLoaded(
                fxMap,
                graph.Materials),
        (XAssetType.ColMapSp, ClipMapAsset clipMap) => ClipMapAuthoredSnapshot.FromLoaded(clipMap, graph.XModels),
        (XAssetType.ColMapMp, ClipMapAsset clipMap) => ClipMapAuthoredSnapshot.FromLoaded(clipMap, graph.XModels),
        (XAssetType.GfxMap, GfxWorldAsset gfxMap) => GfxWorldAuthoredSnapshot.FromLoaded(gfxMap, graph),
        (XAssetType.Vehicle, VehicleDefAsset vehicle) => VehicleAuthoredSnapshot.FromLoaded(vehicle),
        (XAssetType.Weapon, WeaponAsset weapon) => WeaponAuthoredSnapshot.FromLoaded(weapon, graph.Weapons),
        (XAssetType.Menu, MenuDefAsset menu) => MenuAuthoredSnapshot.FromLoaded(menu, graph.Menus),
        (XAssetType.MenuFile, MenuFileAsset menuFile) => MenuFileAuthoredSnapshot.FromLoaded(menuFile, graph.Menus),
        (XAssetType.MapEnts, MapEntsAsset mapEnts) => MapEntsAuthoredSnapshot.FromLoaded(mapEnts),
        (XAssetType.AddonMapEnts, AddonMapEntsAsset addonMapEnts) => AddonMapEntsAuthoredSnapshot.FromLoaded(addonMapEnts),
        (XAssetType.PixelShader, MaterialShaderAsset shader) => MaterialShaderAuthoredSnapshot.FromLoaded(XAssetType.PixelShader, shader),
        (XAssetType.VertexShader, MaterialShaderAsset shader) => MaterialShaderAuthoredSnapshot.FromLoaded(XAssetType.VertexShader, shader),
        (XAssetType.LoadedSound, LoadedSound sound) => LoadedSoundAuthoredSnapshot.FromLoaded(sound),
        (XAssetType.Image, GfxImageAsset image) => GfxImageAuthoredSnapshot.FromLoaded(image),
        (XAssetType.Font, FontAsset font) =>
            FontAuthoredSnapshot.FromLoaded(font, graph.Materials),
        (XAssetType.Techset, MaterialTechniqueSetAsset techset) => TechniqueSetAuthoredSnapshot.FromLoaded(techset),
        (XAssetType.Material, MaterialAsset material) =>
            MaterialAuthoredSnapshot.FromLoaded(
                material,
                graph.Materials),
        _ => null
        };
    }
}

public sealed class TracerAuthoredSnapshot : ITargetZoneDetachedSemanticSnapshot
{
    private readonly TracerColorBuildData[] _colors;
    internal TracerAuthoredSnapshot(string? name, SymbolicXAssetReference? materialReference, uint drawInterval, float speed, float beamLength, float beamWidth, float screwRadius, float screwDistance, IEnumerable<TracerColorBuildData> colors, NestedXAssetBuildLink? materialLink = null)
    {
        Name = name; MaterialReference = materialReference; DrawInterval = drawInterval; Speed = speed; BeamLength = beamLength; BeamWidth = beamWidth; ScrewRadius = screwRadius; ScrewDistance = screwDistance; _colors = colors.ToArray(); MaterialLink = materialLink;
    }
    public XAssetType AssetType => XAssetType.Tracer; public string? Name { get; } public SymbolicXAssetReference? MaterialReference { get; } public NestedXAssetBuildLink? MaterialLink { get; }
    public uint DrawInterval { get; } public float Speed { get; } public float BeamLength { get; } public float BeamWidth { get; } public float ScrewRadius { get; } public float ScrewDistance { get; }
    public IReadOnlyList<TracerColorBuildData> Colors => Array.AsReadOnly(_colors);
    internal static TracerAuthoredSnapshot Import(TargetZoneRowSource source)
    {
        RequireDefinition(source, XAssetType.Tracer);
        if (source.AuthoredDefinition!.SemanticSnapshot is TracerAuthoredSnapshot captured) return captured;
        throw TargetZoneDetachedSnapshotRequirements.Missing(XAssetType.Tracer);
    }
    internal static TracerAuthoredSnapshot FromLoaded(TracerDefAsset asset)
        => FromLoaded(asset, new MaterialGraphClone());
    internal static TracerAuthoredSnapshot FromLoaded(
        TracerDefAsset asset,
        MaterialGraphClone graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        NestedXAssetBuildLink? materialLink = CaptureMaterialLink(asset, graph);
        return new(asset.Name, materialLink?.Reference ?? Reference(XAssetType.Material, asset.Material?.Info.Name), asset.DrawInterval, asset.Speed, asset.BeamLength, asset.BeamWidth, asset.ScrewRadius, asset.ScrewDistance, asset.Colors.Select(color => new TracerColorBuildData(color.Red, color.Green, color.Blue, color.Alpha)), materialLink);
    }
    private static NestedXAssetBuildLink? CaptureMaterialLink(
        TracerDefAsset asset,
        MaterialGraphClone graph)
    {
        string? name = asset.MaterialIncomingDefinition?.Info.Name ?? asset.Material?.Info.Name;
        if (asset.MaterialPointer.Type == PointerType.Null || name is null) return null;
        NestedXAssetPointerSourceForm sourceForm = asset.MaterialPointer.Type switch
        {
            PointerType.Inline => NestedXAssetPointerSourceForm.Inline,
            PointerType.Insert => NestedXAssetPointerSourceForm.Insert,
            PointerType.Offset => NestedXAssetPointerSourceForm.PackedAlias,
            _ => throw new InvalidDataException($"Unsupported nested Tracer Material source form {asset.MaterialPointer.Type}.")
        };
        return new NestedXAssetBuildLink(
            new SymbolicXAssetReference(XAssetType.Material, name),
            sourceForm,
            asset.MaterialIncomingDefinition is null ? null : MaterialAuthoredSnapshot.FromLoaded(asset.MaterialIncomingDefinition, graph).Data,
            sourceForm == NestedXAssetPointerSourceForm.PackedAlias ? asset.MaterialPointer.Raw : null,
            asset.MaterialPointer.CellAddress is { } ownerCell ? XPointerCodec.Encode(ownerCell) : null);
    }
    private static SymbolicXAssetReference? Reference(XAssetType type, string? name) => name is null ? null : new SymbolicXAssetReference(type, name);
    private static void RequireDefinition(TargetZoneRowSource source, XAssetType type) { ArgumentNullException.ThrowIfNull(source); if (source.SerializedType != type || source.State != TargetZoneRowSourceState.Definition || source.AuthoredDefinition is null) throw new InvalidDataException($"Only an authored {type} definition can open a detached draft."); }
}

public sealed class TracerDraft
{
    private readonly TracerColorBuildData[] _colors;
    private NestedXAssetBuildLink? _materialLink;
    internal TracerDraft(TracerAuthoredSnapshot source) { Name = source.Name; MaterialReference = source.MaterialReference; DrawInterval = source.DrawInterval; Speed = source.Speed; BeamLength = source.BeamLength; BeamWidth = source.BeamWidth; ScrewRadius = source.ScrewRadius; ScrewDistance = source.ScrewDistance; _colors = source.Colors.ToArray(); _materialLink = source.MaterialLink; }
    public string? Name { get; } public SymbolicXAssetReference? MaterialReference { get; private set; } public uint DrawInterval { get; private set; } public float Speed { get; private set; } public float BeamLength { get; private set; } public float BeamWidth { get; private set; } public float ScrewRadius { get; private set; } public float ScrewDistance { get; private set; }
    public IReadOnlyList<TracerColorBuildData> Colors => Array.AsReadOnly(_colors.ToArray());
    internal NestedXAssetBuildLink? MaterialLink => _materialLink;
    public void SetMaterialReference(SymbolicXAssetReference? value) { MaterialReference = value; _materialLink = null; }
    public void SetDrawInterval(uint value) => DrawInterval = value; public void SetSpeed(float value) => Speed = value; public void SetBeamLength(float value) => BeamLength = value; public void SetBeamWidth(float value) => BeamWidth = value; public void SetScrewRadius(float value) => ScrewRadius = value; public void SetScrewDistance(float value) => ScrewDistance = value;
    public void SetColor(int index, TracerColorBuildData value) { if ((uint)index >= _colors.Length) throw new ArgumentOutOfRangeException(nameof(index)); _colors[index] = value; }
    internal TracerDraft Clone() => new(new TracerAuthoredSnapshot(Name, MaterialReference, DrawInterval, Speed, BeamLength, BeamWidth, ScrewRadius, ScrewDistance, _colors, _materialLink));
}

public sealed class TracerBuildData : ITracerBuildData
{
    private readonly TracerColorBuildData[] _colors;
    internal TracerBuildData(TracerDraft draft) { Name = draft.Name; MaterialReference = draft.MaterialReference; MaterialLink = draft.MaterialLink; DrawInterval = draft.DrawInterval; Speed = draft.Speed; BeamLength = draft.BeamLength; BeamWidth = draft.BeamWidth; ScrewRadius = draft.ScrewRadius; ScrewDistance = draft.ScrewDistance; _colors = draft.Colors.ToArray(); }
    public XAssetType AssetType => XAssetType.Tracer; public string? Name { get; } public SymbolicXAssetReference? MaterialReference { get; } public NestedXAssetBuildLink? MaterialLink { get; } public uint DrawInterval { get; } public float Speed { get; } public float BeamLength { get; } public float BeamWidth { get; } public float ScrewRadius { get; } public float ScrewDistance { get; }
    public IReadOnlyList<TracerColorBuildData> Colors => Array.AsReadOnly(_colors);
}

public sealed class TracerAuthoringAdapter : AssetAuthoringAdapter<TracerAuthoredSnapshot, TracerDraft, TracerBuildData>
{
    private static readonly TracerBodyEmitter Validator = new();
    public override XAssetType AssetType => XAssetType.Tracer; public override TracerAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => TracerAuthoredSnapshot.Import(source); public override TracerDraft CreateDraft(TracerAuthoredSnapshot snapshot) => new(snapshot); public override TracerDraft CloneDraft(TracerDraft draft) => draft.Clone();
    public override IReadOnlyList<AssetValidationIssue> ValidateDraft(TracerDraft draft) => Validator.Validate(new TracerBuildData(draft)).Select(value => new AssetValidationIssue(value.Path, value.Message, AssetValidationSeverity.Error)).ToArray();
    public override bool SemanticallyEquals(TracerDraft left, TracerDraft right) => left.Name == right.Name && left.MaterialReference == right.MaterialReference && left.MaterialLink == right.MaterialLink && left.DrawInterval == right.DrawInterval && Bits(left.Speed, right.Speed) && Bits(left.BeamLength, right.BeamLength) && Bits(left.BeamWidth, right.BeamWidth) && Bits(left.ScrewRadius, right.ScrewRadius) && Bits(left.ScrewDistance, right.ScrewDistance) && left.Colors.Count == right.Colors.Count && left.Colors.Zip(right.Colors).All(pair => Bits(pair.First.Red, pair.Second.Red) && Bits(pair.First.Green, pair.Second.Green) && Bits(pair.First.Blue, pair.Second.Blue) && Bits(pair.First.Alpha, pair.Second.Alpha));
    public override TracerBuildData ExportBuildData(TracerDraft draft) { var data = new TracerBuildData(draft); if (Validator.Validate(data).Count != 0) throw new InvalidOperationException("Tracer draft has validation errors and cannot produce build data."); return data; }
    private static bool Bits(float left, float right) => BitConverter.SingleToInt32Bits(left) == BitConverter.SingleToInt32Bits(right);
}

public sealed class LightDefAuthoredSnapshot : ITargetZoneDetachedSemanticSnapshot
{
    private readonly byte[] _pad;
    internal LightDefAuthoredSnapshot(
        string? name,
        SymbolicXAssetReference? imageReference,
        byte samplerState,
        ReadOnlySpan<byte> pad,
        uint lmapLookupStart,
        NestedXAssetBuildLink? imageLink = null)
    {
        Name = name;
        ImageReference = imageReference;
        SamplerState = samplerState;
        _pad = pad.ToArray();
        LmapLookupStart = lmapLookupStart;
        ImageLink = imageLink;
    }
    public XAssetType AssetType => XAssetType.LightDef;
    public string? Name { get; }
    public SymbolicXAssetReference? ImageReference { get; }
    public NestedXAssetBuildLink? ImageLink { get; }
    public byte SamplerState { get; }
    public uint LmapLookupStart { get; }
    public byte[] GetPad09To0BCopy() => _pad.ToArray();
    internal static LightDefAuthoredSnapshot Import(TargetZoneRowSource source)
    {
        RequireDefinition(source, XAssetType.LightDef); if (source.AuthoredDefinition!.SemanticSnapshot is LightDefAuthoredSnapshot captured) return captured;
        throw TargetZoneDetachedSnapshotRequirements.Missing(XAssetType.LightDef);
    }
    internal static LightDefAuthoredSnapshot FromLoaded(LightDefAsset asset)
    {
        NestedXAssetBuildLink? imageLink = CaptureImageLink(asset);
        return new(
            asset.Name,
            imageLink?.Reference ?? Reference(asset.Image?.Name),
            asset.SamplerState,
            asset.Pad09To0B,
            asset.LmapLookupStart,
            imageLink);
    }
    private static NestedXAssetBuildLink? CaptureImageLink(
        LightDefAsset asset)
    {
        PointerType pointerType = asset.ImagePointer.Type;
        string? name = asset.IncomingImage?.Name ?? asset.Image?.Name;
        if (pointerType == PointerType.Null || name is null)
            return null;
        return new NestedXAssetBuildLink(
            new SymbolicXAssetReference(XAssetType.Image, name),
            pointerType switch
            {
                PointerType.Inline =>
                    NestedXAssetPointerSourceForm.Inline,
                PointerType.Insert =>
                    NestedXAssetPointerSourceForm.Insert,
                PointerType.Offset =>
                    NestedXAssetPointerSourceForm.PackedAlias,
                _ => throw new InvalidDataException(
                    $"Unsupported nested LightDef image source form {pointerType}.")
            },
            asset.IncomingImage is null
                ? null
                : new GfxImageBuildData(asset.IncomingImage),
            pointerType == PointerType.Offset
                ? asset.ImagePointer.Raw
                : null);
    }
    private static SymbolicXAssetReference? Reference(string? name) =>
        name is null
            ? null
            : new SymbolicXAssetReference(
                XAssetType.Image,
                name.StartsWith(",", StringComparison.Ordinal)
                    ? name
                    : $",{name}");
    private static void RequireDefinition(TargetZoneRowSource source, XAssetType type) { ArgumentNullException.ThrowIfNull(source); if (source.SerializedType != type || source.State != TargetZoneRowSourceState.Definition || source.AuthoredDefinition is null) throw new InvalidDataException($"Only an authored {type} definition can open a detached draft."); }
}

public sealed class LightDefDraft
{
    private byte[] _pad;
    private NestedXAssetBuildLink? _imageLink;
    internal LightDefDraft(LightDefAuthoredSnapshot source) { Name = source.Name; ImageReference = source.ImageReference; _imageLink = source.ImageLink; SamplerState = source.SamplerState; _pad = source.GetPad09To0BCopy(); LmapLookupStart = source.LmapLookupStart; }
    public string? Name { get; } public SymbolicXAssetReference? ImageReference { get; private set; } public byte SamplerState { get; private set; } public uint LmapLookupStart { get; private set; } public byte[] GetPad09To0BCopy() => _pad.ToArray();
    internal NestedXAssetBuildLink? ImageLink => _imageLink;
    public void SetImageReference(SymbolicXAssetReference? value) { ImageReference = value; _imageLink = null; } public void SetSamplerState(byte value) => SamplerState = value; public void SetLmapLookupStart(uint value) => LmapLookupStart = value; public void SetPad09To0B(ReadOnlySpan<byte> value) { if (value.Length != 3) throw new ArgumentException("LightDef padding is exactly three bytes.", nameof(value)); _pad = value.ToArray(); }
    internal LightDefDraft Clone() => new(new LightDefAuthoredSnapshot(Name, ImageReference, SamplerState, _pad, LmapLookupStart, _imageLink));
}

public sealed class LightDefBuildData : ILightDefBuildData
{
    private readonly byte[] _pad;
    internal LightDefBuildData(LightDefDraft draft) { Name = draft.Name; ImageReference = draft.ImageReference; ImageLink = draft.ImageLink; SamplerState = draft.SamplerState; _pad = draft.GetPad09To0BCopy(); LmapLookupStart = draft.LmapLookupStart; }
    public XAssetType AssetType => XAssetType.LightDef; public string? Name { get; } public SymbolicXAssetReference? ImageReference { get; } public NestedXAssetBuildLink? ImageLink { get; } public byte SamplerState { get; } public uint LmapLookupStart { get; } public byte[] GetPad09To0BCopy() => _pad.ToArray();
}

public sealed class LightDefAuthoringAdapter : AssetAuthoringAdapter<LightDefAuthoredSnapshot, LightDefDraft, LightDefBuildData>
{
    private static readonly LightDefBodyEmitter Validator = new();
    public override XAssetType AssetType => XAssetType.LightDef; public override LightDefAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => LightDefAuthoredSnapshot.Import(source); public override LightDefDraft CreateDraft(LightDefAuthoredSnapshot snapshot) => new(snapshot); public override LightDefDraft CloneDraft(LightDefDraft draft) => draft.Clone();
    public override IReadOnlyList<AssetValidationIssue> ValidateDraft(LightDefDraft draft) => Validator.Validate(new LightDefBuildData(draft)).Select(value => new AssetValidationIssue(value.Path, value.Message, AssetValidationSeverity.Error)).ToArray();
    public override bool SemanticallyEquals(LightDefDraft left, LightDefDraft right) => left.Name == right.Name && left.ImageReference == right.ImageReference && left.SamplerState == right.SamplerState && left.LmapLookupStart == right.LmapLookupStart && left.GetPad09To0BCopy().SequenceEqual(right.GetPad09To0BCopy());
    public override LightDefBuildData ExportBuildData(LightDefDraft draft) { var data = new LightDefBuildData(draft); if (Validator.Validate(data).Count != 0) throw new InvalidOperationException("LightDef draft has validation errors and cannot produce build data."); return data; }
}
