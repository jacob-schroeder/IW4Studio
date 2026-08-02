using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Math;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Zone;
using IW4.Studio.MapEditor.Compilation.RenderWorld;
using IW4.Studio.MapEditor.Compilation.RenderWorld.Visibility;
using IW4.Studio.MapEditor.Editing.Objects;

namespace IW4.Studio.MapEditor.Compilation.TargetAcceptance;

/// <summary>
/// Projects the bounded M3/M4 artifacts into a complete serializer-shaped
/// GfxWorld graph. The projection policy keeps the original serializer-only
/// placeholders distinct from target-runtime no-bake defaults; neither form is
/// a general lighting compiler or persistence authority.
/// </summary>
public static class GfxWorldTargetAcceptanceAssembler
{
    public static GfxWorldTargetAcceptanceCandidate Compile(
        RenderWorldVisibilityCandidate visibilityCandidate,
        MapPrimaryChecksumAssignment primaryChecksumAssignment)
    {
        ArgumentNullException.ThrowIfNull(visibilityCandidate);
        ArgumentNullException.ThrowIfNull(primaryChecksumAssignment);

        RenderWorldVisibilityAssessment visibilityAssessment =
            RenderWorldVisibilityValidator.Assess(visibilityCandidate);
        if (!visibilityAssessment.IsValid)
        {
            throw new GfxWorldTargetAcceptanceCompilationException(
                new GfxWorldTargetAcceptanceAssessment(
                    visibilityAssessment.Issues.Select(value =>
                        new GfxWorldTargetAcceptanceIssue(
                            GfxWorldTargetAcceptanceIssueKind
                                .SourceCandidateInvalid,
                            $"visibility.{value.Path}",
                            value.Detail))));
        }

        GfxWorldAsset definition =
            ProjectDefinition(
                visibilityCandidate,
                primaryChecksumAssignment,
                GfxWorldTargetAcceptanceProjectionPolicy
                    .SerializerOnlyUnclassified);
        GfxWorldReferenceBuildData references =
            ProjectReferences(
                visibilityCandidate.RenderCandidate,
                GfxWorldTargetAcceptanceProjectionPolicy
                    .SerializerOnlyUnclassified);
        GfxWorldTargetAcceptanceBlocker[] blockers =
            CreateDeferredBlockers();
        GfxWorldTargetAcceptanceAssessment assessment =
            GfxWorldTargetAcceptanceValidator.Assess(
                visibilityCandidate,
                primaryChecksumAssignment,
                definition,
                references,
                blockers);
        if (!assessment.IsValid)
        {
            throw new GfxWorldTargetAcceptanceCompilationException(
                assessment);
        }

        return new GfxWorldTargetAcceptanceCandidate(
            visibilityCandidate,
            primaryChecksumAssignment,
            definition,
            references,
            blockers,
            assessment);
    }

    internal static GfxWorldAsset ProjectDefinition(
        RenderWorldVisibilityCandidate visibility,
        MapPrimaryChecksumAssignment checksumAssignment,
        GfxWorldTargetAcceptanceProjectionPolicy projectionPolicy)
    {
        ArgumentNullException.ThrowIfNull(projectionPolicy);
        RenderWorldStructuralCandidate render =
            visibility.RenderCandidate;
        RenderWorldCompiledGeometry geometry = render.Geometry;
        int surfaceCount = render.Surfaces.Count;
        int surfaceVisibilityWordCount =
            visibility.RuntimeAllocationShape
                .SurfaceVisibilityWordCount;
        bool targetRuntimeNoBake =
            projectionPolicy.LightingContract ==
                GfxWorldLightingProjectionContract
                    .TargetRuntimeNoBake;

        GfxSurfaceBounds[] surfaceBounds =
            new GfxSurfaceBounds[surfaceCount];
        foreach (RenderWorldVisibilitySurfaceMembership membership in
                 visibility.SurfaceMemberships)
        {
            surfaceBounds[membership.SurfaceOrdinal] =
                new GfxSurfaceBounds
                {
                    Bounds = ProjectBounds(membership.Bounds),
                    Unknown18To1F =
                        new byte[
                            GfxWorldTargetAcceptanceProfile
                                .SurfaceBoundsTailByteCount]
                };
        }

        return new GfxWorldAsset
        {
            Name = visibility.MapAssetName,
            BaseName = DeriveBaseName(visibility.MapAssetName),
            PlaneCount = visibility.CellPartitionPlanes.Count,
            NodeCount = visibility.PackedCellPartitionNodes.Count,
            SurfaceCount = surfaceCount,
            SkyCount = 0,
            Skies = [],
            SunPrimaryLightIndex =
                projectionPolicy.SunPrimaryLightIndex,
            PrimaryLightCount = projectionPolicy.PrimaryLightCount,
            SortKeyLitDecal = 0,
            SortKeyEffectDecal = 0,
            SortKeyEffectAuto = 0,
            SortKeyDistortion = 0,
            DpvsPlanes = new GfxWorldDpvsPlanes
            {
                CellCount = visibility.Cells.Count,
                Planes = [],
                Nodes =
                    visibility.PackedCellPartitionNodes.ToArray(),
                SceneEntCellBits =
                    new uint[
                        visibility.RuntimeAllocationShape
                            .SceneEntityCellBitWordCount]
            },
            CellTreeCounts = visibility.Cells
                .Select(cell =>
                    new GfxCellTreeCount(
                        cell.Tree.DeclaredAabbTreeCount))
                .ToArray(),
            CellTrees = visibility.Cells
                .Select(cell => new GfxCellTree
                {
                    AabbTrees = cell.Tree.AabbTrees
                        .Select(ProjectAabbTree)
                        .ToArray()
                })
                .ToArray(),
            Cells = visibility.Cells
                .Select(cell => new GfxCell
                {
                    Bounds = ProjectBounds(cell.Bounds),
                    PortalCount = cell.PortalCount,
                    Portals = [],
                    ReflectionProbeCount =
                        targetRuntimeNoBake
                            ? (byte)1
                            : (byte)0,
                    Pad21 = [0, 0, 0],
                    ReflectionProbes =
                        targetRuntimeNoBake
                            ? [GfxWorldNoBakeRuntimeDefaults
                                .ReflectionProbeIndex]
                            : []
                })
                .ToArray(),
            WorldDraw = new GfxWorldDraw
            {
                ReflectionProbeCount =
                    targetRuntimeNoBake ? 1u : 0u,
                ReflectionProbeImages =
                    targetRuntimeNoBake ? [null] : [],
                ReflectionProbeOrigins =
                    targetRuntimeNoBake
                        ? [new GfxReflectionProbe(0, 0, 0)]
                        : [],
                ReflectionProbeTextures = [],
                LightmapCount = 0,
                Lightmaps = [],
                LightmapPrimaryTextures = [],
                LightmapSecondaryTextures = [],
                VertexCount = checked((uint)geometry.VertexCount),
                VertexData = new GfxWorldVertexData
                {
                    PackedVertices =
                        geometry.PackedPositionData.ToArray(),
                    WorldVbHandle = 0,
                    WorldVbOffset = 0
                },
                VertexLayerDataSize = checked(
                    (uint)geometry.PackedVertexLayerData.Count),
                VertexLayerData = new GfxWorldVertexLayerData
                {
                    PackedLayerData =
                        geometry.PackedVertexLayerData.ToArray(),
                    LayerVbHandle = 0,
                    LayerVbOffset = 0
                },
                IndexCount = geometry.Indices.Count,
                Indices = geometry.Indices.ToArray(),
                IndexBufferRaw = 0
            },
            LightGrid =
                targetRuntimeNoBake
                    ? GfxWorldNoBakeRuntimeDefaults
                        .CreateEmptyLightGrid()
                    : CreateSerializerOnlyLightGrid(),
            ModelCount = 1,
            Models = [ProjectWorldModel(visibility)],
            Mins =
            [
                visibility.WorldBounds.MidPoint.X,
                visibility.WorldBounds.MidPoint.Y,
                visibility.WorldBounds.MidPoint.Z
            ],
            Maxs =
            [
                visibility.WorldBounds.HalfSize.X,
                visibility.WorldBounds.HalfSize.Y,
                visibility.WorldBounds.HalfSize.Z
            ],
            Checksum = checksumAssignment.Checksum.Value,
            MaterialMemoryCount = 0,
            MaterialMemory = [],
            Sun = new Sunflare
            {
                HasValidData = 0,
                SunFxPosition = [0, 0, 0]
            },
            OutdoorLookupMatrix = new float[16],
            CellCasterBits = [],
            CellCasterBits2 = [],
            SceneDynModels = [],
            SceneDynBrushes = [],
            PrimaryLightEntityShadowVis = [],
            PrimaryLightDynEntShadowVis0 = [],
            PrimaryLightDynEntShadowVis1 = [],
            PrimaryLightForModelDynEnt = [],
            ShadowGeom = Enumerable
                .Range(0, projectionPolicy.PrimaryLightCount)
                .Select(_ => new GfxShadowGeometry())
                .ToArray(),
            LightRegions = Enumerable
                .Range(0, projectionPolicy.PrimaryLightCount)
                .Select(_ => new GfxLightRegion())
                .ToArray(),
            Dpvs = new GfxWorldDpvsStatic
            {
                SModelCount =
                    visibility.StaticDpvsShape.StaticModelCount,
                StaticSurfaceCount =
                    visibility.StaticDpvsShape.StaticSurfaceCount,
                LitSurfsBegin =
                    projectionPolicy.LitSurfacesBegin,
                LitSurfsEnd =
                    projectionPolicy.LitSurfacesEnd,
                VisibilityCounts =
                [
                    .. projectionPolicy.SurfaceCategoryEndpoints,
                    0,
                    checked((uint)surfaceVisibilityWordCount)
                ],
                SModelVisData = [],
                SurfaceVisData = [],
                SortedSurfIndex =
                    visibility.SortedWorldSurfaceOrdinals.ToArray(),
                SModelInsts = [],
                Surfaces = render.Surfaces
                    .Select(surface =>
                        ProjectSurface(
                            surface,
                            targetRuntimeNoBake))
                    .ToArray(),
                SurfaceBounds = surfaceBounds,
                SModelDrawInsts = [],
                SurfaceMaterials = [],
                SurfaceCastsSunShadow = [],
                UsageCount = 0
            },
            DpvsDyn = new GfxWorldDpvsDynamic
            {
                DynEntClientWordCount = [0, 0],
                DynEntClientCount = [0, 0],
                DynEntCellBits = [],
                DynEntVisData = []
            },
            MapVertexChecksum = render.MapVertexChecksum,
            HeroOnlyLightCount = 0,
            HeroOnlyLights = [],
            FogTypesAllowed = projectionPolicy.FogTypesAllowed,
            Pad279To27B = [0, 0, 0],
            UmbraGateCount = 0,
            UmbraGateData = [],
            UmbraGateData2 = []
        };
    }

    internal static GfxWorldReferenceBuildData ProjectReferences(
        RenderWorldStructuralCandidate render,
        GfxWorldTargetAcceptanceProjectionPolicy projectionPolicy)
    {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(projectionPolicy);
        bool targetRuntimeNoBake =
            projectionPolicy.LightingContract ==
                GfxWorldLightingProjectionContract
                    .TargetRuntimeNoBake;

        return new()
        {
            ReflectionProbeImages =
                targetRuntimeNoBake
                    ? [GfxWorldNoBakeRuntimeDefaults
                        .CreateReflectionProbeReference()]
                    : [],
            ReflectionProbeImageDefinitions =
                targetRuntimeNoBake
                    ? [GfxWorldNoBakeRuntimeDefaults
                        .CreateReflectionProbeDefinition()]
                    : [],
            SurfaceMaterials = render.Surfaces
                .Select(surface =>
                    new SymbolicXAssetReference(
                        XAssetType.Material,
                        ExternalName(surface.SymbolicMaterialName)))
                .ToArray()
        };
    }

    private static GfxSurface ProjectSurface(
        RenderWorldCompiledSurface surface,
        bool targetRuntimeNoBake) =>
        new()
        {
            Triangles = new SrfTriangles
            {
                VertexLayerData = surface.VertexLayerData,
                BaseVertex = surface.BaseVertex,
                MinVertexIndex = surface.MinVertexIndex,
                VertexCount = checked((ushort)surface.VertexCount),
                TriCount = checked((ushort)surface.TriangleCount),
                BaseIndex = surface.BaseIndex
            },
            Material = null,
            MaterialIncomingDefinition = null,
            LightmapIndex =
                targetRuntimeNoBake
                    ? GfxWorldNoBakeRuntimeDefaults
                        .NoLightmapSurfaceIndex
                    : (byte)0,
            ReflectionProbeIndex =
                GfxWorldNoBakeRuntimeDefaults
                    .ReflectionProbeIndex,
            PrimaryLightIndex = 0,
            CastsSunShadow = 0
        };

    private static GfxBrushModel ProjectWorldModel(
        RenderWorldVisibilityCandidate visibility)
    {
        RenderWorldWorldModelSurfaceRange source =
            visibility.RenderCandidate.WorldModel;
        (MapVector3 writableMinimum, MapVector3 writableMaximum) =
            Endpoints(visibility.WorldBounds);
        return new GfxBrushModel
        {
            WritableMins =
            [
                writableMinimum.X,
                writableMinimum.Y,
                writableMinimum.Z
            ],
            WritableMaxs =
            [
                writableMaximum.X,
                writableMaximum.Y,
                writableMaximum.Z
            ],
            BoundsMins =
            [
                source.BoundsMinimum.X,
                source.BoundsMinimum.Y,
                source.BoundsMinimum.Z
            ],
            BoundsMaxs =
            [
                source.BoundsMaximum.X,
                source.BoundsMaximum.Y,
                source.BoundsMaximum.Z
            ],
            Radius = source.LocalOriginRadius,
            SurfaceCount = source.SurfaceCount,
            StartSurfIndex = source.StartSurfIndex
        };
    }

    private static GfxAabbTree ProjectAabbTree(
        RenderWorldVisibilityAabbLeaf leaf) =>
        new()
        {
            Bounds = ProjectBounds(leaf.Bounds),
            ChildCount = leaf.ChildCount,
            SurfaceCount = leaf.SurfaceCount,
            StartSurfIndex = leaf.StartSurfaceIndex,
            SModelIndexCount = leaf.StaticModelIndexCount,
            SModelIndexes = leaf.StaticModelOrdinals.ToArray(),
            ChildrenOffset = leaf.ChildrenOffset
        };

    private static Bounds ProjectBounds(MapBounds bounds) =>
        new()
        {
            MidPoint = ProjectVector(bounds.MidPoint),
            HalfSize = ProjectVector(bounds.HalfSize)
        };

    private static Vec3 ProjectVector(MapVector3 value) =>
        new() { X = value.X, Y = value.Y, Z = value.Z };

    private static (MapVector3 Minimum, MapVector3 Maximum) Endpoints(
        MapBounds bounds) =>
        (
            new MapVector3(
                bounds.MidPoint.X - bounds.HalfSize.X,
                bounds.MidPoint.Y - bounds.HalfSize.Y,
                bounds.MidPoint.Z - bounds.HalfSize.Z),
            new MapVector3(
                bounds.MidPoint.X + bounds.HalfSize.X,
                bounds.MidPoint.Y + bounds.HalfSize.Y,
                bounds.MidPoint.Z + bounds.HalfSize.Z)
        );

    private static GfxLightGrid CreateSerializerOnlyLightGrid() =>
        new()
        {
            HasLightRegions = 0,
            SunPrimaryLightIndex = 0,
            Mins = [0, 0, 0],
            Maxs = [0, 0, 0],
            RowAxis = 0,
            ColAxis = 1,
            RowDataStart = [0],
            RawRowDataSize = 0,
            RawRowData = [],
            EntryCount = 0,
            Entries = [],
            ColorCount = 0,
            Colors = []
        };

    private static string DeriveBaseName(string mapAssetName)
    {
        const string prefix = "maps/mp/";
        const string suffix = ".d3dbsp";
        return mapAssetName.Substring(
            prefix.Length,
            mapAssetName.Length - prefix.Length - suffix.Length);
    }

    private static string ExternalName(string value) =>
        value.StartsWith(",", StringComparison.Ordinal)
            ? value
            : "," + value;

    private static GfxWorldTargetAcceptanceBlocker[]
        CreateDeferredBlockers() =>
    [
        new(
            GfxWorldTargetAcceptanceDeferredMilestone
                .M4TargetConsumerAcceptance,
            GfxWorldTargetAcceptanceBlockerKind
                .RetailOrEmulatorConsumerAcceptanceNotEstablished,
            "Managed serializer planning does not establish retail or " +
            "emulator initialization, traversal, culling, rendering, " +
            "collision, or limit acceptance."),
        new(
            GfxWorldTargetAcceptanceDeferredMilestone
                .M4TargetConsumerAcceptance,
            GfxWorldTargetAcceptanceBlockerKind
                .ProbeOnlySurfaceBoundsTailPolicyNotTargetAccepted,
            "The opaque eight-byte GfxSurfaceBounds tail is zeroed only for " +
            "the managed probe. Target meaning and acceptance remain open."),
        new(
            GfxWorldTargetAcceptanceDeferredMilestone
                .M5LightingAndEnvironment,
            GfxWorldTargetAcceptanceBlockerKind
                .SurfaceLightingClassificationNotCompiled,
            "The probe writes zero lighting-classification ranges and " +
            "zero per-surface lightmap, probe, primary-light, and shadow " +
            "assignments. M5 must replace them with compiled values."),
        new(
            GfxWorldTargetAcceptanceDeferredMilestone
                .M5LightingAndEnvironment,
            GfxWorldTargetAcceptanceBlockerKind
                .LightingEnvironmentAndBakeOutputsNotCompiled,
            "Skies, reflection probes, lightmaps, light grid, sun, outdoor, " +
            "fog, light regions, shadow metadata, and baked products remain " +
            "M5 outputs."),
        new(
            GfxWorldTargetAcceptanceDeferredMilestone
                .M7AssetResolutionAndPersistence,
            GfxWorldTargetAcceptanceBlockerKind
                .MaterialDependenciesNotResolved,
            "Surface material names remain external symbolic identities. " +
            "M7 must prove dependency existence, ownership, and linker " +
            "provenance."),
        new(
            GfxWorldTargetAcceptanceDeferredMilestone
                .M7AssetResolutionAndPersistence,
            GfxWorldTargetAcceptanceBlockerKind
                .CompleteMapAssetGraphNotAssembled,
            "The GfxWorld probe is not the complete synchronized ColMap, " +
            "ComMap, MapEnts, FxMap, GameMap, and dependency graph."),
        new(
            GfxWorldTargetAcceptanceDeferredMilestone
                .M7AssetResolutionAndPersistence,
            GfxWorldTargetAcceptanceBlockerKind
                .LinkingEmissionAndPersistenceNotAuthorized,
            "No linker registration, asset-pool mutation, Save As, or " +
            "FastFile persistence path is authorized by this candidate.")
    ];
}
