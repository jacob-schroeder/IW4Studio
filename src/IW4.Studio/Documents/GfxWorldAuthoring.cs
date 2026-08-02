using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Assets;

namespace IW4.Studio.Documents;

/// <summary>
/// Capture-time, detached GfxWorld source.  Gfx maps have a large scalar and
/// binary graph, so capture uses a JSON-only graph copy with every nested
/// BaseAsset edge removed; those edges are preserved separately as symbolic
/// references.  The result holds no pool asset, stream cursor, or block
/// allocation from the runtime that loaded the map.
/// </summary>
public sealed class GfxWorldAuthoredSnapshot : ITargetZoneDetachedSemanticSnapshot
{
    internal GfxWorldAuthoredSnapshot(GfxWorldBuildData data) => Data = data.Copy();
    private GfxWorldAuthoredSnapshot(
        GfxWorldBuildData data,
        bool takeOwnership) =>
        Data = takeOwnership ? data : data.Copy();
    internal GfxWorldBuildData Data { get; }
    public XAssetType AssetType => XAssetType.GfxMap;
    internal static GfxWorldAuthoredSnapshot Import(TargetZoneRowSource source) =>
        source.AuthoredDefinition?.SemanticSnapshot is GfxWorldAuthoredSnapshot snapshot
            ? snapshot
            : throw new InvalidDataException("GfxMap editing requires a capture-time detached semantic snapshot.");
    internal static GfxWorldAuthoredSnapshot FromLoaded(GfxWorldAsset asset) =>
        FromLoaded(asset, new DetachedAssetSemanticGraphClone());
    internal static GfxWorldAuthoredSnapshot FromLoaded(
        GfxWorldAsset asset,
        DetachedAssetSemanticGraphClone graph) =>
        new(
            GfxWorldBuildData.FromLoaded(asset, graph),
            takeOwnership: true);
}

public sealed partial class GfxWorldBuildData : IGfxWorldBuildData
{
    public GfxWorldBuildData(GfxWorldAsset definition, GfxWorldReferenceBuildData references)
        : this(
            definition,
            references,
            takeOwnership: false)
    {
    }

    private GfxWorldBuildData(
        GfxWorldAsset definition,
        GfxWorldReferenceBuildData references,
        bool takeOwnership)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(references);
        Definition = takeOwnership
            ? definition
            : CloneDefinition(definition);
        References = takeOwnership
            ? references
            : CopyReferences(references);
    }

    public XAssetType AssetType => XAssetType.GfxMap;
    public GfxWorldAsset Definition { get; }
    public GfxWorldReferenceBuildData References { get; }
    internal GfxWorldBuildData Copy() => new(Definition, References);

    /// <summary>
    /// Returns a detached copy with one static render instance suppressed
    /// outside the playable world. This deliberately preserves every count,
    /// index table, nested-XModel owner, and imported pointer address. The
    /// original spatial-tree membership is therefore only conservative and
    /// this operation must not be used as a general in-world move.
    /// </summary>
    public GfxWorldBuildData WithSuppressedStaticModel(
        int staticModelIndex,
        float tombstoneZ = -65536f) =>
        WithSuppressedStaticModels(
            [staticModelIndex],
            tombstoneZ);

    /// <summary>
    /// Batch form of conservative suppression. The large detached world graph
    /// and parallel static-model arrays are copied exactly once regardless of
    /// how many distinct rows are suppressed.
    /// </summary>
    public GfxWorldBuildData WithSuppressedStaticModels(
        IEnumerable<int> staticModelIndices,
        float tombstoneZ = -65536f)
    {
        ArgumentNullException.ThrowIfNull(staticModelIndices);
        if (!float.IsFinite(tombstoneZ))
        {
            throw new ArgumentOutOfRangeException(
                nameof(tombstoneZ),
                "A static-model tombstone coordinate must be finite.");
        }

        GfxWorldBuildData edited = Copy();
        GfxWorldDpvsStatic dpvs = edited.Definition.Dpvs;
        GfxStaticModelDrawInst[] draws =
            dpvs.SModelDrawInsts.ToArray();
        GfxStaticModelInst[] instances =
            dpvs.SModelInsts.ToArray();
        int[] indices = staticModelIndices
            .Distinct()
            .Order()
            .ToArray();
        foreach (int staticModelIndex in indices)
        {
            if ((uint)staticModelIndex >= dpvs.SModelCount ||
                staticModelIndex >= draws.Length ||
                staticModelIndex >= instances.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(staticModelIndices),
                    $"Static-model index {staticModelIndex} is outside the " +
                    $"{dpvs.SModelCount}-row DPVS table.");
            }

            GfxStaticModelDrawInst sourceDraw =
                draws[staticModelIndex];
            GfxStaticModelInst sourceInstance =
                instances[staticModelIndex];
            if (sourceDraw.Placement.Origin.Count != 3)
            {
                throw new InvalidDataException(
                    $"Static-model index {staticModelIndex} has " +
                    $"{sourceDraw.Placement.Origin.Count} placement " +
                    "coordinates.");
            }

            var newOrigin = new IW4.Assets.Math.Vec3
            {
                X = sourceDraw.Placement.Origin[0],
                Y = sourceDraw.Placement.Origin[1],
                Z = tombstoneZ
            };
            var delta = new IW4.Assets.Math.Vec3
            {
                X = newOrigin.X - sourceDraw.Placement.Origin[0],
                Y = newOrigin.Y - sourceDraw.Placement.Origin[1],
                Z = newOrigin.Z - sourceDraw.Placement.Origin[2]
            };
            draws[staticModelIndex] = new GfxStaticModelDrawInst
            {
                Placement = new GfxPackedPlacement
                {
                    Origin =
                        [newOrigin.X, newOrigin.Y, newOrigin.Z],
                    PackedAxis =
                        sourceDraw.Placement.PackedAxis.ToArray(),
                    Scale = sourceDraw.Placement.Scale
                },
                ModelPointer = sourceDraw.ModelPointer,
                Model = sourceDraw.Model,
                ModelIncomingDefinition =
                    sourceDraw.ModelIncomingDefinition,
                // PS3 static-camera list construction treats zero as
                // unlimited. One unit rejects the off-map tombstone even
                // when its conservative old AABB leaf is visited.
                CullDist = 1,
                LightingHandle = sourceDraw.LightingHandle,
                ReflectionProbeIndex =
                    sourceDraw.ReflectionProbeIndex,
                PrimaryLightIndex = sourceDraw.PrimaryLightIndex,
                // Bit zero excludes the instance from spot/sun shadow list
                // builders while retaining every serialized index.
                Flags = (byte)(sourceDraw.Flags | 1),
                FirstMaterialSkinIndex =
                    sourceDraw.FirstMaterialSkinIndex,
                GroundLighting = sourceDraw.GroundLighting
            };
            instances[staticModelIndex] = new GfxStaticModelInst
            {
                Bounds = new IW4.Assets.Math.Bounds
                {
                    MidPoint = Translate(
                        sourceInstance.Bounds.MidPoint,
                        delta),
                    HalfSize = sourceInstance.Bounds.HalfSize
                },
                LightingOrigin = Translate(
                    sourceInstance.LightingOrigin,
                    delta)
            };
        }

        Set(
            dpvs,
            nameof(GfxWorldDpvsStatic.SModelDrawInsts),
            draws);
        Set(
            dpvs,
            nameof(GfxWorldDpvsStatic.SModelInsts),
            instances);
        return edited;
    }

    private static IW4.Assets.Math.Vec3 Translate(
        IW4.Assets.Math.Vec3 value,
        IW4.Assets.Math.Vec3 delta) =>
        new()
        {
            X = value.X + delta.X,
            Y = value.Y + delta.Y,
            Z = value.Z + delta.Z
        };

    internal static GfxWorldBuildData FromLoaded(GfxWorldAsset asset) =>
        FromLoaded(asset, new DetachedAssetSemanticGraphClone());

    internal static GfxWorldAsset CreateDetachedDefinitionProjection(GfxWorldAsset asset) =>
        CloneSerializedDefinition(asset);

    internal static IReadOnlyList<GfxSurface> GetSerializedSurfaces(
        GfxWorldAsset asset) =>
        asset.Dpvs.SerializedSurfaceState?.Surfaces ??
        asset.Dpvs.Surfaces;

    internal static GfxWorldBuildData FromLoaded(
        GfxWorldAsset asset,
        DetachedAssetSemanticGraphClone graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var nested = new NestedDefinitionCache(graph.XModels);
        IReadOnlyList<GfxSurface> serializedSurfaces =
            GetSerializedSurfaces(asset);
        SymbolicXAssetReference?[] skyReferences = asset.Skies
            .Select(value => Reference(XAssetType.Image, value.SkyImage?.Name))
            .ToArray();
        IXAssetBuildData?[] skyDefinitions = asset.Skies
            .Select(value => nested.Image(value.SkyImageIncomingDefinition))
            .ToArray();
        SymbolicXAssetReference?[] reflectionReferences = asset.WorldDraw
            .ReflectionProbeImages
            .Select(value => Reference(XAssetType.Image, value?.Name))
            .ToArray();
        IXAssetBuildData?[] reflectionDefinitions = asset.WorldDraw
            .ReflectionProbeImageIncomingDefinitions
            .Select(nested.Image)
            .ToArray();
        GfxLightmapReferenceBuildData[] lightmapReferences = asset.WorldDraw
            .Lightmaps
            .Select(value => new GfxLightmapReferenceBuildData(
                Reference(XAssetType.Image, value.Primary?.Name),
                Reference(XAssetType.Image, value.Secondary?.Name)))
            .ToArray();
        GfxLightmapDefinitionBuildData[] lightmapDefinitions = asset.WorldDraw
            .Lightmaps
            .Select(value => new GfxLightmapDefinitionBuildData(
                nested.Image(value.PrimaryIncomingDefinition),
                nested.Image(value.SecondaryIncomingDefinition)))
            .ToArray();
        SymbolicXAssetReference?[] materialMemoryReferences = asset.MaterialMemory
            .Select(value => Reference(XAssetType.Material, value.Material?.Info.Name))
            .ToArray();
        IXAssetBuildData?[] materialMemoryDefinitions = asset.MaterialMemory
            .Select(value => nested.Material(value.MaterialIncomingDefinition))
            .ToArray();
        SymbolicXAssetReference?[] surfaceReferences = serializedSurfaces
            .Select(value => Reference(XAssetType.Material, value.Material?.Info.Name))
            .ToArray();
        IXAssetBuildData?[] surfaceDefinitions = serializedSurfaces
            .Select(value => nested.Material(value.MaterialIncomingDefinition))
            .ToArray();
        SymbolicXAssetReference?[] smodelReferences = asset.Dpvs.SModelDrawInsts
            .Select(value => Reference(XAssetType.XModel, value.Model?.Name))
            .ToArray();
        IXAssetBuildData?[] smodelDefinitions = asset.Dpvs.SModelDrawInsts
            .Select(value => nested.XModel(value.ModelIncomingDefinition))
            .ToArray();
        SymbolicXAssetReference? lightmapOverridePrimaryReference =
            Reference(XAssetType.Image, asset.WorldDraw.LightmapOverridePrimary?.Name);
        SymbolicXAssetReference? lightmapOverrideSecondaryReference =
            Reference(XAssetType.Image, asset.WorldDraw.LightmapOverrideSecondary?.Name);
        SymbolicXAssetReference? sunSpriteReference =
            Reference(XAssetType.Material, asset.Sun.SpriteMaterial?.Info.Name);
        SymbolicXAssetReference? sunFlareReference =
            Reference(XAssetType.Material, asset.Sun.FlareMaterial?.Info.Name);
        SymbolicXAssetReference? outdoorReference =
            Reference(XAssetType.Image, asset.OutdoorImage?.Name);
        IXAssetBuildData? lightmapOverridePrimaryDefinition =
            nested.Image(asset.WorldDraw.LightmapOverridePrimaryIncomingDefinition);
        IXAssetBuildData? lightmapOverrideSecondaryDefinition =
            nested.Image(asset.WorldDraw.LightmapOverrideSecondaryIncomingDefinition);
        IXAssetBuildData? sunSpriteDefinition =
            nested.Material(asset.Sun.SpriteMaterialIncomingDefinition);
        IXAssetBuildData? sunFlareDefinition =
            nested.Material(asset.Sun.FlareMaterialIncomingDefinition);
        IXAssetBuildData? outdoorDefinition =
            nested.Image(asset.OutdoorImageIncomingDefinition);
        GfxLightmapLinkBuildData[] lightmapLinks = asset.WorldDraw.Lightmaps
            .Select((value, index) => new GfxLightmapLinkBuildData(
                Link(
                    value.PrimaryPointer.Untyped,
                    lightmapReferences[index].Primary,
                    lightmapDefinitions[index].Primary),
                Link(
                    value.SecondaryPointer.Untyped,
                    lightmapReferences[index].Secondary,
                    lightmapDefinitions[index].Secondary)))
            .ToArray();
        if (!asset.WorldDraw.Lightmaps.Any(value =>
                value.PrimaryPointer.Type != PointerType.Null ||
                value.SecondaryPointer.Type != PointerType.Null))
        {
            lightmapLinks = [];
        }
        GfxAabbTreeIndexPointerBuildData[][] aabbIndexPointers =
            asset.CellTrees
                .Select(cell => cell.AabbTrees
                    .Select(tree => AabbIndexPointer(
                        tree.SModelIndexesPointer.Untyped))
                    .ToArray())
                .ToArray();
        if (!asset.CellTrees
                .SelectMany(cell => cell.AabbTrees)
                .Any(tree =>
                    tree.SModelIndexesPointer.Type != PointerType.Null))
        {
            aabbIndexPointers = [];
        }

        return new GfxWorldBuildData(
            CloneSerializedDefinition(asset),
            new GfxWorldReferenceBuildData
            {
            SkyImages = skyReferences,
            ReflectionProbeImages = reflectionReferences,
            Lightmaps = lightmapReferences,
            LightmapOverridePrimary = lightmapOverridePrimaryReference,
            LightmapOverrideSecondary = lightmapOverrideSecondaryReference,
            MaterialMemory = materialMemoryReferences,
            SunSpriteMaterial = sunSpriteReference,
            SunFlareMaterial = sunFlareReference,
            OutdoorImage = outdoorReference,
            SurfaceMaterials = surfaceReferences,
            StaticModelDrawInsts = smodelReferences,
            SkyImageDefinitions = skyDefinitions,
            ReflectionProbeImageDefinitions = reflectionDefinitions,
            LightmapDefinitions = lightmapDefinitions,
            LightmapOverridePrimaryDefinition = lightmapOverridePrimaryDefinition,
            LightmapOverrideSecondaryDefinition = lightmapOverrideSecondaryDefinition,
            MaterialMemoryDefinitions = materialMemoryDefinitions,
            SunSpriteMaterialDefinition = sunSpriteDefinition,
            SunFlareMaterialDefinition = sunFlareDefinition,
            OutdoorImageDefinition = outdoorDefinition,
            SurfaceMaterialDefinitions = surfaceDefinitions,
            StaticModelDrawInstDefinitions = smodelDefinitions,
            SkyImageLinks = BuildLinks(
                asset.Skies.Select(value => value.SkyImagePointer.Untyped),
                skyReferences,
                skyDefinitions),
            ReflectionProbeImageLinks =
                asset.WorldDraw.ReflectionProbeImagePointers.Count ==
                reflectionReferences.Length
                    ? BuildLinks(
                        asset.WorldDraw.ReflectionProbeImagePointers
                            .Select(value => value.Untyped),
                        reflectionReferences,
                        reflectionDefinitions)
                    : [],
            LightmapLinks = lightmapLinks,
            LightmapOverridePrimaryLink = Link(
                asset.WorldDraw.LightmapOverridePrimaryPointer.Untyped,
                lightmapOverridePrimaryReference,
                lightmapOverridePrimaryDefinition),
            LightmapOverrideSecondaryLink = Link(
                asset.WorldDraw.LightmapOverrideSecondaryPointer.Untyped,
                lightmapOverrideSecondaryReference,
                lightmapOverrideSecondaryDefinition),
            MaterialMemoryLinks = BuildLinks(
                asset.MaterialMemory.Select(value => value.MaterialPointer.Untyped),
                materialMemoryReferences,
                materialMemoryDefinitions),
            SunSpriteMaterialLink = Link(
                asset.Sun.SpriteMaterialPointer.Untyped,
                sunSpriteReference,
                sunSpriteDefinition),
            SunFlareMaterialLink = Link(
                asset.Sun.FlareMaterialPointer.Untyped,
                sunFlareReference,
                sunFlareDefinition),
            OutdoorImageLink = Link(
                asset.OutdoorImagePointer.Untyped,
                outdoorReference,
                outdoorDefinition),
            SurfaceMaterialLinks = BuildLinks(
                serializedSurfaces.Select(
                    value => value.MaterialPointer.Untyped),
                surfaceReferences,
                surfaceDefinitions),
            StaticModelDrawInstLinks = BuildLinks(
                asset.Dpvs.SModelDrawInsts.Select(value => value.ModelPointer.Untyped),
                smodelReferences,
                smodelDefinitions),
                AabbTreeSModelIndexPointers = aabbIndexPointers
            },
            takeOwnership: true);
    }

    private static GfxAabbTreeIndexPointerBuildData AabbIndexPointer(
        XPointerReference pointer)
    {
        GfxDirectPointerSourceForm form = pointer.Type switch
        {
            PointerType.Null => GfxDirectPointerSourceForm.Null,
            PointerType.Inline => GfxDirectPointerSourceForm.Inline,
            PointerType.Insert => GfxDirectPointerSourceForm.Insert,
            PointerType.Offset => GfxDirectPointerSourceForm.PackedAlias,
            _ => throw new InvalidDataException(
                $"Unsupported GfxAabbTree index pointer source form {pointer.Type}.")
        };
        return new GfxAabbTreeIndexPointerBuildData(
            form,
            form == GfxDirectPointerSourceForm.PackedAlias
                ? pointer.Raw
                : null);
    }

    private static IReadOnlyList<NestedXAssetBuildLink?> BuildLinks(
        IEnumerable<XPointerReference> pointers,
        IReadOnlyList<SymbolicXAssetReference?> references,
        IReadOnlyList<IXAssetBuildData?> definitions)
    {
        XPointerReference[] copiedPointers = pointers.ToArray();
        if (copiedPointers.Length != references.Count ||
            definitions.Count != references.Count)
        {
            return [];
        }
        if (!copiedPointers.Any(pointer => pointer.Type != PointerType.Null))
            return [];
        return copiedPointers
            .Select((pointer, index) =>
                Link(pointer, references[index], definitions[index]))
            .ToArray();
    }

    private static NestedXAssetBuildLink? Link(
        XPointerReference pointer,
        SymbolicXAssetReference? reference,
        IXAssetBuildData? definition)
    {
        if (pointer.Type == PointerType.Null || reference is null)
            return null;
        NestedXAssetPointerSourceForm form = pointer.Type switch
        {
            PointerType.Inline => NestedXAssetPointerSourceForm.Inline,
            PointerType.Insert => NestedXAssetPointerSourceForm.Insert,
            PointerType.Offset => NestedXAssetPointerSourceForm.PackedAlias,
            _ => throw new InvalidDataException(
                $"Unsupported nested GfxWorld pointer source form {pointer.Type}.")
        };
        return new NestedXAssetBuildLink(
            reference,
            form,
            definition,
            form == NestedXAssetPointerSourceForm.PackedAlias
                ? pointer.Raw
                : null,
            ImportedOwnerCellRaw: pointer.CellAddress is { } ownerCell
                ? XPointerCodec.Encode(ownerCell)
                : null);
    }

    private static GfxWorldReferenceBuildData CopyReferences(GfxWorldReferenceBuildData value)
    {
        var copiedDefinitions = new Dictionary<IXAssetBuildData, IXAssetBuildData>(
            ReferenceEqualityComparer.Instance);
        IXAssetBuildData? CopyDefinition(IXAssetBuildData? definition)
        {
            if (definition is null)
                return null;
            if (copiedDefinitions.TryGetValue(definition, out IXAssetBuildData? existing))
                return existing;
            IXAssetBuildData copy = definition switch
            {
                GfxImageBuildData image => image.Copy(),
                MaterialBuildData material => material.Copy(),
                XModelBuildData model => model.Copy(),
                _ => throw new InvalidDataException(
                    $"GfxWorld nested definition '{definition.GetType().Name}' is not a supported detached Image, Material, or XModel body.")
            };
            copiedDefinitions.Add(definition, copy);
            return copy;
        }
        NestedXAssetBuildLink? CopyLink(NestedXAssetBuildLink? link) =>
            link is null
                ? null
                : new NestedXAssetBuildLink(
                    link.Reference,
                    link.SourceForm,
                    CopyDefinition(link.IncomingDefinition),
                    link.ImportedPackedRaw,
                    link.ImportedOwnerCellRaw);

        return new GfxWorldReferenceBuildData
        {
            SkyImages = value.SkyImages.ToArray(),
            ReflectionProbeImages = value.ReflectionProbeImages.ToArray(),
            Lightmaps = value.Lightmaps.ToArray(),
            LightmapOverridePrimary = value.LightmapOverridePrimary,
            LightmapOverrideSecondary = value.LightmapOverrideSecondary,
            MaterialMemory = value.MaterialMemory.ToArray(),
            SunSpriteMaterial = value.SunSpriteMaterial,
            SunFlareMaterial = value.SunFlareMaterial,
            OutdoorImage = value.OutdoorImage,
            SurfaceMaterials = value.SurfaceMaterials.ToArray(),
            StaticModelDrawInsts = value.StaticModelDrawInsts.ToArray(),
            SkyImageDefinitions = value.SkyImageDefinitions.Select(CopyDefinition).ToArray(),
            ReflectionProbeImageDefinitions = value.ReflectionProbeImageDefinitions.Select(CopyDefinition).ToArray(),
            LightmapDefinitions = value.LightmapDefinitions.Select(item =>
                new GfxLightmapDefinitionBuildData(
                    CopyDefinition(item.Primary),
                    CopyDefinition(item.Secondary))).ToArray(),
            LightmapOverridePrimaryDefinition = CopyDefinition(value.LightmapOverridePrimaryDefinition),
            LightmapOverrideSecondaryDefinition = CopyDefinition(value.LightmapOverrideSecondaryDefinition),
            MaterialMemoryDefinitions = value.MaterialMemoryDefinitions.Select(CopyDefinition).ToArray(),
            SunSpriteMaterialDefinition = CopyDefinition(value.SunSpriteMaterialDefinition),
            SunFlareMaterialDefinition = CopyDefinition(value.SunFlareMaterialDefinition),
            OutdoorImageDefinition = CopyDefinition(value.OutdoorImageDefinition),
            SurfaceMaterialDefinitions = value.SurfaceMaterialDefinitions.Select(CopyDefinition).ToArray(),
            StaticModelDrawInstDefinitions = value.StaticModelDrawInstDefinitions.Select(CopyDefinition).ToArray(),
            SkyImageLinks = value.SkyImageLinks.Select(CopyLink).ToArray(),
            ReflectionProbeImageLinks = value.ReflectionProbeImageLinks.Select(CopyLink).ToArray(),
            LightmapLinks = value.LightmapLinks.Select(item =>
                new GfxLightmapLinkBuildData(
                    CopyLink(item.Primary),
                    CopyLink(item.Secondary))).ToArray(),
            LightmapOverridePrimaryLink = CopyLink(value.LightmapOverridePrimaryLink),
            LightmapOverrideSecondaryLink = CopyLink(value.LightmapOverrideSecondaryLink),
            MaterialMemoryLinks = value.MaterialMemoryLinks.Select(CopyLink).ToArray(),
            SunSpriteMaterialLink = CopyLink(value.SunSpriteMaterialLink),
            SunFlareMaterialLink = CopyLink(value.SunFlareMaterialLink),
            OutdoorImageLink = CopyLink(value.OutdoorImageLink),
            SurfaceMaterialLinks = value.SurfaceMaterialLinks.Select(CopyLink).ToArray(),
            StaticModelDrawInstLinks = value.StaticModelDrawInstLinks.Select(CopyLink).ToArray(),
            AabbTreeSModelIndexPointers = value.AabbTreeSModelIndexPointers
                .Select(cell => (IReadOnlyList<GfxAabbTreeIndexPointerBuildData>)
                    cell.Select(pointer => new GfxAabbTreeIndexPointerBuildData(
                        pointer.SourceForm,
                        pointer.ImportedPackedRaw)).ToArray())
                .ToArray()
        };
    }

    private static SymbolicXAssetReference? Reference(XAssetType type, string? name) => name is null ? null : new(type, name.StartsWith(",", StringComparison.Ordinal) ? name : $",{name}");

    private sealed class NestedDefinitionCache
    {
        private readonly Dictionary<object, IXAssetBuildData> _definitions =
            new(ReferenceEqualityComparer.Instance);
        private readonly XModelGraphClone _xmodels;

        public NestedDefinitionCache(XModelGraphClone xmodels) =>
            _xmodels = xmodels;

        public IXAssetBuildData? Image(GfxImageAsset? asset) =>
            Get(asset, static value => GfxImageAuthoredSnapshot.FromLoaded(value).Data);

        public IXAssetBuildData? Material(MaterialAsset? asset) =>
            Get(
                asset,
                value => MaterialAuthoredSnapshot.FromLoaded(
                    value,
                    _xmodels.Materials).Data);

        public IXAssetBuildData? XModel(XModelAsset? asset) =>
            Get(asset, value => XModelAuthoredSnapshot.FromLoaded(value, _xmodels).Data);

        private IXAssetBuildData? Get<TAsset>(
            TAsset? asset,
            Func<TAsset, IXAssetBuildData> create)
            where TAsset : class
        {
            if (asset is null)
                return null;
            if (_definitions.TryGetValue(asset, out IXAssetBuildData? existing))
                return existing;
            IXAssetBuildData definition = create(asset);
            _definitions.Add(asset, definition);
            return definition;
        }
    }

    private static GfxWorldAsset CloneDefinition(GfxWorldAsset value)
    {
        byte[] utf8Json = JsonSerializer.SerializeToUtf8Bytes(
            value,
            CloneJsonOptions);
        GfxWorldAsset clone = JsonSerializer.Deserialize<GfxWorldAsset>(
            utf8Json,
            CloneJsonOptions)
            ?? throw new InvalidDataException("GfxWorld clone deserialized as null.");
        // The resolver deliberately omits runtime asset edges.  Retain the
        // null-slot topology required by the counted loader arrays.
        Set(clone.WorldDraw, "ReflectionProbeImages", new IW4.Assets.Assets.Image.GfxImageAsset?[value.WorldDraw.ReflectionProbeImages.Count]);
        return clone;
    }

    private static GfxWorldAsset CloneSerializedDefinition(
        GfxWorldAsset value)
    {
        GfxWorldAsset clone = CloneDefinition(value);
        GfxWorldSerializedSurfaceState? serialized =
            value.Dpvs.SerializedSurfaceState;
        if (serialized is null)
            return clone;

        int surfaceCount = value.SurfaceCount;
        if (serialized.SortedSurfIndex.Count !=
                checked((int)value.Dpvs.StaticSurfaceCount) ||
            serialized.Surfaces.Count != surfaceCount ||
            serialized.SurfaceBounds.Count != surfaceCount ||
            serialized.SurfaceMaterials.Count != surfaceCount ||
            serialized.SurfaceCastsSunShadow.Count !=
                value.Dpvs.SurfaceCastsSunShadow.Count)
        {
            throw new InvalidDataException(
                "GfxWorld serialized surface-state provenance no longer " +
                "matches its DPVS header counts.");
        }

        clone.Dpvs.SortedSurfIndex =
            serialized.SortedSurfIndex.ToArray();
        clone.Dpvs.Surfaces = serialized.Surfaces
            .Select(CloneSerializedSurfaceDefinition)
            .ToArray();
        clone.Dpvs.SurfaceBounds = serialized.SurfaceBounds
            .Select(CloneSerializedSurfaceBounds)
            .ToArray();
        clone.Dpvs.SurfaceMaterials =
            serialized.SurfaceMaterials.ToArray();
        clone.Dpvs.SurfaceCastsSunShadow =
            serialized.SurfaceCastsSunShadow.ToArray();
        clone.Dpvs.AuthoredSurfaceIndexByRuntimeSlot =
            Enumerable.Range(0, surfaceCount).ToArray();
        return clone;
    }

    private static GfxSurface CloneSerializedSurfaceDefinition(
        GfxSurface value) =>
        new()
        {
            Triangles = new SrfTriangles
            {
                VertexLayerData = value.Triangles.VertexLayerData,
                BaseVertex = value.Triangles.BaseVertex,
                MinVertexIndex = value.Triangles.MinVertexIndex,
                VertexCount = value.Triangles.VertexCount,
                TriCount = value.Triangles.TriCount,
                BaseIndex = value.Triangles.BaseIndex
            },
            LightmapIndex = value.LightmapIndex,
            ReflectionProbeIndex = value.ReflectionProbeIndex,
            PrimaryLightIndex = value.PrimaryLightIndex,
            CastsSunShadow = value.CastsSunShadow
        };

    private static GfxSurfaceBounds CloneSerializedSurfaceBounds(
        GfxSurfaceBounds value) =>
        new()
        {
            Bounds = new IW4.Assets.Math.Bounds
            {
                MidPoint = value.Bounds.MidPoint,
                HalfSize = value.Bounds.HalfSize
            },
            Unknown18To1F = value.Unknown18To1F.ToArray()
        };

    private static void Set(object target, string propertyName, object value) =>
        target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.SetValue(target, value);

    private static JsonSerializerOptions CloneJsonOptions { get; } = new()
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { RemoveRuntimeAssetEdges } }
    };

    private static void RemoveRuntimeAssetEdges(JsonTypeInfo type)
    {
        if (type.Type.Namespace != "IW4.Assets.Assets.GfxMap") return;
        foreach (JsonPropertyInfo property in type.Properties.Where(property =>
                     property.Name.EndsWith("IncomingDefinition", StringComparison.Ordinal) ||
                     property.Name.EndsWith("IncomingDefinitions", StringComparison.Ordinal) ||
                     property.Name is
                         "Offset" or "StagingAddress" or "RuntimeAddress" or
                         "SerializedSurfaceState" or
                         "SkyImage" or "ReflectionProbeImagePointers" or "ReflectionProbeImages" or
                         "Primary" or "Secondary" or
                         "LightmapOverridePrimary" or "LightmapOverrideSecondary" or
                         "Material" or "SpriteMaterial" or "FlareMaterial" or "OutdoorImage" or
                         "Model").ToArray())
            type.Properties.Remove(property);
    }
}

public sealed class GfxWorldDraft
{
    private GfxWorldBuildData _data;
    internal GfxWorldDraft(GfxWorldBuildData data) => _data = data.Copy();
    public GfxWorldBuildData Data => _data.Copy();
    public void Replace(GfxWorldBuildData value) => _data = value?.Copy() ?? throw new ArgumentNullException(nameof(value));
    public void SuppressStaticModel(
        int staticModelIndex,
        float tombstoneZ = -65536f) =>
        _data = _data.WithSuppressedStaticModel(
            staticModelIndex,
            tombstoneZ);
    internal GfxWorldDraft Clone() => new(_data);
}

public sealed class GfxWorldAuthoringAdapter : AssetAuthoringAdapter<GfxWorldAuthoredSnapshot, GfxWorldDraft, GfxWorldBuildData>
{
    private static readonly GfxWorldBodyEmitter Validator = new();
    public override XAssetType AssetType => XAssetType.GfxMap;
    public override GfxWorldAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => GfxWorldAuthoredSnapshot.Import(source);
    public override GfxWorldDraft CreateDraft(GfxWorldAuthoredSnapshot snapshot) => new(snapshot.Data);
    public override GfxWorldDraft CloneDraft(GfxWorldDraft draft) => draft.Clone();
    public override IReadOnlyList<AssetValidationIssue> ValidateDraft(GfxWorldDraft draft) => Validator.Validate(draft.Data).Select(value => new AssetValidationIssue(value.Path, value.Message, AssetValidationSeverity.Error)).ToArray();
    public override bool SemanticallyEquals(GfxWorldDraft left, GfxWorldDraft right) =>
        MenuSemanticProjection.Serialize(left.Data.Definition) == MenuSemanticProjection.Serialize(right.Data.Definition) && JsonSerializer.Serialize(left.Data.References) == JsonSerializer.Serialize(right.Data.References);
    public override GfxWorldBuildData ExportBuildData(GfxWorldDraft draft)
    {
        GfxWorldBuildData data = draft.Data;
        if (Validator.Validate(data).Count != 0) throw new InvalidOperationException("GfxMap draft has validation errors and cannot produce build data.");
        return data;
    }
}
