using System.Text.Json;
using IW4.Assets.Assets.GameMap;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Assets;

namespace IW4.Studio.Documents;

public sealed class GameWorldMpAuthoredSnapshot : ITargetZoneDetachedSemanticSnapshot
{
    internal GameWorldMpAuthoredSnapshot(GameWorldMpBuildData data) => Data = data.Copy();
    internal GameWorldMpBuildData Data { get; }
    public XAssetType AssetType => XAssetType.GameMapMp;
    internal static GameWorldMpAuthoredSnapshot Import(TargetZoneRowSource source) =>
        source.AuthoredDefinition?.SemanticSnapshot is GameWorldMpAuthoredSnapshot snapshot
            ? snapshot : throw new InvalidDataException("GameMapMp editing requires a capture-time detached semantic snapshot.");
    internal static GameWorldMpAuthoredSnapshot FromLoaded(GameWorldMpAsset asset) => new(GameWorldMpBuildData.FromLoaded(asset));
}

public sealed class GameWorldMpBuildData : IGameWorldMpBuildData
{
    public GameWorldMpBuildData(string? name, GGlassDataBuildData? glassData) { Name = name; GlassData = Copy(glassData); }
    public XAssetType AssetType => XAssetType.GameMapMp;
    public string? Name { get; }
    public GGlassDataBuildData? GlassData { get; }
    internal GameWorldMpBuildData Copy() => new(Name, GlassData);
    internal static GameWorldMpBuildData FromLoaded(GameWorldMpAsset asset) => new(asset.Name, FromLoaded(asset.GlassData));
    private static GGlassDataBuildData? FromLoaded(GGlassData? data) => data is null ? null : new(
        data.GlassPieces.Select(piece => new GGlassPieceBuildData(piece.DamageTaken, piece.CollapseTime, piece.LastStateChangeTime, piece.PackedImpactDir, piece.PackedImpactPos)),
        data.DamageToWeaken,
        data.DamageToDestroy,
        data.GlassNames.Select(name => new GGlassNameBuildData(name.NameStr, name.Name, name.PieceIndices)),
        data.Pad14To7F.ToArray(),
        data.GlassNamesPointer.Raw);
    private static GGlassDataBuildData? Copy(GGlassDataBuildData? data) => data is null ? null : new(data.Pieces, data.DamageToWeaken, data.DamageToDestroy, data.Names, data.GetPad14To7FCopy(), data.ImportedGlassNamesPointerRaw);
}

public sealed class GameWorldMpDraft
{
    private GGlassDataBuildData? _glassData;
    internal GameWorldMpDraft(GameWorldMpBuildData data) { Name = data.Name; _glassData = data.GlassData is null ? null : new GGlassDataBuildData(data.GlassData.Pieces, data.GlassData.DamageToWeaken, data.GlassData.DamageToDestroy, data.GlassData.Names, data.GlassData.GetPad14To7FCopy(), data.GlassData.ImportedGlassNamesPointerRaw); }
    public string? Name { get; }
    public GGlassDataBuildData? GlassData => _glassData is null ? null : new GGlassDataBuildData(_glassData.Pieces, _glassData.DamageToWeaken, _glassData.DamageToDestroy, _glassData.Names, _glassData.GetPad14To7FCopy(), _glassData.ImportedGlassNamesPointerRaw);
    public void SetGlassData(GGlassDataBuildData? value) => _glassData = value is null ? null : new GGlassDataBuildData(value.Pieces, value.DamageToWeaken, value.DamageToDestroy, value.Names, value.GetPad14To7FCopy(), value.ImportedGlassNamesPointerRaw);
    internal GameWorldMpDraft Clone() => new(ToBuildData());
    internal GameWorldMpBuildData ToBuildData() => new(Name, _glassData);
}

public sealed class GameWorldMpAuthoringAdapter : AssetAuthoringAdapter<GameWorldMpAuthoredSnapshot, GameWorldMpDraft, GameWorldMpBuildData>
{
    private static readonly GameWorldMpBodyEmitter Validator = new();
    public override XAssetType AssetType => XAssetType.GameMapMp;
    public override GameWorldMpAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => GameWorldMpAuthoredSnapshot.Import(source);
    public override GameWorldMpDraft CreateDraft(GameWorldMpAuthoredSnapshot snapshot) => new(snapshot.Data);
    public override GameWorldMpDraft CloneDraft(GameWorldMpDraft draft) => draft.Clone();
    public override IReadOnlyList<AssetValidationIssue> ValidateDraft(GameWorldMpDraft draft) => Validator.Validate(draft.ToBuildData()).Select(value => new AssetValidationIssue(value.Path, value.Message, AssetValidationSeverity.Error)).ToArray();
    public override bool SemanticallyEquals(GameWorldMpDraft left, GameWorldMpDraft right) => JsonSerializer.Serialize(left.ToBuildData()) == JsonSerializer.Serialize(right.ToBuildData());
    public override GameWorldMpBuildData ExportBuildData(GameWorldMpDraft draft) { GameWorldMpBuildData data = draft.ToBuildData(); if (Validator.Validate(data).Count != 0) throw new InvalidOperationException("GameMapMp draft has validation errors and cannot produce build data."); return data; }
}
