using System.Text.Json;
using IW4.Assets.Assets.FxMap;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Physics;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Assets;

namespace IW4.Studio.Documents;

/// <summary>Detached FxMap state.  All collections are copied at capture
/// time; nested material/physics identities are symbolic external references
/// and no source block address participates in the saved graph.</summary>
public sealed class FxWorldAuthoredSnapshot : ITargetZoneDetachedSemanticSnapshot
{
    internal FxWorldAuthoredSnapshot(FxWorldBuildData data) => Data = data.Copy();
    internal FxWorldBuildData Data { get; }
    public XAssetType AssetType => XAssetType.FxMap;
    internal static FxWorldAuthoredSnapshot Import(TargetZoneRowSource source) =>
        source.AuthoredDefinition?.SemanticSnapshot is FxWorldAuthoredSnapshot snapshot
            ? snapshot
            : throw new InvalidDataException("FxMap editing requires a capture-time detached semantic snapshot.");
    internal static FxWorldAuthoredSnapshot FromLoaded(FxWorldAsset asset) =>
        FromLoaded(asset, new MaterialGraphClone());
    internal static FxWorldAuthoredSnapshot FromLoaded(
        FxWorldAsset asset,
        MaterialGraphClone graph) =>
        new(FxWorldBuildData.FromLoaded(asset, graph));
}

public sealed class FxWorldBuildData : IFxWorldBuildData
{
    private readonly FxGlassDefReferenceBuildData[] _definitionReferences;
    public FxWorldBuildData(string? name, FxGlassSystem glassSystem, IEnumerable<FxGlassDefReferenceBuildData> definitionReferences)
    {
        ArgumentNullException.ThrowIfNull(glassSystem); ArgumentNullException.ThrowIfNull(definitionReferences);
        Name = name; GlassSystem = Clone(glassSystem); _definitionReferences = definitionReferences.ToArray();
    }
    public XAssetType AssetType => XAssetType.FxMap;
    public string? Name { get; }
    public FxGlassSystem GlassSystem { get; }
    public IReadOnlyList<FxGlassDefReferenceBuildData> DefinitionReferences => Array.AsReadOnly(_definitionReferences);
    internal FxWorldBuildData Copy() => new(Name, GlassSystem, _definitionReferences);

    /// <summary>
    /// Returns a detached copy with only existing serialized glass-definition
    /// half-thickness scalars replaced. Definition cardinality, initial
    /// pieces, the RUNTIME half-thickness cache, and nested asset references
    /// remain unchanged.
    /// </summary>
    public FxWorldBuildData WithGlassDefinitionHalfThickness(
        int definitionIndex,
        float halfThickness) =>
        WithGlassDefinitionHalfThicknesses(
        [
            new KeyValuePair<int, float>(
                definitionIndex,
                halfThickness)
        ]);

    /// <summary>
    /// Batch form of the fixed-cardinality definition edit. The detached
    /// glass graph is copied exactly once for any number of distinct rows.
    /// </summary>
    public FxWorldBuildData WithGlassDefinitionHalfThicknesses(
        IEnumerable<KeyValuePair<int, float>> replacements) =>
        WithGlassDefinitionProperties(
            replacements,
            colorReplacements: []);

    /// <summary>
    /// Returns a detached copy with only one existing serialized
    /// glass-definition packed color replaced. Definition cardinality,
    /// initial pieces, runtime caches, and nested asset references remain
    /// unchanged.
    /// </summary>
    public FxWorldBuildData WithGlassDefinitionColor(
        int definitionIndex,
        uint color) =>
        WithGlassDefinitionColors(
        [
            new KeyValuePair<int, uint>(
                definitionIndex,
                color)
        ]);

    /// <summary>
    /// Batch form of the fixed-cardinality definition-color edit. The
    /// detached glass graph is copied exactly once for any number of distinct
    /// rows.
    /// </summary>
    public FxWorldBuildData WithGlassDefinitionColors(
        IEnumerable<KeyValuePair<int, uint>> replacements) =>
        WithGlassDefinitionProperties(
            halfThicknessReplacements: [],
            replacements);

    /// <summary>
    /// Rebuilds both supported fixed-cardinality definition properties in one
    /// detached graph copy. Each replacement collection must contain distinct
    /// in-range definition indices.
    /// </summary>
    public FxWorldBuildData WithGlassDefinitionProperties(
        IEnumerable<KeyValuePair<int, float>> halfThicknessReplacements,
        IEnumerable<KeyValuePair<int, uint>> colorReplacements)
    {
        ArgumentNullException.ThrowIfNull(halfThicknessReplacements);
        ArgumentNullException.ThrowIfNull(colorReplacements);

        var halfThicknessByDefinition = new Dictionary<int, float>();
        foreach ((int definitionIndex, float halfThickness) in
                 halfThicknessReplacements)
        {
            if (definitionIndex < 0 ||
                definitionIndex >= GlassSystem.Defs.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(halfThicknessReplacements),
                    $"Glass-definition index {definitionIndex} is outside " +
                    $"the {GlassSystem.Defs.Count}-row definition table.");
            }
            if (!IsValidHalfThickness(halfThickness))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(halfThicknessReplacements),
                    $"Glass-definition index {definitionIndex} requires a " +
                    "finite, strictly positive half thickness.");
            }
            if (!halfThicknessByDefinition.TryAdd(
                    definitionIndex,
                    halfThickness))
            {
                throw new ArgumentException(
                    $"Glass-definition index {definitionIndex} appears more " +
                    "than once in the half-thickness replacement set.",
                    nameof(halfThicknessReplacements));
            }
        }

        var colorByDefinition = new Dictionary<int, uint>();
        foreach ((int definitionIndex, uint color) in colorReplacements)
        {
            if (definitionIndex < 0 ||
                definitionIndex >= GlassSystem.Defs.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(colorReplacements),
                    $"Glass-definition index {definitionIndex} is outside " +
                    $"the {GlassSystem.Defs.Count}-row definition table.");
            }
            if (!colorByDefinition.TryAdd(definitionIndex, color))
            {
                throw new ArgumentException(
                    $"Glass-definition index {definitionIndex} appears more " +
                    "than once in the color replacement set.",
                    nameof(colorReplacements));
            }
        }

        return new FxWorldBuildData(
            Name,
            Clone(
                GlassSystem,
                halfThicknessByDefinition,
                colorByDefinition),
            _definitionReferences);
    }

    internal static FxWorldBuildData FromLoaded(FxWorldAsset asset) =>
        FromLoaded(asset, new MaterialGraphClone());
    internal static FxWorldBuildData FromLoaded(
        FxWorldAsset asset,
        MaterialGraphClone graph) => new(
        asset.Name,
        asset.GlassSystem,
        asset.GlassSystem.Defs.Select(value => new FxGlassDefReferenceBuildData(
            Reference(
                XAssetType.Material,
                value.IncomingMaterial?.Info.Name ?? value.Material?.Info.Name),
            Reference(
                XAssetType.Material,
                value.IncomingMaterialShattered?.Info.Name ??
                value.MaterialShattered?.Info.Name),
            Reference(
                XAssetType.PhysPreset,
                value.IncomingPhysPreset?.Name ?? value.PhysPreset?.Name),
            MaterialLink(
                value.MaterialPointer.Untyped,
                value.IncomingMaterial,
                value.Material,
                graph),
            MaterialLink(
                value.MaterialShatteredPointer.Untyped,
                value.IncomingMaterialShattered,
                value.MaterialShattered,
                graph),
            PhysPresetLink(
                value.PhysPresetPointer.Untyped,
                value.IncomingPhysPreset,
                value.PhysPreset))));

    private static SymbolicXAssetReference? Reference(XAssetType type, string? name) => name is null ? null : new(type, name.StartsWith(",", StringComparison.Ordinal) ? name : $",{name}");
    private static NestedXAssetBuildLink? MaterialLink(
        XPointerReference pointer,
        MaterialAsset? incoming,
        MaterialAsset? canonical,
        MaterialGraphClone graph)
    {
        string? name = incoming?.Info.Name ?? canonical?.Info.Name;
        if (pointer.Type == PointerType.Null || name is null)
            return null;
        return new NestedXAssetBuildLink(
            new SymbolicXAssetReference(XAssetType.Material, name),
            SourceForm(pointer.Type),
            incoming is null
                ? null
                : MaterialAuthoredSnapshot.FromLoaded(incoming, graph).Data,
            pointer.Type == PointerType.Offset ? pointer.Raw : null);
    }
    private static NestedXAssetBuildLink? PhysPresetLink(
        XPointerReference pointer,
        PhysPresetAsset? incoming,
        PhysPresetAsset? canonical)
    {
        string? name = incoming?.Name ?? canonical?.Name;
        if (pointer.Type == PointerType.Null || name is null)
            return null;
        return new NestedXAssetBuildLink(
            new SymbolicXAssetReference(XAssetType.PhysPreset, name),
            SourceForm(pointer.Type),
            incoming is null ? null : PhysPresetBuildData.FromLoaded(incoming),
            pointer.Type == PointerType.Offset ? pointer.Raw : null);
    }
    private static NestedXAssetPointerSourceForm SourceForm(PointerType type) =>
        type switch
        {
            PointerType.Inline => NestedXAssetPointerSourceForm.Inline,
            PointerType.Insert => NestedXAssetPointerSourceForm.Insert,
            PointerType.Offset => NestedXAssetPointerSourceForm.PackedAlias,
            _ => throw new InvalidDataException(
                $"Unsupported nested FxMap pointer source form {type}.")
        };

    internal static FxGlassSystem Clone(FxGlassSystem value) =>
        Clone(
            value,
            halfThicknessReplacements: null,
            colorReplacements: null);

    private static FxGlassSystem Clone(
        FxGlassSystem value,
        IReadOnlyDictionary<int, float>? halfThicknessReplacements,
        IReadOnlyDictionary<int, uint>? colorReplacements) => new()
    {
        Time = value.Time, PrevTime = value.PrevTime, DefCount = value.DefCount, PieceLimit = value.PieceLimit,
        PieceWordCount = value.PieceWordCount, InitPieceCount = value.InitPieceCount, CellCount = value.CellCount,
        ActivePieceCount = value.ActivePieceCount, FirstFreePiece = value.FirstFreePiece, GeoDataLimit = value.GeoDataLimit,
        GeoDataCount = value.GeoDataCount, InitGeoDataCount = value.InitGeoDataCount,
        Defs = value.Defs.Select((definition, index) =>
        {
            float halfThickness =
                halfThicknessReplacements is not null &&
                halfThicknessReplacements.TryGetValue(
                    index,
                    out float replacementHalfThickness)
                    ? replacementHalfThickness
                    : definition.HalfThickness;
            uint color =
                colorReplacements is not null &&
                colorReplacements.TryGetValue(
                    index,
                    out uint replacementColor)
                    ? replacementColor
                    : definition.Color;
            return Clone(definition, halfThickness, color);
        }).ToArray(),
        PiecePlaces = value.PiecePlaces.Select(piece => new FxGlassPiecePlace(Clone(piece.Frame), piece.Radius, piece.NextFree)).ToArray(),
        PieceStates = value.PieceStates.Select(state => new FxGlassPieceState { TexCoordOrigin = state.TexCoordOrigin, SupportMask = state.SupportMask, InitIndex = state.InitIndex, GeoDataStart = state.GeoDataStart, DefIndex = state.DefIndex, Pad11 = state.Pad11.ToArray(), VertCount = state.VertCount, HoleDataCount = state.HoleDataCount, CrackDataCount = state.CrackDataCount, FanDataCount = state.FanDataCount, Flags = state.Flags, AreaX2 = state.AreaX2 }).ToArray(),
        PieceDynamics = value.PieceDynamics.Select(dynamics => new FxGlassPieceDynamics(dynamics.FallTime, dynamics.PhysObjId, dynamics.PhysJointId, dynamics.Vel, dynamics.AVel)).ToArray(),
        GeoData = value.GeoData.ToArray(), IsInUse = value.IsInUse.ToArray(), CellBits = value.CellBits.ToArray(), VisData = value.VisData.ToArray(), LinkOrg = value.LinkOrg.ToArray(), HalfThickness = value.HalfThickness.ToArray(), LightingHandles = value.LightingHandles.ToArray(),
        InitPieceStates = value.InitPieceStates.Select(state => new FxGlassInitPieceState { Frame = Clone(state.Frame), Radius = state.Radius, TexCoordOrigin = state.TexCoordOrigin, SupportMask = state.SupportMask, AreaX2 = state.AreaX2, DefIndex = state.DefIndex, VertCount = state.VertCount, FanDataCount = state.FanDataCount, Pad33 = state.Pad33 }).ToArray(),
        InitGeoData = value.InitGeoData.ToArray(), NeedToCompactData = value.NeedToCompactData, InitCount = value.InitCount,
        Pad66 = value.Pad66, EffectChanceAccum = value.EffectChanceAccum, LastPieceDeletionTime = value.LastPieceDeletionTime
    };
    private static FxGlassDef Clone(
        FxGlassDef value,
        float halfThickness,
        uint color) => new()
    {
        HalfThickness = halfThickness,
        TexVecs = value.TexVecs.ToArray(),
        Color = color,
        InvHighMipRadius = value.InvHighMipRadius,
        ShatteredInvHighMipRadius = value.ShatteredInvHighMipRadius
    };
    private static bool IsValidHalfThickness(float value) =>
        float.IsFinite(value) &&
        value > 0f &&
        BitConverter.SingleToInt32Bits(value) !=
            BitConverter.SingleToInt32Bits(-0f);
    private static FxSpatialFrame Clone(FxSpatialFrame value) => new(value.Quat, value.Origin);
}

public sealed class FxWorldDraft
{
    private FxWorldBuildData _data;
    internal FxWorldDraft(FxWorldBuildData data) => _data = data.Copy();
    public FxWorldBuildData Data => _data.Copy();
    public void Replace(FxWorldBuildData value) { ArgumentNullException.ThrowIfNull(value); _data = value.Copy(); }
    internal FxWorldDraft Clone() => new(_data);
}

public sealed class FxWorldAuthoringAdapter : AssetAuthoringAdapter<FxWorldAuthoredSnapshot, FxWorldDraft, FxWorldBuildData>
{
    private static readonly FxWorldBodyEmitter Validator = new();
    public override XAssetType AssetType => XAssetType.FxMap;
    public override FxWorldAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => FxWorldAuthoredSnapshot.Import(source);
    public override FxWorldDraft CreateDraft(FxWorldAuthoredSnapshot snapshot) => new(snapshot.Data);
    public override FxWorldDraft CloneDraft(FxWorldDraft draft) => draft.Clone();
    public override IReadOnlyList<AssetValidationIssue> ValidateDraft(FxWorldDraft draft) => Validator.Validate(draft.Data).Select(value => new AssetValidationIssue(value.Path, value.Message, AssetValidationSeverity.Error)).ToArray();
    public override bool SemanticallyEquals(FxWorldDraft left, FxWorldDraft right) => JsonSerializer.Serialize(left.Data) == JsonSerializer.Serialize(right.Data);
    public override FxWorldBuildData ExportBuildData(FxWorldDraft draft)
    {
        FxWorldBuildData data = draft.Data;
        if (Validator.Validate(data).Count != 0)
            throw new InvalidOperationException("FxMap draft has validation errors and cannot produce build data.");
        return data;
    }
}
