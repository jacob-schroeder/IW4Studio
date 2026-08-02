using System.Text.Json;
using System.Text.Json.Serialization;
using IW4.Assets.Assets.GameMap;
using IW4.FastFiles.Strings;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Assets;

namespace IW4.Studio.Documents;

/// <summary>
/// Detached GameMapSp state.  The source models are copied at registration
/// time, retaining the loader-visible path/tree/vehicle graph by managed
/// identity while intentionally dropping all pointer-cell and runtime-address
/// provenance.  In particular, ScriptString runtime handles and destination
/// cells are not part of authored state.
/// </summary>
public sealed class GameWorldSpAuthoredSnapshot : ITargetZoneDetachedSemanticSnapshot
{
    internal GameWorldSpAuthoredSnapshot(GameWorldSpBuildData data) => Data = data.Copy();
    internal GameWorldSpBuildData Data { get; }
    public XAssetType AssetType => XAssetType.GameMapSp;

    internal static GameWorldSpAuthoredSnapshot Import(TargetZoneRowSource source) =>
        source.AuthoredDefinition?.SemanticSnapshot is GameWorldSpAuthoredSnapshot snapshot
            ? snapshot
            : throw new InvalidDataException("GameMapSp editing requires a capture-time detached semantic snapshot.");

    internal static GameWorldSpAuthoredSnapshot FromLoaded(GameWorldSpAsset asset) =>
        new(GameWorldSpBuildData.FromLoaded(asset));
}

public sealed class GameWorldSpBuildData : IGameWorldSpBuildData
{
    public GameWorldSpBuildData(string? name, PathData path, VehicleTrack vehicleTrack, GGlassData? glassData)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(vehicleTrack);
        Name = name;
        Path = ClonePath(path);
        VehicleTrack = CloneVehicleTrack(vehicleTrack);
        GlassData = CloneGlass(glassData);
    }

    public XAssetType AssetType => XAssetType.GameMapSp;
    public string? Name { get; }
    public PathData Path { get; }
    public VehicleTrack VehicleTrack { get; }
    public GGlassData? GlassData { get; }

    internal GameWorldSpBuildData Copy() => new(Name, Path, VehicleTrack, GlassData);
    internal static GameWorldSpBuildData FromLoaded(GameWorldSpAsset asset) => new(asset.Name, asset.Path, asset.VehicleTrack, asset.GlassData);

    private static PathData ClonePath(PathData value)
    {
        var trees = new Dictionary<PathNodeTree, PathNodeTree>(ReferenceEqualityComparer.Instance);
        return new PathData
        {
            NodeCount = value.NodeCount,
            Nodes = value.Nodes.Select(CloneNode).ToArray(),
            BaseNodes = value.BaseNodes.Select(node => new PathBaseNode { Origin = node.Origin, Type = node.Type }).ToArray(),
            ChainNodeCount = value.ChainNodeCount,
            ChainNodeForNode = value.ChainNodeForNode.ToArray(),
            NodeForChainNode = value.NodeForChainNode.ToArray(),
            VisBytes = value.VisBytes,
            PathVis = value.PathVis.ToArray(),
            NodeTreeCount = value.NodeTreeCount,
            NodeTree = value.NodeTree.Select(tree => CloneTree(tree, trees)).ToArray()
        };
    }

    private static PathNode CloneNode(PathNode node) => new()
    {
        Constant = new PathNodeConstant
        {
            NodeType = node.Constant.NodeType,
            SpawnFlags = node.Constant.SpawnFlags,
            TargetName = CloneScript(node.Constant.TargetName),
            ScriptLinkName = CloneScript(node.Constant.ScriptLinkName),
            ScriptNoteworthy = CloneScript(node.Constant.ScriptNoteworthy),
            Target = CloneScript(node.Constant.Target),
            AnimScript = CloneScript(node.Constant.AnimScript),
            AnimScriptFunc = node.Constant.AnimScriptFunc,
            Origin = node.Constant.Origin,
            Angle = node.Constant.Angle,
            ForwardX = node.Constant.ForwardX,
            ForwardY = node.Constant.ForwardY,
            Radius = node.Constant.Radius,
            MinUseDistSq = node.Constant.MinUseDistSq,
            OverlapNode0 = node.Constant.OverlapNode0,
            OverlapNode1 = node.Constant.OverlapNode1,
            TotalLinkCount = node.Constant.TotalLinkCount,
            Pad3A = node.Constant.Pad3A,
            Links = node.Constant.Links.Select(link => new PathLink
            {
                Distance = link.Distance,
                NodeNumber = link.NodeNumber,
                DisconnectCount = link.DisconnectCount,
                NegotiationLink = link.NegotiationLink,
                BadPlaceCount0 = link.BadPlaceCount0,
                BadPlaceCount1 = link.BadPlaceCount1,
                BadPlaceCount2 = link.BadPlaceCount2,
                BadPlaceCount3 = link.BadPlaceCount3
            }).ToArray()
        },
        Dynamic = new PathNodeDynamic
        {
            OwnerHandle = node.Dynamic.OwnerHandle,
            Pad42 = node.Dynamic.Pad42,
            FreeTime = node.Dynamic.FreeTime,
            ValidTimes = node.Dynamic.ValidTimes.ToArray(),
            DangerousNodeTimes = node.Dynamic.DangerousNodeTimes.ToArray(),
            InPlayerLosTime = node.Dynamic.InPlayerLosTime,
            LinkCount = node.Dynamic.LinkCount,
            OverlapCount = node.Dynamic.OverlapCount,
            TurretEntityNumber = node.Dynamic.TurretEntityNumber,
            UserCount = node.Dynamic.UserCount,
            HasBadPlaceLink = node.Dynamic.HasBadPlaceLink
        },
        // These three cells are post-load search pointers rather than source
        // descriptors.  They must not be retained from the loaded runtime.
        Transient = new PathNodeTransient
        {
            SearchFrame = node.Transient.SearchFrame,
            NextOpenRuntimePointer = 0,
            PreviousOpenRuntimePointer = 0,
            ParentRuntimePointer = 0,
            Cost = node.Transient.Cost,
            Heuristic = node.Transient.Heuristic,
            NodeCostOrLinkIndexBits = node.Transient.NodeCostOrLinkIndexBits
        }
    };

    private static ScriptStringReference CloneScript(ScriptStringReference value) =>
        new(value.RawLocalIndex, value.Text, ScriptStringHandle.Null, default);

    private static PathNodeTree CloneTree(PathNodeTree value, IDictionary<PathNodeTree, PathNodeTree> known)
    {
        if (known.TryGetValue(value, out PathNodeTree? existing))
            return existing;
        var clone = new PathNodeTree
        {
            Axis = value.Axis,
            Distance = value.Distance,
            NodeCount = value.NodeCount,
            Nodes = value.Nodes.ToArray()
        };
        known.Add(value, clone);
        // Packed child pointers can close a graph cycle or refer forward to a
        // later table member.  Allocate the detached identity first, then
        // connect its children so the graph survives unchanged.
        clone.Child0 = value.Child0 is null ? null : CloneTree(value.Child0, known);
        clone.Child1 = value.Child1 is null ? null : CloneTree(value.Child1, known);
        return clone;
    }

    private static VehicleTrack CloneVehicleTrack(VehicleTrack value)
    {
        var known = new Dictionary<VehicleTrackSegment, VehicleTrackSegment>(ReferenceEqualityComparer.Instance);
        return new VehicleTrack
        {
            SegmentCount = value.SegmentCount,
            Segments = value.Segments.Select(segment => CloneSegment(segment, known)).ToArray()
        };
    }

    private static VehicleTrackSegment CloneSegment(VehicleTrackSegment value, IDictionary<VehicleTrackSegment, VehicleTrackSegment> known)
    {
        if (known.TryGetValue(value, out VehicleTrackSegment? existing))
            return existing;
        // A packed branch can reference a previously registered segment;
        // inline payloads cannot form a source recursion.  Registering a
        // completed segment maintains every loaded alias without retaining
        // its original block address or pointer cell.
        var clone = new VehicleTrackSegment
        {
            Name = value.Name,
            Sectors = value.Sectors.Select(CloneSector).ToArray(),
            SectorCount = value.SectorCount,
            NextBranches = value.NextBranches.Select(branch => branch is null ? null : CloneSegment(branch, known)).ToArray(),
            NextBranchCount = value.NextBranchCount,
            PreviousBranches = value.PreviousBranches.Select(branch => branch is null ? null : CloneSegment(branch, known)).ToArray(),
            PreviousBranchCount = value.PreviousBranchCount,
            EndEdgeDirection = value.EndEdgeDirection.ToArray(),
            EndEdgeDistance = value.EndEdgeDistance,
            TotalLength = value.TotalLength
        };
        known.Add(value, clone);
        return clone;
    }

    private static VehicleTrackSector CloneSector(VehicleTrackSector value) => new()
    {
        StartEdgeDirection = value.StartEdgeDirection.ToArray(),
        StartEdgeDistance = value.StartEdgeDistance,
        LeftEdgeDirection = value.LeftEdgeDirection.ToArray(),
        LeftEdgeDistance = value.LeftEdgeDistance,
        RightEdgeDirection = value.RightEdgeDirection.ToArray(),
        RightEdgeDistance = value.RightEdgeDistance,
        SectorLength = value.SectorLength,
        SectorWidth = value.SectorWidth,
        TotalPriorLength = value.TotalPriorLength,
        TotalFollowingLength = value.TotalFollowingLength,
        Obstacles = value.Obstacles.Select(obstacle => new VehicleTrackObstacle { Origin = obstacle.Origin.ToArray(), Radius = obstacle.Radius }).ToArray(),
        ObstacleCount = value.ObstacleCount
    };

    private static GGlassData? CloneGlass(GGlassData? value) => value is null ? null : new()
    {
        GlassPieces = value.GlassPieces.Select(piece => new GGlassPiece
        {
            DamageTaken = piece.DamageTaken,
            CollapseTime = piece.CollapseTime,
            LastStateChangeTime = piece.LastStateChangeTime,
            PackedImpactDir = piece.PackedImpactDir,
            PackedImpactPos = piece.PackedImpactPos
        }).ToArray(),
        PieceCount = value.PieceCount,
        DamageToWeaken = value.DamageToWeaken,
        DamageToDestroy = value.DamageToDestroy,
        GlassNameCount = value.GlassNameCount,
        GlassNames = value.GlassNames.Select(name => new GGlassName
        {
            NameStr = name.NameStr,
            Name = name.Name,
            PieceCount = name.PieceCount,
            PieceIndices = name.PieceIndices.ToArray()
        }).ToArray(),
        Pad14To7F = value.Pad14To7F.ToArray()
    };
}

public sealed class GameWorldSpDraft
{
    private GameWorldSpBuildData _data;
    internal GameWorldSpDraft(GameWorldSpBuildData data) => _data = data.Copy();
    public GameWorldSpBuildData Data => _data.Copy();
    public void Replace(GameWorldSpBuildData value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _data = value.Copy();
    }
    internal GameWorldSpDraft Clone() => new(_data);
}

public sealed class GameWorldSpAuthoringAdapter : AssetAuthoringAdapter<GameWorldSpAuthoredSnapshot, GameWorldSpDraft, GameWorldSpBuildData>
{
    private static readonly GameWorldSpBodyEmitter Validator = new();
    public override XAssetType AssetType => XAssetType.GameMapSp;
    public override GameWorldSpAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => GameWorldSpAuthoredSnapshot.Import(source);
    public override GameWorldSpDraft CreateDraft(GameWorldSpAuthoredSnapshot snapshot) => new(snapshot.Data);
    public override GameWorldSpDraft CloneDraft(GameWorldSpDraft draft) => draft.Clone();
    public override IReadOnlyList<AssetValidationIssue> ValidateDraft(GameWorldSpDraft draft) =>
        Validator.Validate(draft.Data).Select(value => new AssetValidationIssue(value.Path, value.Message, AssetValidationSeverity.Error)).ToArray();
    public override bool SemanticallyEquals(GameWorldSpDraft left, GameWorldSpDraft right) =>
        JsonSerializer.Serialize(left.Data, SemanticJsonOptions) == JsonSerializer.Serialize(right.Data, SemanticJsonOptions);
    public override GameWorldSpBuildData ExportBuildData(GameWorldSpDraft draft)
    {
        GameWorldSpBuildData data = draft.Data;
        if (Validator.Validate(data).Count != 0)
            throw new InvalidOperationException("GameMapSp draft has validation errors and cannot produce build data.");
        return data;
    }

    private static JsonSerializerOptions SemanticJsonOptions { get; } = new() { ReferenceHandler = ReferenceHandler.Preserve };
}
