using IW4.FastFiles.Zone;

namespace IW4.Studio.MapEditor.Compilation.Ownership;

/// <summary>
/// Closed set of roots required by the initial multiplayer map compiler.
/// Add-on MapEnts and single-player-only roots are intentionally outside this
/// contract.
/// </summary>
public enum InitialMpMapAssetRoot
{
    GfxMap = 0,
    ColMapMp = 1,
    ComMap = 2,
    MapEnts = 3,
    FxMap = 4,
    GameMapMp = 5,
    TargetZone = 6
}

/// <summary>
/// Exhaustive serialized field-family identities for the current initial-MP
/// asset models and loader graph. One enum value must have exactly one
/// ownership contract.
/// </summary>
public enum InitialMpMapSerializedFamily
{
    GfxMap_Identity = 0,
    GfxMap_RenderSortKeys,
    GfxMap_Skies,
    GfxMap_SkyStartSurfaces,
    GfxMap_SkyImageReferences,
    GfxMap_DpvsPlanes,
    GfxMap_DpvsPlaneNodes,
    GfxMap_DpvsSceneEntityCellBits,
    GfxMap_CellTreeCounts,
    GfxMap_CellAabbTrees,
    GfxMap_CellAabbStaticModelMemberships,
    GfxMap_Cells,
    GfxMap_CellPortals,
    GfxMap_PortalRuntimeLinks,
    GfxMap_PortalVertices,
    GfxMap_CellReflectionProbeMemberships,
    GfxMap_ReflectionProbeImageReferences,
    GfxMap_ReflectionProbeOrigins,
    GfxMap_ReflectionProbeRuntimeTextures,
    GfxMap_LightmapShape,
    GfxMap_LightmapReferences,
    GfxMap_LightmapRuntimeTextures,
    GfxMap_LightmapOverrideReferences,
    GfxMap_PackedWorldVertices,
    GfxMap_WorldVertexRuntimeBuffer,
    GfxMap_PackedVertexLayerData,
    GfxMap_VertexLayerRuntimeBuffer,
    GfxMap_WorldIndices,
    GfxMap_WorldIndexRuntimeBuffer,
    GfxMap_LightGridHeader,
    GfxMap_LightGridRowDataStart,
    GfxMap_LightGridRawRowData,
    GfxMap_LightGridEntries,
    GfxMap_LightGridColors,
    GfxMap_BrushModelLocalBoundsAndSurfaceRange,
    GfxMap_BrushModelRuntimeBounds,
    GfxMap_WorldBounds,
    GfxMap_PrimaryChecksum,
    GfxMap_MaterialMemoryRows,
    GfxMap_MaterialMemoryReferences,
    GfxMap_SunParameters,
    GfxMap_SunMaterialReferences,
    GfxMap_OutdoorLookupMatrix,
    GfxMap_OutdoorImageReference,
    GfxMap_RuntimeCasterAndDynamicSceneCaches,
    GfxMap_ShadowGeometry,
    GfxMap_ShadowGeometryMemberships,
    GfxMap_LightRegions,
    GfxMap_LightRegionHulls,
    GfxMap_LightRegionAxes,
    GfxMap_DpvsStaticHeader,
    GfxMap_DpvsStaticUsageCount,
    GfxMap_DpvsStaticVisibilityCaches,
    GfxMap_SortedSurfaceIndices,
    GfxMap_StaticModelInstances,
    GfxMap_Surfaces,
    GfxMap_SurfaceMaterialReferences,
    GfxMap_SurfaceBounds,
    GfxMap_StaticModelDrawInstances,
    GfxMap_StaticModelReferences,
    GfxMap_RuntimeSurfaceMetadataCaches,
    GfxMap_DpvsDynamicHeader,
    GfxMap_DpvsDynamicCaches,
    GfxMap_MapVertexChecksum,
    GfxMap_HeroOnlyLights,
    GfxMap_FogTypesAllowed,
    GfxMap_TailPadding,
    GfxMap_UmbraGateShape,
    GfxMap_UmbraGateRuntimePayloads,

    ColMapMp_Identity,
    ColMapMp_InUseState,
    ColMapMp_Planes,
    ColMapMp_StaticModelPlacements,
    ColMapMp_StaticModelReferences,
    ColMapMp_Materials,
    ColMapMp_BrushSides,
    ColMapMp_BrushSidePlaneReferences,
    ColMapMp_BrushEdges,
    ColMapMp_BspNodes,
    ColMapMp_BspNodePlaneReferences,
    ColMapMp_Leaves,
    ColMapMp_LeafBrushNodes,
    ColMapMp_LeafBrushReferences,
    ColMapMp_LeafSurfaceReferences,
    ColMapMp_TriangleVertices,
    ColMapMp_TriangleIndices,
    ColMapMp_TriangleWalkability,
    ColMapMp_Borders,
    ColMapMp_Partitions,
    ColMapMp_PartitionBorders,
    ColMapMp_AabbTrees,
    ColMapMp_InlineModels,
    ColMapMp_Brushes,
    ColMapMp_BrushNestedSides,
    ColMapMp_BrushAdjacency,
    ColMapMp_BrushBounds,
    ColMapMp_BrushContents,
    ColMapMp_MapEntsReference,
    ColMapMp_StaticModelAabbNodes,
    ColMapMp_DynamicEntityShape,
    ColMapMp_DynamicEntityDefinitions,
    ColMapMp_DynamicEntityReferences,
    ColMapMp_DynamicEntityPoseCache,
    ColMapMp_DynamicEntityClientCache,
    ColMapMp_DynamicEntityCollisionCache,
    ColMapMp_PrimaryChecksum,
    ColMapMp_RootPadding,

    ComMap_Identity,
    ComMap_InUseState,
    ComMap_PrimaryLights,
    ComMap_PrimaryLightDefinitionNames,

    MapEnts_Identity,
    MapEnts_EntityString,
    MapEnts_BrushModelOrdinals,
    MapEnts_TriggerShape,
    MapEnts_TriggerModels,
    MapEnts_TriggerHulls,
    MapEnts_TriggerSlabs,
    MapEnts_Stages,
    MapEnts_StageNames,
    MapEnts_RootPadding,

    FxMap_Identity,
    FxMap_GlassSourceShape,
    FxMap_GlassRuntimeStateHeader,
    FxMap_GlassDefinitions,
    FxMap_GlassDefinitionReferences,
    FxMap_RuntimePiecePlaces,
    FxMap_RuntimePieceStates,
    FxMap_RuntimePieceDynamics,
    FxMap_RuntimeGeometry,
    FxMap_RuntimeInUseBits,
    FxMap_RuntimeCellBits,
    FxMap_RuntimeVisibility,
    FxMap_RuntimeLinkOrigins,
    FxMap_RuntimeHalfThickness,
    FxMap_LightingHandles,
    FxMap_InitialPieceStates,
    FxMap_InitialGeometry,

    GameMapMp_Identity,
    GameMapMp_GlassPresenceAndShape,
    GameMapMp_GlassPieces,
    GameMapMp_GlassDamageThresholds,
    GameMapMp_GlassNames,
    GameMapMp_GlassNameStrings,
    GameMapMp_GlassNameScriptStrings,
    GameMapMp_GlassPieceMemberships,
    GameMapMp_GlassPadding,

    TargetZone_AssetRowIdentities,
    TargetZone_AssetRowOrderAndHeaderForms,
    TargetZone_MapRootBodies,
    TargetZone_DependencyEdges,
    TargetZone_NestedAssetDefinitions,
    TargetZone_XStrings,
    TargetZone_ScriptStrings,
    TargetZone_BlockAllocations,
    TargetZone_PointerAndAliasCells,
    TargetZone_ExternalHeaderAndBlockSizes,
    TargetZone_EncodedPayload,
    TargetZone_MaterialImageTechniqueDependencies,
    TargetZone_ModelAndSurfaceDependencies,
    TargetZone_LightDefinitionDependencies,
    TargetZone_EffectAndPhysicsDependencies,
    TargetZone_ScriptVisionAudioDependencies
}

public enum MapFieldOwnership
{
    EditorSource = 0,
    CompilerSerialized = 1,
    RuntimeDerived = 2,
    LinkerReference = 3,
    ImportedPreservation = 4
}

/// <summary>
/// Authority that supplies the serialized cell or payload at the current
/// milestone. The named compiler subsystem is recorded separately.
/// </summary>
public enum MapSerializedOwner
{
    CompilerSubsystem = 0,
    TargetZoneLinker = 1,
    RuntimeInitializationContract = 2,
    ImportedBaseline = 3
}

public enum MapCompilerSubsystem
{
    MapIdentity = 0,
    RenderWorld = 1,
    VisibilityAndSpatial = 2,
    Collision = 3,
    EntityAndGameplay = 4,
    LightingAndEnvironment = 5,
    Glass = 6,
    CrossAssetLinker = 7,
    DependencyAndResource = 8,
    TargetZoneLinker = 9
}

public enum MapRuntimeSubsystem
{
    None = 0,
    AssetDatabase = 1,
    Renderer = 2,
    RendererVisibility = 3,
    Collision = 4,
    CommonWorld = 5,
    GameEntitySystem = 6,
    EffectsGlass = 7,
    GameplayGlass = 8,
    MultipleMapConsumers = 9
}

public enum MapCompilerMilestone
{
    M0OwnershipLock = 0,
    M3StructuralGeometry = 3,
    M4Visibility = 4,
    M5LightingEnvironment = 5,
    M6GlassAndGameplay = 6,
    M7GreenfieldLink = 7,
    M8ProductionHardening = 8
}

public enum MapOwnershipReadiness
{
    OwnershipLocked = 0,
    SourceProjectionReady = 1,
    CompilerAlgorithmPending = 2,
    RuntimeDerivationProven = 3,
    ExistingReferenceLinkingReady = 4,
    CrossAssetClosurePending = 5,
    ReverseEngineeringPending = 6,
    ImportedPreservationOnly = 7
}

/// <summary>
/// One field-family ownership decision. Paths describe detached build data
/// using <c>$.definition</c> for an asset body and <c>$.targetZone</c> for
/// linker-owned output.
/// </summary>
public sealed class MapCompilerOwnershipFamilyContract
{
    public MapCompilerOwnershipFamilyContract(
        InitialMpMapSerializedFamily family,
        InitialMpMapAssetRoot root,
        string serializedPath,
        MapFieldOwnership ownership,
        MapSerializedOwner serializedOwner,
        MapCompilerSubsystem compilerOwner,
        MapRuntimeSubsystem runtimeOwner,
        MapCompilerMilestone milestone,
        MapOwnershipReadiness readiness,
        string boundary)
    {
        if (!Enum.IsDefined(family))
            throw new ArgumentOutOfRangeException(nameof(family));
        if (!Enum.IsDefined(root))
            throw new ArgumentOutOfRangeException(nameof(root));
        if (!Enum.IsDefined(ownership))
            throw new ArgumentOutOfRangeException(nameof(ownership));
        if (!Enum.IsDefined(serializedOwner))
            throw new ArgumentOutOfRangeException(nameof(serializedOwner));
        if (!Enum.IsDefined(compilerOwner))
            throw new ArgumentOutOfRangeException(nameof(compilerOwner));
        if (!Enum.IsDefined(runtimeOwner))
            throw new ArgumentOutOfRangeException(nameof(runtimeOwner));
        if (!Enum.IsDefined(milestone))
            throw new ArgumentOutOfRangeException(nameof(milestone));
        if (!Enum.IsDefined(readiness))
            throw new ArgumentOutOfRangeException(nameof(readiness));
        ArgumentException.ThrowIfNullOrWhiteSpace(serializedPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(boundary);

        string expectedPrefix = root == InitialMpMapAssetRoot.TargetZone
            ? "$.targetZone"
            : "$.definition";
        if (!serializedPath.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"{root} paths must begin with '{expectedPrefix}'.",
                nameof(serializedPath));
        }

        string familyPrefix = root + "_";
        if (!family.ToString().StartsWith(familyPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Family {family} does not belong to root {root}.",
                nameof(family));
        }

        ValidateOwnership(
            ownership,
            serializedOwner,
            runtimeOwner,
            readiness);

        Family = family;
        Root = root;
        SerializedPath = serializedPath;
        Ownership = ownership;
        SerializedOwner = serializedOwner;
        CompilerOwner = compilerOwner;
        RuntimeOwner = runtimeOwner;
        Milestone = milestone;
        Readiness = readiness;
        Boundary = boundary;
    }

    public InitialMpMapSerializedFamily Family { get; }
    public InitialMpMapAssetRoot Root { get; }
    public string SerializedPath { get; }
    public MapFieldOwnership Ownership { get; }
    public MapSerializedOwner SerializedOwner { get; }
    public MapCompilerSubsystem CompilerOwner { get; }
    public MapRuntimeSubsystem RuntimeOwner { get; }
    public MapCompilerMilestone Milestone { get; }
    public MapOwnershipReadiness Readiness { get; }
    public string Boundary { get; }

    private static void ValidateOwnership(
        MapFieldOwnership ownership,
        MapSerializedOwner serializedOwner,
        MapRuntimeSubsystem runtimeOwner,
        MapOwnershipReadiness readiness)
    {
        if (ownership == MapFieldOwnership.RuntimeDerived)
        {
            if (serializedOwner !=
                    MapSerializedOwner.RuntimeInitializationContract ||
                runtimeOwner == MapRuntimeSubsystem.None ||
                readiness != MapOwnershipReadiness.RuntimeDerivationProven)
            {
                throw new ArgumentException(
                    "Runtime-derived families require a named runtime owner, " +
                    "a serialized runtime-initialization contract, and proven " +
                    "derivation.");
            }
        }
        else if (serializedOwner ==
                 MapSerializedOwner.RuntimeInitializationContract)
        {
            throw new ArgumentException(
                "Only runtime-derived families may use runtime-initialization ownership.");
        }

        if (ownership == MapFieldOwnership.LinkerReference &&
            serializedOwner != MapSerializedOwner.TargetZoneLinker)
        {
            throw new ArgumentException(
                "Linker references must be serialized by the target-zone linker.");
        }
        if (ownership != MapFieldOwnership.LinkerReference &&
            serializedOwner == MapSerializedOwner.TargetZoneLinker)
        {
            throw new ArgumentException(
                "Target-zone linker ownership is reserved for linker references.");
        }

        if (ownership == MapFieldOwnership.ImportedPreservation)
        {
            if (serializedOwner != MapSerializedOwner.ImportedBaseline ||
                readiness is not (
                    MapOwnershipReadiness.ImportedPreservationOnly or
                    MapOwnershipReadiness.ReverseEngineeringPending))
            {
                throw new ArgumentException(
                    "Imported-preservation families require imported-baseline " +
                    "ownership and an explicitly unresolved readiness.");
            }
        }
        else if (serializedOwner == MapSerializedOwner.ImportedBaseline)
        {
            throw new ArgumentException(
                "Only imported-preservation families may use imported-baseline ownership.");
        }
    }
}

public sealed class MapCompilerAssetRootContract
{
    private readonly IReadOnlyList<MapCompilerOwnershipFamilyContract> _families;

    public MapCompilerAssetRootContract(
        InitialMpMapAssetRoot root,
        XAssetType? serializedType,
        string rootPath,
        IEnumerable<MapCompilerOwnershipFamilyContract> families)
    {
        if (!Enum.IsDefined(root))
            throw new ArgumentOutOfRangeException(nameof(root));
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(families);

        if ((root == InitialMpMapAssetRoot.TargetZone) !=
            (serializedType is null))
        {
            throw new ArgumentException(
                "Only the target-zone pseudo-root omits an XAssetType.",
                nameof(serializedType));
        }

        XAssetType? expectedType = root switch
        {
            InitialMpMapAssetRoot.GfxMap => XAssetType.GfxMap,
            InitialMpMapAssetRoot.ColMapMp => XAssetType.ColMapMp,
            InitialMpMapAssetRoot.ComMap => XAssetType.ComMap,
            InitialMpMapAssetRoot.MapEnts => XAssetType.MapEnts,
            InitialMpMapAssetRoot.FxMap => XAssetType.FxMap,
            InitialMpMapAssetRoot.GameMapMp => XAssetType.GameMapMp,
            InitialMpMapAssetRoot.TargetZone => null,
            _ => throw new ArgumentOutOfRangeException(nameof(root))
        };
        if (serializedType != expectedType)
        {
            throw new ArgumentException(
                $"{root} requires serialized type {expectedType}.",
                nameof(serializedType));
        }

        string expectedRootPath = root == InitialMpMapAssetRoot.TargetZone
            ? "$.targetZone"
            : "$.definition";
        if (!string.Equals(
                rootPath,
                expectedRootPath,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"{root} requires root path '{expectedRootPath}'.",
                nameof(rootPath));
        }

        MapCompilerOwnershipFamilyContract[] copy = families.ToArray();
        if (copy.Length == 0)
            throw new ArgumentException("An ownership root cannot be empty.", nameof(families));
        if (copy.Any(value => value.Root != root))
        {
            throw new ArgumentException(
                $"Every family in the {root} contract must declare the same root.",
                nameof(families));
        }
        if (copy.Select(value => value.Family).Distinct().Count() != copy.Length)
        {
            throw new ArgumentException(
                $"{root} contains a duplicate field-family identity.",
                nameof(families));
        }
        if (copy.Select(value => value.SerializedPath)
            .Distinct(StringComparer.Ordinal).Count() != copy.Length)
        {
            throw new ArgumentException(
                $"{root} contains a duplicate serialized family path.",
                nameof(families));
        }

        Root = root;
        SerializedType = serializedType;
        RootPath = rootPath;
        _families = Array.AsReadOnly(copy);
    }

    public InitialMpMapAssetRoot Root { get; }
    public XAssetType? SerializedType { get; }
    public string RootPath { get; }
    public IReadOnlyList<MapCompilerOwnershipFamilyContract> Families =>
        _families;
}

/// <summary>
/// Validated closed catalog. Construction fails if any required root or
/// serialized family is absent, duplicated, or assigned to the wrong root.
/// </summary>
public sealed class MapCompilerOwnershipCatalog
{
    private readonly IReadOnlyList<MapCompilerAssetRootContract> _roots;
    private readonly IReadOnlyDictionary<
        InitialMpMapAssetRoot,
        MapCompilerAssetRootContract> _byRoot;
    private readonly IReadOnlyDictionary<
        InitialMpMapSerializedFamily,
        MapCompilerOwnershipFamilyContract> _byFamily;

    public MapCompilerOwnershipCatalog(
        IEnumerable<MapCompilerAssetRootContract> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        MapCompilerAssetRootContract[] rootCopy = roots.ToArray();
        InitialMpMapAssetRoot[] requiredRoots =
            Enum.GetValues<InitialMpMapAssetRoot>();
        InitialMpMapSerializedFamily[] requiredFamilies =
            Enum.GetValues<InitialMpMapSerializedFamily>();

        if (rootCopy.Select(value => value.Root).Distinct().Count() !=
            rootCopy.Length)
        {
            throw new InvalidDataException(
                "The initial-MP ownership catalog contains a duplicate root.");
        }

        InitialMpMapAssetRoot[] missingRoots = requiredRoots
            .Except(rootCopy.Select(value => value.Root))
            .ToArray();
        InitialMpMapAssetRoot[] extraRoots = rootCopy
            .Select(value => value.Root)
            .Except(requiredRoots)
            .ToArray();
        if (missingRoots.Length != 0 || extraRoots.Length != 0)
        {
            throw new InvalidDataException(
                "The initial-MP ownership catalog root set is not closed. " +
                $"Missing=[{string.Join(", ", missingRoots)}], " +
                $"extra=[{string.Join(", ", extraRoots)}].");
        }

        MapCompilerOwnershipFamilyContract[] familyCopy = rootCopy
            .SelectMany(value => value.Families)
            .ToArray();
        if (familyCopy.Select(value => value.Family).Distinct().Count() !=
            familyCopy.Length)
        {
            throw new InvalidDataException(
                "The initial-MP ownership catalog contains a duplicate family.");
        }

        InitialMpMapSerializedFamily[] missingFamilies = requiredFamilies
            .Except(familyCopy.Select(value => value.Family))
            .ToArray();
        InitialMpMapSerializedFamily[] extraFamilies = familyCopy
            .Select(value => value.Family)
            .Except(requiredFamilies)
            .ToArray();
        if (missingFamilies.Length != 0 || extraFamilies.Length != 0)
        {
            throw new InvalidDataException(
                "The initial-MP ownership catalog family set is not closed. " +
                $"Missing=[{string.Join(", ", missingFamilies)}], " +
                $"extra=[{string.Join(", ", extraFamilies)}].");
        }

        XAssetType[] duplicateTypes = rootCopy
            .Where(value => value.SerializedType.HasValue)
            .GroupBy(value => value.SerializedType!.Value)
            .Where(group => group.Count() != 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateTypes.Length != 0)
        {
            throw new InvalidDataException(
                "The initial-MP ownership catalog assigns an XAssetType to " +
                $"multiple roots: {string.Join(", ", duplicateTypes)}.");
        }

        _roots = Array.AsReadOnly(rootCopy);
        _byRoot = new System.Collections.ObjectModel.ReadOnlyDictionary<
            InitialMpMapAssetRoot,
            MapCompilerAssetRootContract>(
            rootCopy.ToDictionary(value => value.Root));
        _byFamily = new System.Collections.ObjectModel.ReadOnlyDictionary<
            InitialMpMapSerializedFamily,
            MapCompilerOwnershipFamilyContract>(
            familyCopy.ToDictionary(value => value.Family));
    }

    public IReadOnlyList<MapCompilerAssetRootContract> Roots => _roots;
    public int RequiredRootCount => Enum.GetValues<InitialMpMapAssetRoot>().Length;
    public int RequiredFamilyCount =>
        Enum.GetValues<InitialMpMapSerializedFamily>().Length;

    public MapCompilerAssetRootContract RequireRoot(
        InitialMpMapAssetRoot root) =>
        _byRoot.TryGetValue(root, out MapCompilerAssetRootContract? contract)
            ? contract
            : throw new KeyNotFoundException(
                $"No initial-MP ownership root is registered for {root}.");

    public MapCompilerOwnershipFamilyContract RequireFamily(
        InitialMpMapSerializedFamily family) =>
        _byFamily.TryGetValue(
            family,
            out MapCompilerOwnershipFamilyContract? contract)
            ? contract
            : throw new KeyNotFoundException(
                $"No initial-MP ownership family is registered for {family}.");
}
