namespace IW4.Studio.MapEditor.Compilation.Ownership;

/// <summary>
/// Closed set of cross-root and cross-family identity contracts required
/// before an initial multiplayer map may be generated.
/// </summary>
public enum InitialMpMapRelationship
{
    SharedMapRootIdentity = 0,
    SharedPrimaryMapChecksum,
    SharedStaticModelOrdinal,
    SharedInlineModelOrdinal,
    DynamicEntityBrushModelOrdinal,
    DynamicEntityPhysicsBrushModelOrdinal,
    SharedGlassPieceOrdinal,
    CollisionGlassPieceHandle,
    GlassNamePieceMembership,
    SharedPrimaryLightOrdinal,
    StagePrimaryLightOrdinal,
    MapEntTriggerRanges,
    StageTriggerOrdinal,
    DynamicEntityParallelSlots,
    DynamicEntityDpvsShape,
    SymbolicZoneDependencyClosure,
    ZoneScriptStringResolution
}

public enum MapRelationshipKind
{
    SharedIdentity = 0,
    EqualScalar = 1,
    SharedOrdinal = 2,
    OneBasedHandleToZeroBasedOrdinal = 3,
    ParallelCardinality = 4,
    ContiguousRange = 5,
    SymbolicDependency = 6,
    ScriptStringResolution = 7,
    IndependentNamespace = 8
}

public enum MapRelationshipEndpointRole
{
    Authority = 0,
    Participant = 1,
    Selector = 2,
    Target = 3,
    Cardinality = 4,
    Dependency = 5,
    RuntimeConsumer = 6
}

public enum MapRelationshipIdentityReadiness
{
    ConsumerContractProven = 0,
    OwnershipContractLocked = 1,
    ReverseEngineeringPending = 2
}

public enum MapRelationshipGenerationReadiness
{
    ExistingValuePreservationReady = 0,
    DeterministicPolicyReady = 1,
    CompilerAlgorithmPending = 2,
    CrossAssetCompilerPending = 3,
    ExistingReferenceLinkingReady = 4,
    DependencyClosurePending = 5
}

public sealed class MapRelationshipEndpoint
{
    public MapRelationshipEndpoint(
        InitialMpMapAssetRoot root,
        string path,
        MapRelationshipEndpointRole role)
    {
        if (!Enum.IsDefined(root))
            throw new ArgumentOutOfRangeException(nameof(root));
        if (!Enum.IsDefined(role))
            throw new ArgumentOutOfRangeException(nameof(role));
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string expectedPrefix = root == InitialMpMapAssetRoot.TargetZone
            ? "$.targetZone"
            : "$.definition";
        if (!path.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"{root} relationship paths must begin with " +
                $"'{expectedPrefix}'.",
                nameof(path));
        }

        Root = root;
        Path = path;
        Role = role;
    }

    public InitialMpMapAssetRoot Root { get; }
    public string Path { get; }
    public MapRelationshipEndpointRole Role { get; }
}

/// <summary>
/// One M0 identity/index contract and the later milestone responsible for
/// generating values that satisfy it. Identity proof never implies that the
/// named generation compiler already exists.
/// </summary>
public sealed class MapCrossAssetRelationshipContract
{
    private readonly IReadOnlyList<MapRelationshipEndpoint> _endpoints;

    public MapCrossAssetRelationshipContract(
        InitialMpMapRelationship relationship,
        MapRelationshipKind kind,
        IEnumerable<MapRelationshipEndpoint> endpoints,
        MapRelationshipIdentityReadiness identityReadiness,
        MapCompilerMilestone generationMilestone,
        MapRelationshipGenerationReadiness generationReadiness,
        string identityContract)
    {
        if (!Enum.IsDefined(relationship))
            throw new ArgumentOutOfRangeException(nameof(relationship));
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(identityReadiness))
            throw new ArgumentOutOfRangeException(nameof(identityReadiness));
        if (!Enum.IsDefined(generationMilestone))
            throw new ArgumentOutOfRangeException(nameof(generationMilestone));
        if (!Enum.IsDefined(generationReadiness))
            throw new ArgumentOutOfRangeException(nameof(generationReadiness));
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(identityContract);
        if (generationMilestone is
            MapCompilerMilestone.M0OwnershipLock or
            MapCompilerMilestone.M8ProductionHardening)
        {
            throw new ArgumentException(
                "Cross-family generation must name a concrete M3-M7 " +
                "compiler milestone, separately from the M0 identity lock.",
                nameof(generationMilestone));
        }

        MapRelationshipEndpoint[] copy = endpoints.ToArray();
        if (copy.Length < 2)
        {
            throw new ArgumentException(
                "A cross-family relationship requires at least two endpoints.",
                nameof(endpoints));
        }
        if (copy.GroupBy(value => (value.Root, value.Path, value.Role))
            .Any(group => group.Count() != 1))
        {
            throw new ArgumentException(
                "A cross-family relationship contains a duplicate endpoint.",
                nameof(endpoints));
        }

        Relationship = relationship;
        Kind = kind;
        _endpoints = Array.AsReadOnly(copy);
        IdentityReadiness = identityReadiness;
        GenerationMilestone = generationMilestone;
        GenerationReadiness = generationReadiness;
        IdentityContract = identityContract;
    }

    public InitialMpMapRelationship Relationship { get; }
    public MapRelationshipKind Kind { get; }
    public IReadOnlyList<MapRelationshipEndpoint> Endpoints => _endpoints;
    public MapCompilerMilestone IdentityMilestone =>
        MapCompilerMilestone.M0OwnershipLock;
    public MapRelationshipIdentityReadiness IdentityReadiness { get; }
    public MapCompilerMilestone GenerationMilestone { get; }
    public MapRelationshipGenerationReadiness GenerationReadiness { get; }
    public string IdentityContract { get; }
}

public sealed class MapCrossAssetRelationshipCatalog
{
    private readonly IReadOnlyList<MapCrossAssetRelationshipContract>
        _relationships;
    private readonly IReadOnlyDictionary<
        InitialMpMapRelationship,
        MapCrossAssetRelationshipContract> _byRelationship;

    public MapCrossAssetRelationshipCatalog(
        IEnumerable<MapCrossAssetRelationshipContract> relationships)
    {
        ArgumentNullException.ThrowIfNull(relationships);
        MapCrossAssetRelationshipContract[] copy =
            relationships.ToArray();
        if (copy.Select(value => value.Relationship).Distinct().Count() !=
            copy.Length)
        {
            throw new InvalidDataException(
                "The initial-MP relationship catalog contains a duplicate.");
        }

        InitialMpMapRelationship[] required =
            Enum.GetValues<InitialMpMapRelationship>();
        InitialMpMapRelationship[] missing = required
            .Except(copy.Select(value => value.Relationship))
            .ToArray();
        InitialMpMapRelationship[] extra = copy
            .Select(value => value.Relationship)
            .Except(required)
            .ToArray();
        if (missing.Length != 0 || extra.Length != 0)
        {
            throw new InvalidDataException(
                "The initial-MP relationship catalog is not closed. " +
                $"Missing=[{string.Join(", ", missing)}], " +
                $"extra=[{string.Join(", ", extra)}].");
        }

        _relationships = Array.AsReadOnly(copy);
        _byRelationship =
            new System.Collections.ObjectModel.ReadOnlyDictionary<
                InitialMpMapRelationship,
                MapCrossAssetRelationshipContract>(
                copy.ToDictionary(value => value.Relationship));
    }

    public IReadOnlyList<MapCrossAssetRelationshipContract> Relationships =>
        _relationships;
    public int RequiredRelationshipCount =>
        Enum.GetValues<InitialMpMapRelationship>().Length;

    public MapCrossAssetRelationshipContract Require(
        InitialMpMapRelationship relationship) =>
        _byRelationship.TryGetValue(
            relationship,
            out MapCrossAssetRelationshipContract? contract)
            ? contract
            : throw new KeyNotFoundException(
                $"No initial-MP relationship is registered for {relationship}.");
}

/// <summary>
/// Complete current M0 cross-root relationship catalog. Generation ownership
/// is deliberately assigned to later milestones.
/// </summary>
public static class InitialMpMapRelationships
{
    public static MapCrossAssetRelationshipCatalog Current { get; } = new(
    [
        Relationship(
            InitialMpMapRelationship.SharedMapRootIdentity,
            MapRelationshipKind.SharedIdentity,
            MapRelationshipIdentityReadiness.ConsumerContractProven,
            MapCompilerMilestone.M7GreenfieldLink,
            MapRelationshipGenerationReadiness.DeterministicPolicyReady,
            "GfxMap, ColMapMp, ComMap, MapEnts, FxMap, and GameMapMp use " +
            "one normalized map identity. Serialized spelling is retained " +
            "per row, but lookup identity cannot diverge.",
            E(InitialMpMapAssetRoot.GfxMap, "$.definition.name", MapRelationshipEndpointRole.Participant),
            E(InitialMpMapAssetRoot.ColMapMp, "$.definition.name", MapRelationshipEndpointRole.Participant),
            E(InitialMpMapAssetRoot.ComMap, "$.definition.name", MapRelationshipEndpointRole.Participant),
            E(InitialMpMapAssetRoot.MapEnts, "$.definition.name", MapRelationshipEndpointRole.Participant),
            E(InitialMpMapAssetRoot.FxMap, "$.definition.name", MapRelationshipEndpointRole.Participant),
            E(InitialMpMapAssetRoot.GameMapMp, "$.definition.name", MapRelationshipEndpointRole.Participant),
            E(InitialMpMapAssetRoot.TargetZone, "$.targetZone.assetRows[mapRoots].logicalName", MapRelationshipEndpointRole.Authority)),
        Relationship(
            InitialMpMapRelationship.SharedPrimaryMapChecksum,
            MapRelationshipKind.EqualScalar,
            MapRelationshipIdentityReadiness.ConsumerContractProven,
            MapCompilerMilestone.M3StructuralGeometry,
            MapRelationshipGenerationReadiness.DeterministicPolicyReady,
            "Both fields are uint32 and must be numerically equal. " +
            "ImportedContentPreserved output retains the production value; " +
            "any semantic source, settings, dependency, or compiler-contract " +
            "change is StudioAuthoredContent and writes one MapPrimaryChecksumPolicy " +
            "StudioCanonicalV1 standard CRC32 over domain-separated whole-map " +
            "content identity to both roots. The retail byte scope is not claimed.",
            E(InitialMpMapAssetRoot.GfxMap, "$.definition.checksum", MapRelationshipEndpointRole.Participant),
            E(InitialMpMapAssetRoot.ColMapMp, "$.definition.checksum", MapRelationshipEndpointRole.Participant)),
        Relationship(
            InitialMpMapRelationship.SharedStaticModelOrdinal,
            MapRelationshipKind.SharedOrdinal,
            MapRelationshipIdentityReadiness.OwnershipContractLocked,
            MapCompilerMilestone.M5LightingEnvironment,
            MapRelationshipGenerationReadiness.CrossAssetCompilerPending,
            "A paired static-model source owns one shared ordinal across the " +
            "Gfx draw/instance rows and Col placement row. XModel identity and " +
            "transform must agree; spatial, collision, probe, light, and shadow " +
            "derivatives are rebuilt atomically.",
            E(InitialMpMapAssetRoot.GfxMap, "$.definition.dpvs.smodelDrawInsts[i]", MapRelationshipEndpointRole.Participant),
            E(InitialMpMapAssetRoot.GfxMap, "$.definition.dpvs.smodelInsts[i]", MapRelationshipEndpointRole.Participant),
            E(InitialMpMapAssetRoot.ColMapMp, "$.definition.staticModelList[i]", MapRelationshipEndpointRole.Participant)),
        Relationship(
            InitialMpMapRelationship.SharedInlineModelOrdinal,
            MapRelationshipKind.SharedOrdinal,
            MapRelationshipIdentityReadiness.ConsumerContractProven,
            MapCompilerMilestone.M3StructuralGeometry,
            MapRelationshipGenerationReadiness.CrossAssetCompilerPending,
            "MapEnt textual model '*n' selects GfxMap.models[n] and " +
            "ColMapMp.cmodels[n] with no decrement or remap. Slot zero is the " +
            "world model and textual brush-model references use dense prefix " +
            "1..T. Across the official MP corpus, the remaining table suffix " +
            "is owned exactly by slot-1 dynamic brush rows.",
            E(InitialMpMapAssetRoot.MapEnts, "$.definition.entityString.entities[].model=\"*n\"", MapRelationshipEndpointRole.Selector),
            E(InitialMpMapAssetRoot.GfxMap, "$.definition.models[n]", MapRelationshipEndpointRole.Target),
            E(InitialMpMapAssetRoot.ColMapMp, "$.definition.cmodels[n]", MapRelationshipEndpointRole.Target)),
        Relationship(
            InitialMpMapRelationship.DynamicEntityBrushModelOrdinal,
            MapRelationshipKind.SharedOrdinal,
            MapRelationshipIdentityReadiness.ConsumerContractProven,
            MapCompilerMilestone.M6GlassAndGameplay,
            MapRelationshipGenerationReadiness.CrossAssetCompilerPending,
            "Let T be the final dense MapEnt '*n' ordinal. Slot-1 dynamic " +
            "brush row j owns inline-model ordinal 1+T+j in both Gfx and Col. " +
            "This suffix immediately follows the MapEnt prefix with no holes.",
            E(InitialMpMapAssetRoot.MapEnts, "$.definition.entityString.maxDenseBrushModelOrdinal=T", MapRelationshipEndpointRole.Cardinality),
            E(InitialMpMapAssetRoot.ColMapMp, "$.definition.dynEntDefList[1][j].brushModel=1+T+j", MapRelationshipEndpointRole.Selector),
            E(InitialMpMapAssetRoot.GfxMap, "$.definition.models[1+T+j]", MapRelationshipEndpointRole.Target),
            E(InitialMpMapAssetRoot.ColMapMp, "$.definition.cmodels[1+T+j]", MapRelationshipEndpointRole.Target)),
        Relationship(
            InitialMpMapRelationship.DynamicEntityPhysicsBrushModelOrdinal,
            MapRelationshipKind.IndependentNamespace,
            MapRelationshipIdentityReadiness.ReverseEngineeringPending,
            MapCompilerMilestone.M6GlassAndGameplay,
            MapRelationshipGenerationReadiness.CrossAssetCompilerPending,
            "A nonzero DynEntityDef.physicsBrushModel is an independent " +
            "selector. Its sentinel and exact target-domain contract remain " +
            "unproven and cannot be inferred from the proven brushModel suffix.",
            E(InitialMpMapAssetRoot.ColMapMp, "$.definition.dynEntDefList[slot][].physicsBrushModel", MapRelationshipEndpointRole.Selector),
            E(InitialMpMapAssetRoot.ColMapMp, "$.definition.dynEntDefList[slot][].brushModel", MapRelationshipEndpointRole.Participant)),
        Relationship(
            InitialMpMapRelationship.SharedGlassPieceOrdinal,
            MapRelationshipKind.SharedOrdinal,
            MapRelationshipIdentityReadiness.ConsumerContractProven,
            MapCompilerMilestone.M6GlassAndGameplay,
            MapRelationshipGenerationReadiness.CrossAssetCompilerPending,
            "Zero-based piece ordinal i identifies FxMap initial piece i, " +
            "GameMapMp gameplay piece i, and glass-name membership value i. " +
            "Fx initial-piece and Game piece cardinalities must be equal.",
            E(InitialMpMapAssetRoot.FxMap, "$.definition.glassSystem.initPieceStates[i]", MapRelationshipEndpointRole.Participant),
            E(InitialMpMapAssetRoot.GameMapMp, "$.definition.glassData.glassPieces[i]", MapRelationshipEndpointRole.Participant),
            E(InitialMpMapAssetRoot.GameMapMp, "$.definition.glassData.glassNames[].pieceIndices[*]=i", MapRelationshipEndpointRole.Selector)),
        Relationship(
            InitialMpMapRelationship.CollisionGlassPieceHandle,
            MapRelationshipKind.OneBasedHandleToZeroBasedOrdinal,
            MapRelationshipIdentityReadiness.ConsumerContractProven,
            MapCompilerMilestone.M6GlassAndGameplay,
            MapRelationshipGenerationReadiness.CrossAssetCompilerPending,
            "CBrush.glassPieceIndex zero means no glass. A nonzero handle h " +
            "selects shared zero-based Fx/Game piece ordinal h-1. Duplicate " +
            "brush handles are legal; distinct nonzero handles form 1..N.",
            E(InitialMpMapAssetRoot.ColMapMp, "$.definition.brushes[].glassPieceIndex", MapRelationshipEndpointRole.Selector),
            E(InitialMpMapAssetRoot.FxMap, "$.definition.glassSystem.initPieceStates[h-1]", MapRelationshipEndpointRole.Target),
            E(InitialMpMapAssetRoot.GameMapMp, "$.definition.glassData.glassPieces[h-1]", MapRelationshipEndpointRole.Target)),
        Relationship(
            InitialMpMapRelationship.GlassNamePieceMembership,
            MapRelationshipKind.ContiguousRange,
            MapRelationshipIdentityReadiness.ConsumerContractProven,
            MapCompilerMilestone.M6GlassAndGameplay,
            MapRelationshipGenerationReadiness.CrossAssetCompilerPending,
            "Each gameplay glass-name row owns an explicit ushort list of " +
            "shared zero-based piece ordinals; every value must be within the " +
            "equal Fx/Game initial-piece domain.",
            E(InitialMpMapAssetRoot.GameMapMp, "$.definition.glassData.glassNames[].pieceIndices[]", MapRelationshipEndpointRole.Selector),
            E(InitialMpMapAssetRoot.GameMapMp, "$.definition.glassData.pieceCount", MapRelationshipEndpointRole.Cardinality),
            E(InitialMpMapAssetRoot.FxMap, "$.definition.glassSystem.initPieceCount", MapRelationshipEndpointRole.Cardinality)),
        Relationship(
            InitialMpMapRelationship.SharedPrimaryLightOrdinal,
            MapRelationshipKind.SharedOrdinal,
            MapRelationshipIdentityReadiness.OwnershipContractLocked,
            MapCompilerMilestone.M5LightingEnvironment,
            MapRelationshipGenerationReadiness.CrossAssetCompilerPending,
            "Primary-light ordinal i originates in ComMap.primaryLights[i] " +
            "and indexes Gfx primary-light-sized shadowGeometry[i] and " +
            "lightRegions[i]. Gfx primaryLightCount and the Com row count must " +
            "agree; the directional-sun index uses that same domain.",
            E(InitialMpMapAssetRoot.ComMap, "$.definition.primaryLights[i]", MapRelationshipEndpointRole.Authority),
            E(InitialMpMapAssetRoot.GfxMap, "$.definition.primaryLightCount", MapRelationshipEndpointRole.Cardinality),
            E(InitialMpMapAssetRoot.GfxMap, "$.definition.sunPrimaryLightIndex", MapRelationshipEndpointRole.Selector),
            E(InitialMpMapAssetRoot.GfxMap, "$.definition.shadowGeom[i]", MapRelationshipEndpointRole.Target),
            E(InitialMpMapAssetRoot.GfxMap, "$.definition.lightRegions[i]", MapRelationshipEndpointRole.Target)),
        Relationship(
            InitialMpMapRelationship.StagePrimaryLightOrdinal,
            MapRelationshipKind.SharedOrdinal,
            MapRelationshipIdentityReadiness.OwnershipContractLocked,
            MapCompilerMilestone.M6GlassAndGameplay,
            MapRelationshipGenerationReadiness.CrossAssetCompilerPending,
            "A MapEnt stage sunPrimaryLightIndex selects the shared Com/Gfx " +
            "primary-light ordinal domain. Stage generation may not invent a " +
            "separate light numbering scheme.",
            E(InitialMpMapAssetRoot.MapEnts, "$.definition.stages[].sunPrimaryLightIndex", MapRelationshipEndpointRole.Selector),
            E(InitialMpMapAssetRoot.ComMap, "$.definition.primaryLights[]", MapRelationshipEndpointRole.Target),
            E(InitialMpMapAssetRoot.GfxMap, "$.definition.primaryLightCount", MapRelationshipEndpointRole.Cardinality)),
        Relationship(
            InitialMpMapRelationship.MapEntTriggerRanges,
            MapRelationshipKind.ContiguousRange,
            MapRelationshipIdentityReadiness.OwnershipContractLocked,
            MapCompilerMilestone.M6GlassAndGameplay,
            MapRelationshipGenerationReadiness.CompilerAlgorithmPending,
            "TriggerModel (firstHull,hullCount) ranges address trigger.hulls; " +
            "TriggerHull (firstSlab,slabCount) ranges address trigger.slabs. " +
            "Counts and every half-open range must remain in the selected domain.",
            E(InitialMpMapAssetRoot.MapEnts, "$.definition.trigger.models[].{firstHull,hullCount}", MapRelationshipEndpointRole.Selector),
            E(InitialMpMapAssetRoot.MapEnts, "$.definition.trigger.hulls[]", MapRelationshipEndpointRole.Target),
            E(InitialMpMapAssetRoot.MapEnts, "$.definition.trigger.hulls[].{firstSlab,slabCount}", MapRelationshipEndpointRole.Selector),
            E(InitialMpMapAssetRoot.MapEnts, "$.definition.trigger.slabs[]", MapRelationshipEndpointRole.Target)),
        Relationship(
            InitialMpMapRelationship.StageTriggerOrdinal,
            MapRelationshipKind.SharedOrdinal,
            MapRelationshipIdentityReadiness.OwnershipContractLocked,
            MapCompilerMilestone.M6GlassAndGameplay,
            MapRelationshipGenerationReadiness.CompilerAlgorithmPending,
            "Stage.triggerIndex selects one MapEnt trigger-model ordinal and " +
            "must be in range of the compiled trigger model table.",
            E(InitialMpMapAssetRoot.MapEnts, "$.definition.stages[].triggerIndex", MapRelationshipEndpointRole.Selector),
            E(InitialMpMapAssetRoot.MapEnts, "$.definition.trigger.models[]", MapRelationshipEndpointRole.Target)),
        Relationship(
            InitialMpMapRelationship.DynamicEntityParallelSlots,
            MapRelationshipKind.ParallelCardinality,
            MapRelationshipIdentityReadiness.ConsumerContractProven,
            MapCompilerMilestone.M6GlassAndGameplay,
            MapRelationshipGenerationReadiness.CrossAssetCompilerPending,
            "For each independent slot 0 and 1, dynEntCount[slot] equals the " +
            "definition, pose, client, and collision row counts. Ordinals are " +
            "slot-local and cannot cross slots.",
            E(InitialMpMapAssetRoot.ColMapMp, "$.definition.dynEntCount[slot]", MapRelationshipEndpointRole.Cardinality),
            E(InitialMpMapAssetRoot.ColMapMp, "$.definition.dynEntDefList[slot][]", MapRelationshipEndpointRole.Participant),
            E(InitialMpMapAssetRoot.ColMapMp, "$.definition.dynEntPoseList[slot][]", MapRelationshipEndpointRole.Participant),
            E(InitialMpMapAssetRoot.ColMapMp, "$.definition.dynEntClientList[slot][]", MapRelationshipEndpointRole.Participant),
            E(InitialMpMapAssetRoot.ColMapMp, "$.definition.dynEntCollList[slot][]", MapRelationshipEndpointRole.Participant)),
        Relationship(
            InitialMpMapRelationship.DynamicEntityDpvsShape,
            MapRelationshipKind.ParallelCardinality,
            MapRelationshipIdentityReadiness.OwnershipContractLocked,
            MapCompilerMilestone.M6GlassAndGameplay,
            MapRelationshipGenerationReadiness.CrossAssetCompilerPending,
            "Gfx DPVS dynamic client counts and word counts are derived from " +
            "the same two ColMap dynamic-entity slot domains; Gfx runtime cell " +
            "and visibility allocations are shaped from those shared counts.",
            E(InitialMpMapAssetRoot.ColMapMp, "$.definition.dynEntCount[2]", MapRelationshipEndpointRole.Authority),
            E(InitialMpMapAssetRoot.GfxMap, "$.definition.dpvsDyn.dynEntClientCount[2]", MapRelationshipEndpointRole.Cardinality),
            E(InitialMpMapAssetRoot.GfxMap, "$.definition.dpvsDyn.dynEntClientWordCount[2]", MapRelationshipEndpointRole.Cardinality),
            E(InitialMpMapAssetRoot.GfxMap, "$.definition.dpvsDyn.{dynEntCellBits[][],dynEntVisData[][]}", MapRelationshipEndpointRole.RuntimeConsumer)),
        Relationship(
            InitialMpMapRelationship.SymbolicZoneDependencyClosure,
            MapRelationshipKind.SymbolicDependency,
            MapRelationshipIdentityReadiness.OwnershipContractLocked,
            MapCompilerMilestone.M7GreenfieldLink,
            MapRelationshipGenerationReadiness.DependencyClosurePending,
            "Every map-root LinkerReference resolves by typed symbolic asset " +
            "identity into the target-zone graph. Loaded objects, pool rows, " +
            "and imported pointer raws are never dependency identity. The " +
            "initial vertical slice may reuse existing dependencies but must " +
            "validate complete closure.",
            E(InitialMpMapAssetRoot.GfxMap, "$.definition.{skies,worldDraw,materialMemory,sun,outdoorImage,dpvs}.{image,material,xmodel}References", MapRelationshipEndpointRole.Dependency),
            E(InitialMpMapAssetRoot.ColMapMp, "$.definition.{staticModelList,mapEnts,dynEntDefList}.{xmodel,mapEnts,destroyFx,physPreset}References", MapRelationshipEndpointRole.Dependency),
            E(InitialMpMapAssetRoot.ComMap, "$.definition.primaryLights[].defName", MapRelationshipEndpointRole.Dependency),
            E(InitialMpMapAssetRoot.FxMap, "$.definition.glassSystem.defs[].{material,materialShattered,physPreset}", MapRelationshipEndpointRole.Dependency),
            E(InitialMpMapAssetRoot.TargetZone, "$.targetZone.assetRows[].dependencies[]", MapRelationshipEndpointRole.Authority)),
        Relationship(
            InitialMpMapRelationship.ZoneScriptStringResolution,
            MapRelationshipKind.ScriptStringResolution,
            MapRelationshipIdentityReadiness.OwnershipContractLocked,
            MapCompilerMilestone.M7GreenfieldLink,
            MapRelationshipGenerationReadiness.ExistingReferenceLinkingReady,
            "Semantic script-string text is collected once, assigned one " +
            "deterministic ushort zone-table index, and written to each typed " +
            "use. Imported numeric indices are not stable identity.",
            E(InitialMpMapAssetRoot.GameMapMp, "$.definition.glassData.glassNames[].name", MapRelationshipEndpointRole.Selector),
            E(InitialMpMapAssetRoot.TargetZone, "$.targetZone.scriptStrings[]", MapRelationshipEndpointRole.Authority))
    ]);

    private static MapCrossAssetRelationshipContract Relationship(
        InitialMpMapRelationship relationship,
        MapRelationshipKind kind,
        MapRelationshipIdentityReadiness identityReadiness,
        MapCompilerMilestone generationMilestone,
        MapRelationshipGenerationReadiness generationReadiness,
        string contract,
        params MapRelationshipEndpoint[] endpoints) =>
        new(
            relationship,
            kind,
            endpoints,
            identityReadiness,
            generationMilestone,
            generationReadiness,
            contract);

    private static MapRelationshipEndpoint E(
        InitialMpMapAssetRoot root,
        string path,
        MapRelationshipEndpointRole role) =>
        new(root, path, role);
}
