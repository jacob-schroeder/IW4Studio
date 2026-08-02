using System.Collections.ObjectModel;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Math;
using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Studio.Documents;
using IW4.Studio.MapEditor.Compilation.Bundles;
using IW4.Studio.MapEditor.Compilation.StaticModels;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.Entities;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.MapEntsSyntax;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.Editing.Provenance;
using IW4.Studio.MapEditor.Editing.SavePlanning;

namespace IW4.Studio.MapEditor.Compilation.Import;

public sealed class ExistingMapImportResult
{
    internal ExistingMapImportResult(
        CompiledMapBundle bundle,
        EditorMapDocument document,
        IEnumerable<CompiledSourceBinding> sourceBindings,
        MapImportAudit audit,
        StaticModelCorrespondenceCatalog? staticModelCorrespondences = null)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(sourceBindings);
        ArgumentNullException.ThrowIfNull(audit);
        Bundle = bundle;
        Document = document;
        SourceBindings = Array.AsReadOnly(sourceBindings.ToArray());
        Audit = audit;
        StaticModelCorrespondences =
            staticModelCorrespondences ??
            StaticModelCompilationRelationshipResolver.Resolve(
                bundle,
                document);
    }

    public CompiledMapBundle Bundle { get; }
    public EditorMapDocument Document { get; }
    public IReadOnlyList<CompiledSourceBinding> SourceBindings { get; }
    public MapImportAudit Audit { get; }
    public StaticModelCorrespondenceCatalog StaticModelCorrespondences
    {
        get;
    }
}

public interface IExistingMapImporter
{
    ExistingMapImportResult Import(
        CompiledMapBundle bundle,
        CancellationToken cancellationToken = default);
}

public sealed class ExistingMapImporter : IExistingMapImporter
{
    public ExistingMapImportResult Import(
        CompiledMapBundle bundle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        cancellationToken.ThrowIfCancellationRequested();

        var bindings = new SourceBindingCatalogBuilder(bundle);
        var diagnostics = new List<string>();
        var worldSurfaces = new List<EditorWorldSurface>();
        var staticModels = new List<EditorStaticModel>();
        var collision = new List<EditorCollisionObject>();
        var entities = new List<EditorEntity>();
        var lights = new List<EditorPrimaryLight>();
        var glass = new List<EditorGlassObject>();
        var spatial = new List<EditorSpatialObject>();
        var environment = new List<EditorEnvironmentValue>();
        EditorMapEntitySource? entitySource = null;

        if (bundle.TryGetBaseline(
                MapAssetKind.GfxMap,
                out GfxWorldBuildData? gfx) &&
            gfx is not null)
        {
            ImportGfx(
                bundle,
                gfx,
                bindings,
                worldSurfaces,
                staticModels,
                spatial,
                environment,
                diagnostics,
                cancellationToken);
        }

        if (TryGetClip(
                bundle,
                out MapAssetKind clipKind,
                out ClipMapBuildData? clip) &&
            clip is not null)
        {
            ImportClip(
                bundle,
                clipKind,
                clip,
                bindings,
                staticModels,
                collision,
                diagnostics,
                cancellationToken);
        }

        if (bundle.TryGetBaseline(
                MapAssetKind.ComMap,
                out ComWorldBuildData? com) &&
            com is not null)
            ImportCom(
                bundle,
                com,
                bindings,
                lights,
                environment,
                cancellationToken);

        if (TryGetMapEnts(bundle, out MapEntsSource? mapEnts) &&
            mapEnts is not null)
        {
            entitySource = ImportMapEnts(
                bundle,
                mapEnts,
                bindings,
                entities,
                environment,
                diagnostics,
                cancellationToken);
        }

        if (bundle.TryGetBaseline(
                MapAssetKind.FxMap,
                out FxWorldBuildData? fx) &&
            fx is not null)
            ImportFx(
                bundle,
                fx,
                bindings,
                glass,
                environment,
                cancellationToken);

        if (bundle.TryGetBaseline(
                MapAssetKind.GameMapMp,
                out GameWorldMpBuildData? game) &&
            game is not null)
        {
            ImportGame(
                bundle,
                game,
                bindings,
                glass,
                environment,
                cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var document = new EditorMapDocument(
            bundle.DocumentId,
            bundle.MapIdentity,
            new EditorEnvironment(environment),
            entitySource,
            worldSurfaces,
            staticModels,
            collision,
            entities,
            lights,
            glass,
            spatial);
        cancellationToken.ThrowIfCancellationRequested();
        StaticModelCorrespondenceCatalog staticModelCorrespondences =
            StaticModelCompilationRelationshipResolver.Resolve(
                bundle,
                document,
                cancellationToken);
        string[] unresolved = BuildUnresolvedJoinAudit(
            document,
            staticModelCorrespondences);
        var audit = new MapImportAudit(
            document,
            unresolved,
            diagnostics,
            cancellationToken);
        return new ExistingMapImportResult(
            bundle,
            document,
            bindings.Bindings,
            audit,
            staticModelCorrespondences);
    }

    private static void ImportGfx(
        CompiledMapBundle bundle,
        GfxWorldBuildData data,
        SourceBindingCatalogBuilder bindings,
        ICollection<EditorWorldSurface> worldSurfaces,
        ICollection<EditorStaticModel> staticModels,
        ICollection<EditorSpatialObject> spatial,
        ICollection<EditorEnvironmentValue> environment,
        ICollection<string> diagnostics,
        CancellationToken cancellationToken)
    {
        CompiledMapAssetDescriptor asset = bundle.RequireAsset(MapAssetKind.GfxMap);
        GfxWorldAsset world = data.Definition;
        IReadOnlyList<GfxSurface> surfaces =
            world.Dpvs.SerializedSurfaceState?.Surfaces ?? world.Dpvs.Surfaces;
        IReadOnlyList<GfxSurfaceBounds> surfaceBounds =
            world.Dpvs.SerializedSurfaceState?.SurfaceBounds ??
            world.Dpvs.SurfaceBounds;
        if (world.SurfaceCount != surfaces.Count)
        {
            diagnostics.Add(
                $"GfxMap declares {world.SurfaceCount} surfaces but retains {surfaces.Count} serialized surface records.");
        }

        for (int index = 0; index < surfaces.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GfxSurface surface = surfaces[index];
            CompiledSourceBinding recordBinding = bindings.Add(
                asset,
                $"definition.dpvs.surfaces[{index}]",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            CompiledSourceBinding materialBinding = bindings.Add(
                asset,
                $"references.surfaceMaterials[{index}]",
                index,
                MapValueProvenance.ExactSerialized);
            MapBounds? bounds = index < surfaceBounds.Count
                ? Convert(surfaceBounds[index].Bounds)
                : null;
            CompiledSourceBinding boundsBinding = bounds is null
                ? recordBinding
                : bindings.Add(
                    asset,
                    $"definition.dpvs.surfaceBounds[{index}]",
                    index,
                    MapValueProvenance.ExactDecodedRuntime);
            string? materialName = index < data.References.SurfaceMaterials.Count
                ? DisplayName(data.References.SurfaceMaterials[index])
                : null;
            worldSurfaces.Add(new EditorWorldSurface(
                DeterministicMapIdentity.Object(
                    bundle.MapIdentity,
                    asset.SerializedType.ToString(),
                    asset.AssetName,
                    "world-surface",
                    index),
                Value(index, MapValueProvenance.Derived, recordBinding),
                Value(
                    (int)surface.Triangles.VertexCount,
                    MapValueProvenance.ExactDecodedRuntime,
                    recordBinding),
                Value(
                    (int)surface.Triangles.TriCount,
                    MapValueProvenance.ExactDecodedRuntime,
                    recordBinding),
                Value(
                    materialName,
                    materialName is null
                        ? MapValueProvenance.Unknown
                        : MapValueProvenance.ExactSerialized,
                    materialBinding),
                Value(
                    bounds,
                    bounds is null
                        ? MapValueProvenance.Unknown
                        : MapValueProvenance.ExactDecodedRuntime,
                    boundsBinding),
                Value(
                    surface.LightmapIndex,
                    MapValueProvenance.ExactDecodedRuntime,
                    recordBinding),
                Value(
                    surface.ReflectionProbeIndex,
                    MapValueProvenance.ExactDecodedRuntime,
                    recordBinding),
                Value(
                    surface.PrimaryLightIndex,
                    MapValueProvenance.ExactDecodedRuntime,
                    recordBinding)));
        }

        int renderModelCount = Math.Min(
            world.Dpvs.SModelDrawInsts.Count,
            world.Dpvs.SModelInsts.Count);
        if (world.Dpvs.SModelCount != renderModelCount)
        {
            diagnostics.Add(
                $"GfxMap declares {world.Dpvs.SModelCount} static models but retains {world.Dpvs.SModelDrawInsts.Count} draw and {world.Dpvs.SModelInsts.Count} instance records.");
        }
        for (int index = 0; index < renderModelCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GfxStaticModelDrawInst draw = world.Dpvs.SModelDrawInsts[index];
            GfxStaticModelInst instance = world.Dpvs.SModelInsts[index];
            CompiledSourceBinding recordBinding = bindings.Add(
                asset,
                $"definition.dpvs.sModelDrawInsts[{index}]",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            CompiledSourceBinding modelBinding = bindings.Add(
                asset,
                $"references.staticModelDrawInsts[{index}]",
                index,
                MapValueProvenance.ExactSerialized);
            CompiledSourceBinding originBinding = bindings.Add(
                asset,
                $"definition.dpvs.sModelDrawInsts[{index}].placement.origin",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            CompiledSourceBinding scaleBinding = bindings.Add(
                asset,
                $"definition.dpvs.sModelDrawInsts[{index}].placement.scale",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            CompiledSourceBinding cullDistanceBinding = bindings.Add(
                asset,
                $"definition.dpvs.sModelDrawInsts[{index}].cullDist",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            CompiledSourceBinding flagsBinding = bindings.Add(
                asset,
                $"definition.dpvs.sModelDrawInsts[{index}].flags",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            CompiledSourceBinding boundsBinding = bindings.Add(
                asset,
                $"definition.dpvs.sModelInsts[{index}].bounds",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            CompiledSourceBinding lightingOriginBinding = bindings.Add(
                asset,
                $"definition.dpvs.sModelInsts[{index}].lightingOrigin",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            string? modelName = index <
                data.References.StaticModelDrawInsts.Count
                    ? DisplayName(
                        data.References.StaticModelDrawInsts[index])
                    : null;
            staticModels.Add(new EditorStaticModel(
                DeterministicMapIdentity.Object(
                    bundle.MapIdentity,
                    asset.SerializedType.ToString(),
                    asset.AssetName,
                    "render-static-model",
                    index),
                StaticModelRepresentation.Render,
                Value(index, MapValueProvenance.Derived, recordBinding),
                Value(
                    modelName,
                    modelName is null
                        ? MapValueProvenance.Unknown
                        : MapValueProvenance.ExactSerialized,
                    modelBinding),
                Value(
                    ConvertOrigin(draw.Placement.Origin),
                    MapValueProvenance.ExactDecodedRuntime,
                    originBinding),
                Value<float?>(
                    draw.Placement.Scale,
                    MapValueProvenance.ExactDecodedRuntime,
                    scaleBinding),
                Value<MapBounds?>(
                    Convert(instance.Bounds),
                    MapValueProvenance.ExactDecodedRuntime,
                    boundsBinding),
                StaticModelCompiledFieldBindings.ForRender(
                    originBinding.Id,
                    cullDistanceBinding.Id,
                    flagsBinding.Id,
                    boundsBinding.Id,
                    lightingOriginBinding.Id)));
        }

        for (int cellIndex = 0; cellIndex < world.Cells.Count; cellIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GfxCell cell = world.Cells[cellIndex];
            CompiledSourceBinding cellBinding = bindings.Add(
                asset,
                $"definition.cells[{cellIndex}]",
                cellIndex,
                MapValueProvenance.ExactDecodedRuntime);
            spatial.Add(new EditorSpatialObject(
                DeterministicMapIdentity.Object(
                    bundle.MapIdentity,
                    asset.SerializedType.ToString(),
                    asset.AssetName,
                    "cell",
                    cellIndex),
                MapObjectKind.Cell,
                Value(cellIndex, MapValueProvenance.Derived, cellBinding),
                Value<MapBounds?>(
                    Convert(cell.Bounds),
                    MapValueProvenance.ExactDecodedRuntime,
                    cellBinding),
                Value(
                    cell.PortalCount,
                    MapValueProvenance.ExactDecodedRuntime,
                    cellBinding)));

            for (int portalIndex = 0;
                 portalIndex < cell.Portals.Count;
                 portalIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                GfxPortal portal = cell.Portals[portalIndex];
                CompiledSourceBinding portalBinding = bindings.Add(
                    asset,
                    $"definition.cells[{cellIndex}].portals[{portalIndex}]",
                    portalIndex,
                    MapValueProvenance.ExactDecodedRuntime);
                spatial.Add(new EditorSpatialObject(
                    DeterministicMapIdentity.Object(
                        bundle.MapIdentity,
                        asset.SerializedType.ToString(),
                        asset.AssetName,
                        $"cell-{cellIndex}-portal",
                        portalIndex),
                    MapObjectKind.Portal,
                    Value(
                        portalIndex,
                        MapValueProvenance.Derived,
                        portalBinding),
                    Value<MapBounds?>(
                        null,
                        MapValueProvenance.Unknown,
                        portalBinding),
                    Value(
                        (int)portal.VertexCount,
                        MapValueProvenance.ExactDecodedRuntime,
                        portalBinding)));
            }
        }

        AddEnvironment(
            environment,
            bindings,
            asset,
            "World bounds",
            FormatWorldBounds(world),
            "definition.mins + definition.maxs",
            MapValueProvenance.Derived);
        AddEnvironment(
            environment,
            bindings,
            asset,
            "Skies",
            world.Skies.Count.ToString(),
            "definition.skies",
            MapValueProvenance.ExactSerialized);
        AddEnvironment(
            environment,
            bindings,
            asset,
            "Cells",
            world.Cells.Count.ToString(),
            "definition.cells",
            MapValueProvenance.ExactSerialized);
        AddEnvironment(
            environment,
            bindings,
            asset,
            "Fog type capability flags",
            $"0x{world.FogTypesAllowed:X2}",
            "definition.fogTypesAllowed",
            MapValueProvenance.ExactSerialized);
        AddEnvironment(
            environment,
            bindings,
            asset,
            "Umbra gates",
            world.UmbraGateCount.ToString(),
            "definition.umbraGateCount",
            MapValueProvenance.ExactSerialized);
        AddEnvironment(
            environment,
            bindings,
            asset,
            "Gfx checksum",
            $"0x{world.Checksum:X8}",
            "definition.checksum",
            MapValueProvenance.ExactSerialized);
        AddEnvironment(
            environment,
            bindings,
            asset,
            "Map vertex checksum",
            $"0x{world.MapVertexChecksum:X8}",
            "definition.mapVertexChecksum",
            MapValueProvenance.ExactSerialized);
    }

    private static void ImportClip(
        CompiledMapBundle bundle,
        MapAssetKind clipKind,
        ClipMapBuildData data,
        SourceBindingCatalogBuilder bindings,
        ICollection<EditorStaticModel> staticModels,
        ICollection<EditorCollisionObject> collision,
        ICollection<string> diagnostics,
        CancellationToken cancellationToken)
    {
        CompiledMapAssetDescriptor asset = bundle.RequireAsset(clipKind);
        ClipMapAsset clip = data.Definition;
        if (clip.SerializedType != asset.SerializedType)
        {
            throw new InvalidDataException(
                $"Detached collision type {clip.SerializedType} does not match source row {asset.SerializedType}.");
        }

        for (int index = 0; index < clip.StaticModelList.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClipStaticModel model = clip.StaticModelList[index];
            CompiledSourceBinding recordBinding = bindings.Add(
                asset,
                $"definition.staticModelList[{index}]",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            CompiledSourceBinding modelBinding = bindings.Add(
                asset,
                $"references.staticModels[{index}]",
                index,
                MapValueProvenance.ExactSerialized);
            CompiledSourceBinding originBinding = bindings.Add(
                asset,
                $"definition.staticModelList[{index}].origin",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            CompiledSourceBinding boundsMidpointBinding = bindings.Add(
                asset,
                $"definition.staticModelList[{index}].absMin",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            CompiledSourceBinding boundsHalfSizeBinding = bindings.Add(
                asset,
                $"definition.staticModelList[{index}].absMax",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            string? modelName = index < data.References.StaticModels.Count
                ? DisplayName(data.References.StaticModels[index])
                : null;
            staticModels.Add(new EditorStaticModel(
                DeterministicMapIdentity.Object(
                    bundle.MapIdentity,
                    asset.SerializedType.ToString(),
                    asset.AssetName,
                    "collision-static-model",
                    index),
                StaticModelRepresentation.Collision,
                Value(index, MapValueProvenance.Derived, recordBinding),
                Value(
                    modelName,
                    modelName is null
                        ? MapValueProvenance.Unknown
                        : MapValueProvenance.ExactSerialized,
                    modelBinding),
                Value(
                    Convert(model.Origin),
                    MapValueProvenance.ExactDecodedRuntime,
                    originBinding),
                Value<float?>(
                    null,
                    MapValueProvenance.Unknown,
                    recordBinding),
                Value<MapBounds?>(
                    new MapBounds(
                        Convert(model.AbsMin),
                        Convert(model.AbsMax)),
                    MapValueProvenance.ExactDecodedRuntime,
                    boundsMidpointBinding),
                StaticModelCompiledFieldBindings.ForCollision(
                    originBinding.Id,
                    boundsMidpointBinding.Id,
                    boundsHalfSizeBinding.Id)));
        }

        if (clip.NumBrushes != clip.Brushes.Count)
        {
            diagnostics.Add(
                $"ColMap declares {clip.NumBrushes} brushes but retains {clip.Brushes.Count} brush records.");
        }
        for (int index = 0; index < clip.Brushes.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IW4.Assets.Assets.Physics.CBrush brush = clip.Brushes[index];
            CompiledSourceBinding recordBinding = bindings.Add(
                asset,
                $"definition.brushes[{index}]",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            MapBounds? bounds = index < clip.BrushBounds.Count
                ? Convert(clip.BrushBounds[index])
                : null;
            uint? contents = index < clip.BrushContents.Count
                ? clip.BrushContents[index]
                : null;
            CompiledSourceBinding boundsBinding = bounds is null
                ? recordBinding
                : bindings.Add(
                    asset,
                    $"definition.brushBounds[{index}]",
                    index,
                    MapValueProvenance.ExactDecodedRuntime);
            CompiledSourceBinding contentsBinding = contents is null
                ? recordBinding
                : bindings.Add(
                    asset,
                    $"definition.brushContents[{index}]",
                    index,
                    MapValueProvenance.ExactSerialized);
            collision.Add(new EditorCollisionObject(
                DeterministicMapIdentity.Object(
                    bundle.MapIdentity,
                    asset.SerializedType.ToString(),
                    asset.AssetName,
                    "collision-brush",
                    index),
                CollisionObjectKind.Brush,
                Value(index, MapValueProvenance.Derived, recordBinding),
                Value(
                    bounds,
                    bounds is null
                        ? MapValueProvenance.Unknown
                        : MapValueProvenance.ExactDecodedRuntime,
                    boundsBinding),
                Value(
                    contents,
                    contents is null
                        ? MapValueProvenance.Unknown
                        : MapValueProvenance.ExactSerialized,
                    contentsBinding),
                Value(
                    (int)brush.NumSides,
                    MapValueProvenance.ExactSerialized,
                    recordBinding)));
        }

        int requiredIndices = checked(clip.TriCount * 3);
        if (clip.TriIndices.Count < requiredIndices)
        {
            throw new InvalidDataException(
                $"ColMap triangle index table has {clip.TriIndices.Count} entries for {clip.TriCount} triangles.");
        }
        for (int index = 0; index < clip.TriCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int first = checked(index * 3);
            ushort i0 = clip.TriIndices[first];
            ushort i1 = clip.TriIndices[first + 1];
            ushort i2 = clip.TriIndices[first + 2];
            if (i0 >= clip.Verts.Count ||
                i1 >= clip.Verts.Count ||
                i2 >= clip.Verts.Count)
            {
                throw new InvalidDataException(
                    $"ColMap triangle {index} references a vertex outside the {clip.Verts.Count}-row table.");
            }

            CompiledSourceBinding recordBinding = bindings.Add(
                asset,
                $"definition.triIndices[{first}..{first + 2}] -> definition.verts[{i0},{i1},{i2}]",
                index,
                MapValueProvenance.Derived);
            MapBounds bounds = Bounds(
                clip.Verts[i0],
                clip.Verts[i1],
                clip.Verts[i2]);
            collision.Add(new EditorCollisionObject(
                DeterministicMapIdentity.Object(
                    bundle.MapIdentity,
                    asset.SerializedType.ToString(),
                    asset.AssetName,
                    "collision-triangle",
                    index),
                CollisionObjectKind.Triangle,
                Value(index, MapValueProvenance.Derived, recordBinding),
                Value<MapBounds?>(
                    bounds,
                    MapValueProvenance.Derived,
                    recordBinding),
                Value<uint?>(
                    null,
                    MapValueProvenance.Unknown,
                    recordBinding),
                Value(
                    3,
                    MapValueProvenance.Derived,
                    recordBinding)));
        }
    }

    private static void ImportCom(
        CompiledMapBundle bundle,
        ComWorldBuildData com,
        SourceBindingCatalogBuilder bindings,
        ICollection<EditorPrimaryLight> lights,
        ICollection<EditorEnvironmentValue> environment,
        CancellationToken cancellationToken)
    {
        CompiledMapAssetDescriptor asset = bundle.RequireAsset(MapAssetKind.ComMap);
        for (int index = 0; index < com.PrimaryLights.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ComPrimaryLightBuildData light = com.PrimaryLights[index];
            CompiledSourceBinding recordBinding = bindings.Add(
                asset,
                $"primaryLights[{index}]",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            CompiledSourceBinding nameBinding = bindings.Add(
                asset,
                $"primaryLights[{index}].defName",
                index,
                MapValueProvenance.ExactSerialized);
            CompiledSourceBinding typeBinding = bindings.Add(
                asset,
                $"primaryLights[{index}].type",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            CompiledSourceBinding shadowBinding = bindings.Add(
                asset,
                $"primaryLights[{index}].canUseShadowMap",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            CompiledSourceBinding exponentBinding = bindings.Add(
                asset,
                $"primaryLights[{index}].exponent",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            CompiledSourceBinding unusedBinding = bindings.Add(
                asset,
                $"primaryLights[{index}].unused",
                index,
                MapValueProvenance.ExactSerialized);
            CompiledSourceBinding colorBinding = bindings.Add(
                asset,
                $"primaryLights[{index}].color",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            CompiledSourceBinding directionBinding = bindings.Add(
                asset,
                $"primaryLights[{index}].direction",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            CompiledSourceBinding originBinding = bindings.Add(
                asset,
                $"primaryLights[{index}].origin",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            CompiledSourceBinding radiusBinding = bindings.Add(
                asset,
                $"primaryLights[{index}].radius",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            CompiledSourceBinding outerFovBinding = bindings.Add(
                asset,
                $"primaryLights[{index}].cosHalfFovOuter",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            CompiledSourceBinding innerFovBinding = bindings.Add(
                asset,
                $"primaryLights[{index}].cosHalfFovInner",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            CompiledSourceBinding expandedFovBinding = bindings.Add(
                asset,
                $"primaryLights[{index}].cosHalfFovExpanded",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            CompiledSourceBinding rotationBinding = bindings.Add(
                asset,
                $"primaryLights[{index}].rotationLimit",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            CompiledSourceBinding translationBinding = bindings.Add(
                asset,
                $"primaryLights[{index}].translationLimit",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            lights.Add(new EditorPrimaryLight(
                DeterministicMapIdentity.Object(
                    bundle.MapIdentity,
                    asset.SerializedType.ToString(),
                    asset.AssetName,
                    "primary-light",
                    index),
                Value(index, MapValueProvenance.Derived, recordBinding),
                Value(
                    light.Type,
                    MapValueProvenance.ExactDecodedRuntime,
                    typeBinding),
                Value(
                    light.CanUseShadowMap,
                    MapValueProvenance.ExactDecodedRuntime,
                    shadowBinding),
                Value(
                    light.Exponent,
                    MapValueProvenance.ExactDecodedRuntime,
                    exponentBinding),
                Value(
                    light.Unused,
                    MapValueProvenance.ExactSerialized,
                    unusedBinding),
                Value(
                    Convert(light.Color),
                    MapValueProvenance.ExactDecodedRuntime,
                    colorBinding),
                Value(
                    Convert(light.Direction),
                    MapValueProvenance.ExactDecodedRuntime,
                    directionBinding),
                Value(
                    Convert(light.Origin),
                    MapValueProvenance.ExactDecodedRuntime,
                    originBinding),
                Value(
                    light.Radius,
                    MapValueProvenance.ExactDecodedRuntime,
                    radiusBinding),
                Value(
                    light.CosHalfFovOuter,
                    MapValueProvenance.ExactDecodedRuntime,
                    outerFovBinding),
                Value(
                    light.CosHalfFovInner,
                    MapValueProvenance.ExactDecodedRuntime,
                    innerFovBinding),
                Value(
                    light.CosHalfFovExpanded,
                    MapValueProvenance.ExactDecodedRuntime,
                    expandedFovBinding),
                Value(
                    light.RotationLimit,
                    MapValueProvenance.ExactDecodedRuntime,
                    rotationBinding),
                Value(
                    light.TranslationLimit,
                    MapValueProvenance.ExactDecodedRuntime,
                    translationBinding),
                Value(
                    light.DefName,
                    light.DefName is null
                        ? MapValueProvenance.Unknown
                        : MapValueProvenance.ExactSerialized,
                    nameBinding)));
        }

        AddEnvironment(
            environment,
            bindings,
            asset,
            "ComMap in-use flag",
            com.IsInUse.ToString(),
            "isInUse",
            MapValueProvenance.ExactSerialized);
    }

    private static EditorMapEntitySource ImportMapEnts(
        CompiledMapBundle bundle,
        MapEntsSource source,
        SourceBindingCatalogBuilder bindings,
        ICollection<EditorEntity> entities,
        ICollection<EditorEnvironmentValue> environment,
        ICollection<string> diagnostics,
        CancellationToken cancellationToken)
    {
        CompiledMapAssetDescriptor asset = bundle.RequireAsset(MapAssetKind.MapEnts);
        CompiledSourceBinding rawBinding = bindings.Add(
            asset,
            "entityStringBytes",
            null,
            MapValueProvenance.ExactSerialized);
        byte[] bytes = source.GetEntityBytes();
        MapEntsSyntaxDocument syntax =
            MapEntsSyntaxParser.Parse(bytes, cancellationToken);
        foreach (MapEntsSyntaxDiagnostic diagnostic in syntax.Diagnostics)
        {
            diagnostics.Add(
                $"MapEnt syntax {diagnostic.Code} at byte " +
                $"{diagnostic.Span.Offset}: {diagnostic.Message}");
        }

        for (int index = 0; index < syntax.Entities.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MapEntsSyntaxEntity syntaxEntity = syntax.Entities[index];
            CompiledSourceBinding ordinalBinding = bindings.Add(
                asset,
                $"entityStringBytes.entities[{index}].ordinal",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            CompiledSourceBinding offsetBinding = bindings.Add(
                asset,
                $"entityStringBytes.entities[{index}].span.offset",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            CompiledSourceBinding lengthBinding = bindings.Add(
                asset,
                $"entityStringBytes.entities[{index}].span.length",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            var keyValues = new List<EditorEntityProperty>(
                syntaxEntity.Properties.Count);
            for (int propertyIndex = 0;
                 propertyIndex < syntaxEntity.Properties.Count;
                 propertyIndex++)
            {
                MapEntsSyntaxProperty property =
                    syntaxEntity.Properties[propertyIndex];
                CompiledSourceBinding keyBinding = bindings.Add(
                    asset,
                    $"entityStringBytes.entities[{index}].properties[{propertyIndex}].key",
                    index,
                    MapValueProvenance.ExactDecodedRuntime);
                CompiledSourceBinding valueBinding = bindings.Add(
                    asset,
                    $"entityStringBytes.entities[{index}].properties[{propertyIndex}].value",
                    index,
                    MapValueProvenance.ExactDecodedRuntime);
                keyValues.Add(new EditorEntityProperty(
                    property.Ordinal,
                    Value(
                        property.Key,
                        MapValueProvenance.ExactDecodedRuntime,
                        keyBinding),
                    Value(
                        property.Value,
                        MapValueProvenance.ExactDecodedRuntime,
                        valueBinding),
                    property.Span,
                    property.KeyTokenSpan,
                    property.KeyContentSpan,
                    property.ValueTokenSpan,
                    property.ValueContentSpan));
            }

            string[] classNames = keyValues
                .Where(value => string.Equals(
                    value.Key,
                    "classname",
                    StringComparison.OrdinalIgnoreCase))
                .Select(value => value.Value)
                .ToArray();
            string? className =
                classNames.Length == 1 &&
                !string.IsNullOrWhiteSpace(classNames[0])
                    ? classNames[0]
                    : null;
            MapEntityCompilationAssessment assessment = syntax.CanEdit
                ? MapEntityConsumerCatalog.ConservativeIw4.Classify(
                    keyValues.Select(value =>
                        new KeyValuePair<string, string>(
                            value.Key,
                            value.Value)))
                : new MapEntityCompilationAssessment(
                    MapEntityCompilationRelationship.Unknown,
                    "The byte-authoritative MapEnt syntax failed strict " +
                    "validation, so compiled consumer relationships cannot " +
                    "authorize editing.");
            entities.Add(new EditorEntity(
                DeterministicMapIdentity.Object(
                    bundle.MapIdentity,
                    asset.SerializedType.ToString(),
                    asset.AssetName,
                    "mapent-entity",
                    index),
                Value(
                    index,
                    MapValueProvenance.ExactDecodedRuntime,
                    ordinalBinding),
                Value(
                    syntaxEntity.Span.Offset,
                    MapValueProvenance.ExactDecodedRuntime,
                    offsetBinding),
                Value(
                    syntaxEntity.Span.Length,
                    MapValueProvenance.ExactDecodedRuntime,
                    lengthBinding),
                className,
                assessment,
                keyValues));
        }

        AddEnvironment(
            environment,
            bindings,
            asset,
            "MapEnt bytes",
            bytes.Length.ToString(),
            "entityStringBytes",
            MapValueProvenance.ExactSerialized);
        AddEnvironment(
            environment,
            bindings,
            asset,
            "Trigger models",
            source.TriggerModelCount.ToString(),
            "triggers.models",
            MapValueProvenance.ExactSerialized);
        AddEnvironment(
            environment,
            bindings,
            asset,
            "Stages",
            source.StageCount.ToString(),
            "stages",
            MapValueProvenance.ExactSerialized);
        return new EditorMapEntitySource(
            source.Name,
            syntax,
            rawBinding.Id);
    }

    private static void ImportFx(
        CompiledMapBundle bundle,
        FxWorldBuildData fx,
        SourceBindingCatalogBuilder bindings,
        ICollection<EditorGlassObject> glass,
        ICollection<EditorEnvironmentValue> environment,
        CancellationToken cancellationToken)
    {
        CompiledMapAssetDescriptor asset = bundle.RequireAsset(MapAssetKind.FxMap);
        var definitionStates =
            new EditorFxGlassDefinitionState[fx.GlassSystem.Defs.Count];
        for (int index = 0; index < fx.GlassSystem.Defs.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IW4.Assets.Assets.FxMap.FxGlassDef definition =
                fx.GlassSystem.Defs[index];
            CompiledSourceBinding definitionBinding = bindings.Add(
                asset,
                $"glassSystem.defs[{index}]",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            CompiledSourceBinding thicknessBinding = bindings.Add(
                asset,
                $"glassSystem.defs[{index}].halfThickness",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            CompiledSourceBinding colorBinding = bindings.Add(
                asset,
                $"glassSystem.defs[{index}].color",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            MapObjectId definitionObjectId =
                DeterministicMapIdentity.Object(
                    bundle.MapIdentity,
                    asset.SerializedType.ToString(),
                    asset.AssetName,
                    "fx-glass-definition",
                    index);
            var definitionState = new EditorFxGlassDefinitionState(
                index,
                definitionObjectId,
                thicknessBinding.Id,
                definition.HalfThickness,
                colorBinding.Id,
                definition.Color);
            definitionStates[index] = definitionState;
            glass.Add(new EditorGlassObject(
                definitionObjectId,
                GlassRepresentation.FxDefinition,
                Value(
                    index,
                    MapValueProvenance.Derived,
                    definitionBinding),
                Value<int?>(
                    index,
                    MapValueProvenance.Derived,
                    definitionBinding),
                Value<MapVector3?>(
                    null,
                    MapValueProvenance.Unknown,
                    definitionBinding),
                Value<float?>(
                    definition.HalfThickness,
                    MapValueProvenance.ExactDecodedRuntime,
                    thicknessBinding),
                additionalProperties: null,
                definitionState));
        }

        for (int index = 0;
             index < fx.GlassSystem.InitPieceStates.Count;
             index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IW4.Assets.Assets.FxMap.FxGlassInitPieceState piece =
                fx.GlassSystem.InitPieceStates[index];
            CompiledSourceBinding binding = bindings.Add(
                asset,
                $"glassSystem.initPieceStates[{index}]",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            float? halfThickness =
                piece.DefIndex < fx.GlassSystem.Defs.Count
                    ? fx.GlassSystem.Defs[piece.DefIndex].HalfThickness
                    : null;
            CompiledSourceBinding thicknessBinding = halfThickness is null
                ? binding
                : bindings.Add(
                    asset,
                    $"glassSystem.defs[{piece.DefIndex}].halfThickness",
                    piece.DefIndex,
                    MapValueProvenance.ExactDecodedRuntime);
            EditorFxGlassDefinitionState? definitionState =
                piece.DefIndex < definitionStates.Length
                    ? definitionStates[piece.DefIndex]
                    : null;
            glass.Add(new EditorGlassObject(
                DeterministicMapIdentity.Object(
                    bundle.MapIdentity,
                    asset.SerializedType.ToString(),
                    asset.AssetName,
                    "fx-glass-initial-piece",
                    index),
                GlassRepresentation.FxInitialPiece,
                Value(index, MapValueProvenance.Derived, binding),
                Value<int?>(
                    piece.DefIndex,
                    MapValueProvenance.ExactDecodedRuntime,
                    binding),
                Value<MapVector3?>(
                    new MapVector3(
                        piece.Frame.Origin.X,
                        piece.Frame.Origin.Y,
                        piece.Frame.Origin.Z),
                    MapValueProvenance.ExactDecodedRuntime,
                    binding),
                Value(
                    halfThickness,
                    halfThickness is null
                        ? MapValueProvenance.Unknown
                        : MapValueProvenance.Derived,
                    thicknessBinding),
                additionalProperties: null,
                definitionState));
        }

        AddEnvironment(
            environment,
            bindings,
            asset,
            "FX glass definitions",
            fx.GlassSystem.Defs.Count.ToString(),
            "glassSystem.defs",
            MapValueProvenance.ExactSerialized);
        AddEnvironment(
            environment,
            bindings,
            asset,
            "FX initial glass pieces",
            fx.GlassSystem.InitPieceStates.Count.ToString(),
            "glassSystem.initPieceStates",
            MapValueProvenance.ExactSerialized);
    }

    private static void ImportGame(
        CompiledMapBundle bundle,
        GameWorldMpBuildData game,
        SourceBindingCatalogBuilder bindings,
        ICollection<EditorGlassObject> glass,
        ICollection<EditorEnvironmentValue> environment,
        CancellationToken cancellationToken)
    {
        CompiledMapAssetDescriptor asset =
            bundle.RequireAsset(MapAssetKind.GameMapMp);
        GGlassDataBuildData? data = game.GlassData;
        if (data is null)
            return;
        for (int index = 0; index < data.Pieces.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GGlassPieceBuildData piece = data.Pieces[index];
            CompiledSourceBinding binding = bindings.Add(
                asset,
                $"glassData.pieces[{index}]",
                index,
                MapValueProvenance.ExactDecodedRuntime);
            glass.Add(new EditorGlassObject(
                DeterministicMapIdentity.Object(
                    bundle.MapIdentity,
                    asset.SerializedType.ToString(),
                    asset.AssetName,
                    "gameplay-glass-piece",
                    index),
                GlassRepresentation.GameplayPiece,
                Value(index, MapValueProvenance.Derived, binding),
                Value<int?>(
                    null,
                    MapValueProvenance.Unknown,
                    binding),
                Value<MapVector3?>(
                    null,
                    MapValueProvenance.Unknown,
                    binding),
                Value<float?>(
                    null,
                    MapValueProvenance.Unknown,
                    binding),
                [
                    new EditorObjectProperty(
                        "Damage taken",
                        piece.DamageTaken.ToString(),
                        MapValueProvenance.ExactDecodedRuntime,
                        binding.Id),
                    new EditorObjectProperty(
                        "Collapse time",
                        piece.CollapseTime.ToString(),
                        MapValueProvenance.ExactDecodedRuntime,
                        binding.Id),
                    new EditorObjectProperty(
                        "Last state change time",
                        piece.LastStateChangeTime.ToString(),
                        MapValueProvenance.ExactDecodedRuntime,
                        binding.Id),
                    new EditorObjectProperty(
                        "Packed impact direction",
                        $"0x{piece.PackedImpactDir:X4}",
                        MapValueProvenance.ExactSerialized,
                        binding.Id),
                    new EditorObjectProperty(
                        "Packed impact position",
                        $"0x{piece.PackedImpactPos:X4}",
                        MapValueProvenance.ExactSerialized,
                        binding.Id)
                ]));
        }

        AddEnvironment(
            environment,
            bindings,
            asset,
            "Gameplay glass pieces",
            data.Pieces.Count.ToString(),
            "glassData.pieces",
            MapValueProvenance.ExactSerialized);
        AddEnvironment(
            environment,
            bindings,
            asset,
            "Gameplay glass names",
            data.Names.Count.ToString(),
            "glassData.names",
            MapValueProvenance.ExactSerialized);
    }

    private static bool TryGetClip(
        CompiledMapBundle bundle,
        out MapAssetKind kind,
        out ClipMapBuildData? clip)
    {
        if (bundle.TryGetBaseline(MapAssetKind.ColMapMp, out clip) &&
            clip is not null)
        {
            kind = MapAssetKind.ColMapMp;
            return true;
        }
        if (bundle.TryGetBaseline(MapAssetKind.ColMapSp, out clip) &&
            clip is not null)
        {
            kind = MapAssetKind.ColMapSp;
            return true;
        }

        kind = default;
        clip = null;
        return false;
    }

    private static bool TryGetMapEnts(
        CompiledMapBundle bundle,
        out MapEntsSource? source)
    {
        if (bundle.TryGetBaseline(
                MapAssetKind.MapEnts,
                out IMapEntsBuildData? data) &&
            data is not null)
        {
            source = new MapEntsSource(
                data.Name,
                data.GetEntityStringBytesCopy,
                data.Triggers.Models.Count,
                data.Stages.Count);
            return true;
        }

        source = null;
        return false;
    }

    private static string[] BuildUnresolvedJoinAudit(
        EditorMapDocument document,
        StaticModelCorrespondenceCatalog staticModelCorrespondences)
    {
        ArgumentNullException.ThrowIfNull(staticModelCorrespondences);
        var result = new List<string>();
        int renderModels = document.StaticModels.Count(value =>
            value.Representation == StaticModelRepresentation.Render);
        int collisionModels = document.StaticModels.Count(value =>
            value.Representation == StaticModelRepresentation.Collision);
        if (renderModels > 0 || collisionModels > 0)
        {
            int exactPairs =
                staticModelCorrespondences.Relationships.Count;
            if (exactPairs > 0)
            {
                result.Add(
                    $"{exactPairs} static-model render/collision pair(s) " +
                    "have mutual one-to-one correspondence in this exact " +
                    "imported bundle and are eligible only for conservative " +
                    "compiled suppression.");
            }

            int unresolvedRender = renderModels - exactPairs;
            int unresolvedCollision = collisionModels - exactPairs;
            if (unresolvedRender > 0 || unresolvedCollision > 0)
            {
                result.Add(
                    $"Remaining Gfx render static models " +
                    $"({unresolvedRender}) and Col collision static models " +
                    $"({unresolvedCollision}) stay separate; count, ordinal, " +
                    "name, proximity, or non-unique transform similarity is " +
                    "not identity proof.");
            }
        }

        int fxPieces = document.Glass.Count(value =>
            value.Representation == GlassRepresentation.FxInitialPiece);
        int gamePieces = document.Glass.Count(value =>
            value.Representation == GlassRepresentation.GameplayPiece);
        if (fxPieces > 0 || gamePieces > 0)
        {
            result.Add(
                $"FX initial glass pieces ({fxPieces}) and GameMap gameplay glass pieces ({gamePieces}) remain separate until cross-asset identity is proven.");
        }

        if (document.PrimaryLights.Count > 0)
        {
            result.Add(
                "ComMap primary lights are not joined to Gfx light-region, shadow, or MapEnt stage records.");
        }
        if (document.Entities.Count > 0)
        {
            result.Add(
                "MapEnt syntax entities retain exact text provenance but are " +
                "not joined to compiled Gfx, ColMap, or GameMap counterparts.");
        }
        return result.ToArray();
    }

    private static void AddEnvironment(
        ICollection<EditorEnvironmentValue> values,
        SourceBindingCatalogBuilder bindings,
        CompiledMapAssetDescriptor asset,
        string name,
        string value,
        string fieldPath,
        MapValueProvenance provenance)
    {
        CompiledSourceBinding binding = bindings.Add(
            asset,
            fieldPath,
            null,
            provenance);
        values.Add(new EditorEnvironmentValue(
            name,
            value,
            provenance,
            binding.Id));
    }

    private static MapValue<T> Value<T>(
        T value,
        MapValueProvenance provenance,
        CompiledSourceBinding binding) =>
        new(value, provenance, binding.Id);

    private static string? DisplayName(SymbolicXAssetReference? reference) =>
        reference is null
            ? null
            : XAssetStableIdentity.GetLookupSpelling(
                reference.OriginalSerializedName);

    private static MapVector3 Convert(Vec3 value) =>
        new(value.X, value.Y, value.Z);

    private static MapVector3 Convert(Float3BuildData value) =>
        new(value.X, value.Y, value.Z);

    private static MapVector3 ConvertOrigin(IReadOnlyList<float> values)
    {
        if (values.Count != 3)
        {
            throw new InvalidDataException(
                $"A compiled placement origin has {values.Count} components.");
        }
        return new MapVector3(values[0], values[1], values[2]);
    }

    private static MapBounds Convert(Bounds value) =>
        new(Convert(value.MidPoint), Convert(value.HalfSize));

    private static MapBounds Bounds(Vec3 a, Vec3 b, Vec3 c)
    {
        float minX = MathF.Min(a.X, MathF.Min(b.X, c.X));
        float minY = MathF.Min(a.Y, MathF.Min(b.Y, c.Y));
        float minZ = MathF.Min(a.Z, MathF.Min(b.Z, c.Z));
        float maxX = MathF.Max(a.X, MathF.Max(b.X, c.X));
        float maxY = MathF.Max(a.Y, MathF.Max(b.Y, c.Y));
        float maxZ = MathF.Max(a.Z, MathF.Max(b.Z, c.Z));
        return new MapBounds(
            new MapVector3(
                (minX + maxX) * 0.5f,
                (minY + maxY) * 0.5f,
                (minZ + maxZ) * 0.5f),
            new MapVector3(
                (maxX - minX) * 0.5f,
                (maxY - minY) * 0.5f,
                (maxZ - minZ) * 0.5f));
    }

    private static string FormatWorldBounds(GfxWorldAsset world)
    {
        if (world.Mins.Count != 3 || world.Maxs.Count != 3)
            return "(unavailable)";

        var minimum = new MapVector3(
            world.Mins[0],
            world.Mins[1],
            world.Mins[2]);
        var maximum = new MapVector3(
            world.Maxs[0],
            world.Maxs[1],
            world.Maxs[2]);
        return new MapBounds(
            new MapVector3(
                (minimum.X + maximum.X) * 0.5f,
                (minimum.Y + maximum.Y) * 0.5f,
                (minimum.Z + maximum.Z) * 0.5f),
            new MapVector3(
                (maximum.X - minimum.X) * 0.5f,
                (maximum.Y - minimum.Y) * 0.5f,
                (maximum.Z - minimum.Z) * 0.5f))
            .ToString();
    }

    private sealed record MapEntsSource(
        string? Name,
        Func<byte[]> GetEntityBytes,
        int TriggerModelCount,
        int StageCount);
}

internal sealed class SourceBindingCatalogBuilder
{
    private readonly CompiledMapBundle _bundle;
    private readonly Dictionary<SourceBindingId, CompiledSourceBinding> _bindings =
        [];

    public SourceBindingCatalogBuilder(CompiledMapBundle bundle) =>
        _bundle = bundle ?? throw new ArgumentNullException(nameof(bundle));

    public IReadOnlyList<CompiledSourceBinding> Bindings =>
        new ReadOnlyCollection<CompiledSourceBinding>(
            _bindings.Values
                .OrderBy(value => value.OwnerRow.SerializedIndex)
                .ThenBy(value => value.AssetType)
                .ThenBy(value => value.FieldPath, StringComparer.Ordinal)
                .ThenBy(value => value.SourceOrdinal)
                .ToArray());

    public CompiledSourceBinding Add(
        CompiledMapAssetDescriptor asset,
        string fieldPath,
        int? sourceOrdinal,
        MapValueProvenance provenance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);
        SourceBindingId id = DeterministicMapIdentity.Binding(
            _bundle.MapIdentity,
            asset.SerializedType.ToString(),
            asset.AssetName,
            $"{asset.SourcePath}.{fieldPath}",
            sourceOrdinal);
        var binding = new CompiledSourceBinding(
            id,
            asset.SerializedType,
            asset.AssetName,
            $"{asset.SourcePath}.{fieldPath}",
            asset.OwnerRow,
            sourceOrdinal,
            asset.BaselineDigest,
            provenance);
        if (_bindings.TryGetValue(id, out CompiledSourceBinding? existing))
        {
            if (existing != binding)
            {
                throw new InvalidDataException(
                    $"Deterministic source binding {id} collided across two compiled fields.");
            }
            return existing;
        }

        _bindings.Add(id, binding);
        return binding;
    }
}
