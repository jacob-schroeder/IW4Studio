using System.Text.Json;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.Physics;
using IW4.Assets.Assets.XModel;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Assets;

namespace IW4.Studio.Documents;

public enum ClipStaticModelTranslationSpatialIssueKind
{
    StaticModelOrdinalOutOfRange,
    InvalidSpatialTree,
    TranslationOverflow
}

public sealed record ClipStaticModelTranslationSpatialIssue(
    ClipStaticModelTranslationSpatialIssueKind Kind,
    string Detail);

/// <summary>
/// Read-only proof that one absolute collision-model translation can retain
/// the imported child topology while expanding its owning leaf-to-root
/// envelopes.
/// </summary>
public sealed class ClipStaticModelTranslationSpatialAssessment
{
    internal ClipStaticModelTranslationSpatialAssessment(
        StaticModelTranslationEdit edit,
        IEnumerable<ClipStaticModelTranslationSpatialIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        Edit = edit;
        Issues = Array.AsReadOnly(issues.ToArray());
    }

    public StaticModelTranslationEdit Edit { get; }
    public IReadOnlyList<ClipStaticModelTranslationSpatialIssue> Issues
    {
        get;
    }
    public bool IsEligible => Issues.Count == 0;
}

/// <summary>
/// A value-only ColMap capture. Nested runtime XAsset objects are removed from
/// the copied definition. Their identities, imported pointer forms, and any
/// inline/insert incoming bodies are retained as detached build data in
/// <see cref="ClipMapReferenceBuildData"/>.
/// </summary>
public sealed class ClipMapAuthoredSnapshot : ITargetZoneDetachedSemanticSnapshot
{
    internal ClipMapAuthoredSnapshot(ClipMapBuildData data) => Data = data.Copy();
    internal ClipMapBuildData Data { get; }
    public XAssetType AssetType => Data.AssetType;

    internal static ClipMapAuthoredSnapshot Import(TargetZoneRowSource source) =>
        source.AuthoredDefinition?.SemanticSnapshot is ClipMapAuthoredSnapshot snapshot
            ? snapshot
            : throw new InvalidDataException("ColMap editing requires a capture-time detached semantic snapshot.");

    internal static ClipMapAuthoredSnapshot FromLoaded(ClipMapAsset asset) =>
        FromLoaded(asset, new XModelGraphClone());

    internal static ClipMapAuthoredSnapshot FromLoaded(
        ClipMapAsset asset,
        XModelGraphClone graph) =>
        new(ClipMapBuildData.FromLoaded(asset, graph));
}

/// <summary>Immutable detached ColMap source.  Its definition never contains
/// a loaded XModel, Fx, PhysPreset, MapEnts, block address, or pool handle.
/// Imported packed values are retained only as source-form provenance.</summary>
public sealed partial class ClipMapBuildData : IClipMapBuildData
{
    internal ClipMapBuildData(
        XAssetType serializedType,
        ClipMapAsset definition,
        ClipMapReferenceBuildData references,
        ClipMapLinkerProvenance? linkerProvenance = null)
    {
        if (serializedType is not (XAssetType.ColMapSp or XAssetType.ColMapMp))
            throw new ArgumentOutOfRangeException(nameof(serializedType));
        AssetType = serializedType;
        SerializedType = serializedType;
        Definition = Clone(definition, serializedType);
        References = Copy(references);
        LinkerProvenance =
            linkerProvenance ?? ClipMapLinkerProvenance.Empty;
    }

    public XAssetType AssetType { get; }
    public XAssetType SerializedType { get; }
    public ClipMapAsset Definition { get; }
    public ClipMapReferenceBuildData References { get; }
    public ClipMapLinkerProvenance LinkerProvenance { get; }

    internal ClipMapBuildData Copy() =>
        new(SerializedType, Definition, References, LinkerProvenance);

    /// <summary>
    /// Proves, without mutating or cloning the detached baseline, that one
    /// translation can use the conservative Clip tree-envelope rewriter.
    /// Cross-asset identity and Gfx/lighting eligibility are intentionally
    /// outside this collision-only assessment.
    /// </summary>
    public ClipStaticModelTranslationSpatialAssessment
        AssessConservativeStaticModelTranslation(
            StaticModelTranslationEdit translation)
    {
        ClipStaticModel[] models =
            Definition.StaticModelList.ToArray();
        SModelAabbNode[] nodes =
            Definition.SModelNodes.ToArray();
        ClipStaticModelTreeTopology topology;
        try
        {
            topology = ClipStaticModelTreeTopology.Validate(
                Definition.NumStaticModels,
                models,
                Definition.SModelNodeCount,
                nodes);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or
            OverflowException)
        {
            return new(
                translation,
                [
                    new(
                        ClipStaticModelTranslationSpatialIssueKind
                            .InvalidSpatialTree,
                        exception.Message)
                ]);
        }

        int ordinal = translation.SourceOrdinal;
        if ((uint)ordinal >= Definition.NumStaticModels ||
            ordinal >= models.Length)
        {
            return new(
                translation,
                [
                    new(
                        ClipStaticModelTranslationSpatialIssueKind
                            .StaticModelOrdinalOutOfRange,
                        $"Static-model index {ordinal} is outside the " +
                        $"{Definition.NumStaticModels}-row collision table.")
                ]);
        }

        try
        {
            ClipStaticModel source = models[ordinal];
            Vec3 origin = translation.ToVec3();
            var delta = new Vec3
            {
                X = origin.X - source.Origin.X,
                Y = origin.Y - source.Origin.Y,
                Z = origin.Z - source.Origin.Z
            };
            var movedBounds = new Bounds
            {
                MidPoint =
                    StaticModelSpatialEnvelope.Translate(
                        source.AbsMin,
                        delta),
                HalfSize =
                    StaticModelSpatialEnvelope.Copy(
                        source.AbsMax)
            };
            StaticModelSpatialEnvelope.Validate(
                movedBounds,
                $"staticModelList[{ordinal}].bounds");

            int nodeIndex = topology.ParentNodeByModel[ordinal];
            while (nodeIndex >= 0)
            {
                movedBounds =
                    StaticModelSpatialEnvelope.Include(
                        nodes[nodeIndex].Bounds,
                        movedBounds);
                nodeIndex =
                    topology.ParentNodeByNode[nodeIndex];
            }
        }
        catch (Exception exception) when (
            exception is InvalidDataException or
            OverflowException)
        {
            return new(
                translation,
                [
                    new(
                        ClipStaticModelTranslationSpatialIssueKind
                            .TranslationOverflow,
                        exception.Message)
                ]);
        }

        return new(translation, []);
    }

    /// <summary>
    /// Returns a detached copy with one collision static model suppressed
    /// outside the playable world. The serialized bounds midpoint follows
    /// the same delta while its half-size, model link, node ranges, counts,
    /// and imported pointer topology remain unchanged. This is intentionally
    /// not a general in-world move because the source spatial tree is retained.
    /// </summary>
    public ClipMapBuildData WithSuppressedStaticModel(
        int staticModelIndex,
        float tombstoneZ = -65536f) =>
        WithSuppressedStaticModels(
            [staticModelIndex],
            tombstoneZ);

    /// <summary>
    /// Batch form of conservative suppression. The detached collision graph
    /// and static-model table are copied exactly once.
    /// </summary>
    public ClipMapBuildData WithSuppressedStaticModels(
        IEnumerable<int> staticModelIndices,
        float tombstoneZ = -65536f)
    {
        ArgumentNullException.ThrowIfNull(staticModelIndices);
        if (!float.IsFinite(tombstoneZ))
        {
            throw new ArgumentOutOfRangeException(
                nameof(tombstoneZ),
                "A collision static-model tombstone coordinate must be finite.");
        }

        ClipMapBuildData edited = Copy();
        ClipMapAsset definition = edited.Definition;
        ClipStaticModel[] models =
            definition.StaticModelList.ToArray();
        foreach (int staticModelIndex in staticModelIndices
                     .Distinct()
                     .Order())
        {
            if (staticModelIndex < 0 ||
                staticModelIndex >= definition.NumStaticModels ||
                staticModelIndex >= models.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(staticModelIndices),
                    $"Static-model index {staticModelIndex} is outside the " +
                    $"{definition.NumStaticModels}-row collision table.");
            }

            ClipStaticModel source = models[staticModelIndex];
            var newOrigin = new Vec3
            {
                X = source.Origin.X,
                Y = source.Origin.Y,
                Z = tombstoneZ
            };
            var delta = new Vec3
            {
                X = newOrigin.X - source.Origin.X,
                Y = newOrigin.Y - source.Origin.Y,
                Z = newOrigin.Z - source.Origin.Z
            };
            models[staticModelIndex] = new ClipStaticModel
            {
                XModelPointer = source.XModelPointer,
                XModel = source.XModel,
                XModelIncomingDefinition =
                    source.XModelIncomingDefinition,
                Origin = newOrigin,
                InvScaledAxis = source.InvScaledAxis.ToArray(),
                // The PS3 field names predate the decoded bounds layout:
                // AbsMin is the midpoint and AbsMax is the half-size.
                AbsMin = Translate(source.AbsMin, delta),
                AbsMax = source.AbsMax
            };
        }

        Set(
            definition,
            nameof(ClipMapAsset.StaticModelList),
            models);
        return edited;
    }

    /// <summary>
    /// Returns a detached copy with absolute translations applied to existing
    /// collision static-model rows and with their proven IW4
    /// <see cref="SModelAabbNode"/> leaf-to-root envelopes expanded
    /// conservatively. Node/model cardinality and child ranges are preserved.
    /// This low-level owner rewrite is not standalone save authority: callers
    /// must pair it with the exact Gfx row and every other invalidated
    /// compiled subsystem.
    /// </summary>
    public ClipMapBuildData WithConservativelyTranslatedStaticModels(
        IEnumerable<StaticModelTranslationEdit> translations)
    {
        ArgumentNullException.ThrowIfNull(translations);
        StaticModelTranslationEdit[] edits =
            translations.ToArray();
        if (edits.Length == 0)
            return Copy();
        if (edits.Select(value => value.SourceOrdinal)
                .Distinct().Count() != edits.Length)
        {
            throw new ArgumentException(
                "A collision static-model row may be translated at most once per rebuild.",
                nameof(translations));
        }

        ClipMapBuildData edited = Copy();
        ClipMapAsset definition = edited.Definition;
        ClipStaticModel[] models =
            definition.StaticModelList.ToArray();
        SModelAabbNode[] nodes =
            definition.SModelNodes.ToArray();
        ClipStaticModelTreeTopology topology =
            ClipStaticModelTreeTopology.Validate(
                definition.NumStaticModels,
                models,
                definition.SModelNodeCount,
                nodes);

        foreach (StaticModelTranslationEdit edit in edits
                     .OrderBy(value => value.SourceOrdinal))
        {
            int ordinal = edit.SourceOrdinal;
            if ((uint)ordinal >= definition.NumStaticModels ||
                ordinal >= models.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(translations),
                    $"Static-model index {ordinal} is outside the " +
                    $"{definition.NumStaticModels}-row collision table.");
            }

            ClipStaticModel source = models[ordinal];
            Vec3 origin = edit.ToVec3();
            var delta = new Vec3
            {
                X = origin.X - source.Origin.X,
                Y = origin.Y - source.Origin.Y,
                Z = origin.Z - source.Origin.Z
            };
            Vec3 midpoint =
                StaticModelSpatialEnvelope.Translate(
                    source.AbsMin,
                    delta);
            var movedBounds = new Bounds
            {
                MidPoint = midpoint,
                HalfSize =
                    StaticModelSpatialEnvelope.Copy(
                        source.AbsMax)
            };
            StaticModelSpatialEnvelope.Validate(
                movedBounds,
                $"staticModelList[{ordinal}].bounds");
            models[ordinal] = new ClipStaticModel
            {
                XModelPointer = source.XModelPointer,
                XModel = source.XModel,
                XModelIncomingDefinition =
                    source.XModelIncomingDefinition,
                Origin = origin,
                InvScaledAxis =
                    source.InvScaledAxis
                        .Select(StaticModelSpatialEnvelope.Copy)
                        .ToArray(),
                // PS3 consumes these rows as midpoint/half-size despite the
                // historical managed property names.
                AbsMin = midpoint,
                AbsMax =
                    StaticModelSpatialEnvelope.Copy(
                        source.AbsMax)
            };

            int nodeIndex = topology.ParentNodeByModel[ordinal];
            while (nodeIndex >= 0)
            {
                SModelAabbNode sourceNode = nodes[nodeIndex];
                Bounds expanded =
                    StaticModelSpatialEnvelope.Include(
                        sourceNode.Bounds,
                        movedBounds);
                nodes[nodeIndex] = new SModelAabbNode
                {
                    Bounds = expanded,
                    FirstChild = sourceNode.FirstChild,
                    ChildCount = sourceNode.ChildCount
                };
                movedBounds = expanded;
                nodeIndex =
                    topology.ParentNodeByNode[nodeIndex];
            }
        }

        Set(
            definition,
            nameof(ClipMapAsset.StaticModelList),
            models);
        Set(
            definition,
            nameof(ClipMapAsset.SModelNodes),
            nodes);
        return edited;
    }

    /// <summary>
    /// Returns a detached copy whose owned nested MapEnts definition differs
    /// only in <c>entityStringBytes</c>. The nested pointer source form,
    /// identity, triggers, stages, padding, collision data, and all other
    /// links remain unchanged.
    /// </summary>
    public ClipMapBuildData WithNestedMapEntsEntityStringBytes(
        ReadOnlySpan<byte> entityStringBytes)
    {
        NestedXAssetBuildLink link = References.MapEntsLink ??
            throw new InvalidOperationException(
                "The ColMap has no retained nested MapEnts link.");
        if (link.SourceForm == NestedXAssetPointerSourceForm.PackedAlias ||
            link.IncomingDefinition is not IMapEntsBuildData source)
        {
            throw new InvalidOperationException(
                "Only an owned inline or insert MapEnts definition can be " +
                "replaced through its ColMap owner.");
        }

        var replacement = new MapEntsBuildData(
            source.Name,
            entityStringBytes,
            source.Triggers,
            source.Stages,
            source.GetPad29To2BCopy());
        var replacementLink = new NestedXAssetBuildLink(
            link.Reference,
            link.SourceForm,
            replacement,
            link.ImportedPackedRaw,
            link.ImportedOwnerCellRaw);
        var replacementReferences = new ClipMapReferenceBuildData(
            References.StaticModels,
            References.DynamicEntities,
            References.MapEnts,
            References.StaticModelLinks,
            replacementLink);
        return new ClipMapBuildData(
            SerializedType,
            Definition,
            replacementReferences,
            LinkerProvenance);
    }

    private static Vec3 Translate(Vec3 value, Vec3 delta) =>
        new()
        {
            X = value.X + delta.X,
            Y = value.Y + delta.Y,
            Z = value.Z + delta.Z
        };

    private sealed class ClipStaticModelTreeTopology
    {
        private ClipStaticModelTreeTopology(
            int[] parentNodeByModel,
            int[] parentNodeByNode)
        {
            ParentNodeByModel = parentNodeByModel;
            ParentNodeByNode = parentNodeByNode;
        }

        public int[] ParentNodeByModel { get; }
        public int[] ParentNodeByNode { get; }

        public static ClipStaticModelTreeTopology Validate(
            int modelCount,
            IReadOnlyList<ClipStaticModel> models,
            int nodeCount,
            IReadOnlyList<SModelAabbNode> nodes)
        {
            if (modelCount < 0 ||
                modelCount != models.Count ||
                nodeCount < 0 ||
                nodeCount != nodes.Count)
            {
                throw new InvalidDataException(
                    "Collision static-model counts do not match their serialized tables.");
            }
            if (modelCount == 0 || nodeCount == 0)
            {
                throw new InvalidDataException(
                    "A translated collision static model requires a nonempty model table and root-node tree.");
            }
            int[] modelParents =
                Enumerable.Repeat(-1, modelCount).ToArray();
            int[] nodeParents =
                Enumerable.Repeat(-1, nodeCount).ToArray();
            var childrenByNode = new int[nodeCount][];
            for (int nodeIndex = 0;
                 nodeIndex < nodeCount;
                 nodeIndex++)
            {
                SModelAabbNode node = nodes[nodeIndex];
                StaticModelSpatialEnvelope.Validate(
                    node.Bounds,
                    $"smodelNodes[{nodeIndex}].bounds");
                if (node.ChildCount == 0)
                {
                    throw new InvalidDataException(
                        $"Collision static-model node {nodeIndex} has an empty child range.");
                }

                int first = node.FirstChild;
                int end = checked(first + node.ChildCount);
                if (first < modelCount)
                {
                    if (end > modelCount)
                    {
                        throw new InvalidDataException(
                            $"Collision static-model node {nodeIndex} crosses the model/node virtual-child boundary.");
                    }
                    childrenByNode[nodeIndex] = [];
                    for (int modelIndex = first;
                         modelIndex < end;
                         modelIndex++)
                    {
                        if (modelParents[modelIndex] >= 0)
                        {
                            throw new InvalidDataException(
                                $"Collision static-model row {modelIndex} has more than one spatial parent.");
                        }
                        StaticModelSpatialEnvelope.Validate(
                            new Bounds
                            {
                                MidPoint =
                                    models[modelIndex].AbsMin,
                                HalfSize =
                                    models[modelIndex].AbsMax
                            },
                            $"staticModelList[{modelIndex}].bounds");
                        modelParents[modelIndex] = nodeIndex;
                    }
                    continue;
                }

                int firstNode = first - modelCount;
                int endNode = end - modelCount;
                if (firstNode < 0 ||
                    endNode > nodeCount)
                {
                    throw new InvalidDataException(
                        $"Collision static-model node {nodeIndex} references a child-node range outside the tree.");
                }
                int[] children =
                    Enumerable.Range(
                        firstNode,
                        node.ChildCount)
                    .ToArray();
                childrenByNode[nodeIndex] = children;
                foreach (int childNode in children)
                {
                    if (childNode == nodeIndex ||
                        nodeParents[childNode] >= 0)
                    {
                        throw new InvalidDataException(
                            $"Collision static-model node {childNode} has an invalid or duplicate spatial parent.");
                    }
                    nodeParents[childNode] = nodeIndex;
                }
            }

            if (nodeParents[0] >= 0 ||
                nodeParents.Skip(1).Any(value => value < 0) ||
                modelParents.Any(value => value < 0))
            {
                throw new InvalidDataException(
                    "Collision static-model spatial rows are not one root-owned tree covering every model exactly once.");
            }

            var visited = new bool[nodeCount];
            Visit(0);
            if (visited.Any(value => !value))
            {
                throw new InvalidDataException(
                    "Collision static-model spatial rows contain an unreachable node or cycle.");
            }

            for (int modelIndex = 0;
                 modelIndex < modelCount;
                 modelIndex++)
            {
                int parentNodeIndex = modelParents[modelIndex];
                var modelBounds = new Bounds
                {
                    MidPoint = models[modelIndex].AbsMin,
                    HalfSize = models[modelIndex].AbsMax
                };
                if (!StaticModelSpatialEnvelope.ContainsImported(
                        nodes[parentNodeIndex].Bounds,
                        modelBounds))
                {
                    throw new InvalidDataException(
                        $"Collision static-model leaf {parentNodeIndex} " +
                        $"excludes direct model row {modelIndex}.");
                }
            }
            for (int nodeIndex = 1;
                 nodeIndex < nodeCount;
                 nodeIndex++)
            {
                int parentNodeIndex = nodeParents[nodeIndex];
                if (!StaticModelSpatialEnvelope.ContainsImported(
                        nodes[parentNodeIndex].Bounds,
                        nodes[nodeIndex].Bounds))
                {
                    throw new InvalidDataException(
                        $"Collision static-model node {parentNodeIndex} " +
                        $"excludes direct child node {nodeIndex}.");
                }
            }

            return new ClipStaticModelTreeTopology(
                modelParents,
                nodeParents);

            void Visit(int nodeIndex)
            {
                if (visited[nodeIndex])
                {
                    throw new InvalidDataException(
                        "Collision static-model spatial rows contain a cycle.");
                }
                visited[nodeIndex] = true;
                foreach (int child in
                         childrenByNode[nodeIndex])
                {
                    Visit(child);
                }
            }
        }
    }

    private static void Set(
        object target,
        string propertyName,
        object value)
    {
        System.Reflection.PropertyInfo property =
            target.GetType().GetProperty(
                propertyName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public)
            ?? throw new InvalidDataException(
                $"Property '{propertyName}' was not found on " +
                $"'{target.GetType().Name}'.");
        property.SetValue(target, value);
    }

    internal static ClipMapBuildData FromLoaded(ClipMapAsset asset) =>
        FromLoaded(asset, new XModelGraphClone());

    internal static ClipMapBuildData FromLoaded(
        ClipMapAsset asset,
        XModelGraphClone graph)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(graph);
        XAssetType type = asset.SerializedType;
        var xmodelDefinitions =
            new Dictionary<XModelAsset, XModelBuildData>(
                ReferenceEqualityComparer.Instance);
        XModelBuildData? XModelDefinition(XModelAsset? incoming)
        {
            if (incoming is null)
                return null;
            if (xmodelDefinitions.TryGetValue(
                    incoming,
                    out XModelBuildData? existing))
            {
                return existing;
            }
            XModelBuildData definition =
                XModelAuthoredSnapshot.FromLoaded(incoming, graph).Data;
            xmodelDefinitions.Add(incoming, definition);
            return definition;
        }

        SymbolicXAssetReference?[] staticReferences = asset.StaticModelList
            .Select(value => Reference(
                XAssetType.XModel,
                value.XModelIncomingDefinition?.Name ?? value.XModel?.Name))
            .ToArray();
        NestedXAssetBuildLink?[] staticLinks = asset.StaticModelList
            .Select((value, index) => Link(
                value.XModelPointer.Untyped,
                staticReferences[index],
                XModelDefinition(value.XModelIncomingDefinition)))
            .ToArray();
        var dynamicReferences = new IReadOnlyList<ClipMapDynEntityReferenceBuildData>[asset.DynEntDefList.Count];
        for (int list = 0; list < dynamicReferences.Length; list++)
        {
            dynamicReferences[list] = Array.AsReadOnly(
                asset.DynEntDefList[list]
                    .Select(value =>
                    {
                        SymbolicXAssetReference? xmodel = Reference(
                            XAssetType.XModel,
                            value.XModelIncomingDefinition?.Name ??
                            value.XModel?.Name);
                        SymbolicXAssetReference? destroyFx = Reference(
                            XAssetType.Fx,
                            value.DestroyFx?.Name);
                        SymbolicXAssetReference? physPreset = Reference(
                            XAssetType.PhysPreset,
                            value.PhysPresetIncomingDefinition?.Name ??
                            value.PhysPreset?.Name);
                        IXAssetBuildData? xmodelDefinition =
                            XModelDefinition(
                                value.XModelIncomingDefinition);
                        IXAssetBuildData? physPresetDefinition =
                            value.PhysPresetIncomingDefinition is { } incoming
                                ? PhysPresetBuildData.FromLoaded(incoming)
                                : null;
                        return new ClipMapDynEntityReferenceBuildData(
                            xmodel,
                            destroyFx,
                            physPreset,
                            Link(
                                value.XModelPointer.Untyped,
                                xmodel,
                                xmodelDefinition),
                            Link(
                                value.DestroyFxPointer.Untyped,
                                destroyFx,
                                null),
                            Link(
                                value.PhysPresetPointer.Untyped,
                                physPreset,
                                physPresetDefinition));
                    })
                    .ToArray());
        }
        SymbolicXAssetReference? mapEntsReference = Reference(
            XAssetType.MapEnts,
            asset.MapEntsIncomingDefinition?.Name ?? asset.MapEnts?.Name);
        IXAssetBuildData? mapEntsDefinition =
            asset.MapEntsIncomingDefinition is { } incomingMapEnts
                ? MapEntsAuthoredSnapshot.FromLoaded(incomingMapEnts).Data
                : null;
        var references = new ClipMapReferenceBuildData(
            staticReferences,
            dynamicReferences,
            mapEntsReference,
            staticLinks,
            Link(
                asset.MapEntsPointer.Untyped,
                mapEntsReference,
                mapEntsDefinition));
        var linkerProvenance = new ClipMapLinkerProvenance(
            importedPlanesPackedRaw:
                asset.PlanesPointer.Type == PointerType.Offset
                    ? asset.PlanesPointer.Raw
                    : null,
            importedIsInUse: asset.SerializedIsInUse,
            leafBrushNodeBrushesPointerRaws:
                asset.LeafBrushNodes.Select(node =>
                    node.LeafBrushCount > 0 &&
                    node.Data.BrushesPointer.Type is (
                        PointerType.Inline or PointerType.Offset)
                        ? node.Data.BrushesPointer.Raw
                        : (int?)null),
            partitionBordersPointerRaws:
                asset.Partitions.Select(partition =>
                    partition.BordersPointer.Type == PointerType.Offset
                        ? partition.BordersPointer.Raw
                        : (int?)null));
        return new ClipMapBuildData(
            type,
            asset,
            references,
            linkerProvenance);
    }

    private static SymbolicXAssetReference? Reference(XAssetType type, string? name) =>
        name is null
            ? null
            : new SymbolicXAssetReference(
                type,
                name.StartsWith(",", StringComparison.Ordinal) ? name : $",{name}");

    private static NestedXAssetBuildLink? Link(
        XPointerReference pointer,
        SymbolicXAssetReference? reference,
        IXAssetBuildData? incomingDefinition)
    {
        if (pointer.Type == PointerType.Null || reference is null)
            return null;
        NestedXAssetPointerSourceForm form = pointer.Type switch
        {
            PointerType.Inline => NestedXAssetPointerSourceForm.Inline,
            PointerType.Insert => NestedXAssetPointerSourceForm.Insert,
            PointerType.Offset => NestedXAssetPointerSourceForm.PackedAlias,
            _ => throw new InvalidDataException(
                $"Unsupported nested ColMap pointer source form {pointer.Type}.")
        };
        if (form is not NestedXAssetPointerSourceForm.PackedAlias &&
            incomingDefinition is null)
        {
            // Hand-authored/legacy object graphs did not retain incoming
            // definitions. Keep their established external-reference path.
            return null;
        }
        return new NestedXAssetBuildLink(
            reference,
            form,
            incomingDefinition,
            form == NestedXAssetPointerSourceForm.PackedAlias
                ? pointer.Raw
                : null,
            ImportedOwnerCellRaw: pointer.CellAddress is { } ownerCell
                ? XPointerCodec.Encode(ownerCell)
                : null);
    }

    private static ClipMapReferenceBuildData Copy(
        ClipMapReferenceBuildData value) =>
        new(
            value.StaticModels,
            value.DynamicEntities.Select(list =>
                (IReadOnlyList<ClipMapDynEntityReferenceBuildData>)Array.AsReadOnly(
                    list.Select(item =>
                        new ClipMapDynEntityReferenceBuildData(
                            item.XModel,
                            item.DestroyFx,
                            item.PhysPreset,
                            Copy(item.XModelLink),
                            Copy(item.DestroyFxLink),
                            Copy(item.PhysPresetLink)))
                        .ToArray())),
            value.MapEnts,
            value.StaticModelLinks.Select(Copy),
            Copy(value.MapEntsLink));

    private static NestedXAssetBuildLink? Copy(
        NestedXAssetBuildLink? value) =>
        value is null
            ? null
            : new NestedXAssetBuildLink(
                value.Reference,
                value.SourceForm,
                value.IncomingDefinition,
                value.ImportedPackedRaw,
                value.ImportedOwnerCellRaw);

    private static ClipMapAsset Clone(ClipMapAsset value, XAssetType type)
    {
        var copies = new CloneContext();
        return new ClipMapAsset
        {
        SerializedType = type,
        Name = value.Name,
        IsInUse = value.IsInUse,
        PlaneCount = value.PlaneCount,
        Planes = value.Planes.Select(copies.Plane).ToArray(),
        NumStaticModels = value.NumStaticModels,
        StaticModelList = value.StaticModelList.Select(StaticModel).ToArray(),
        NumMaterials = value.NumMaterials,
        Materials = value.Materials.Select(Material).ToArray(),
        NumBrushSides = value.NumBrushSides,
        BrushSides = value.BrushSides.Select(copies.Side).ToArray(),
        NumBrushEdges = value.NumBrushEdges,
        BrushEdges = value.BrushEdges.ToArray(),
        NumNodes = value.NumNodes,
        Nodes = value.Nodes.Select(copies.Node).ToArray(),
        NumLeafs = value.NumLeafs,
        Leafs = value.Leafs.Select(Leaf).ToArray(),
        LeafBrushNodesCount = value.LeafBrushNodesCount,
        LeafBrushNodes = value.LeafBrushNodes.Select(LeafBrushNode).ToArray(),
        NumLeafBrushes = value.NumLeafBrushes,
        LeafBrushes = value.LeafBrushes.ToArray(),
        NumLeafSurfaces = value.NumLeafSurfaces,
        LeafSurfaces = value.LeafSurfaces.ToArray(),
        VertCount = value.VertCount,
        Verts = value.Verts.Select(Vec).ToArray(),
        TriCount = value.TriCount,
        TriIndices = value.TriIndices.ToArray(),
        TriEdgeIsWalkable = value.TriEdgeIsWalkable.ToArray(),
        BorderCount = value.BorderCount,
        Borders = value.Borders.Select(copies.Border).ToArray(),
        PartitionCount = value.PartitionCount,
        Partitions = value.Partitions.Select(copies.Partition).ToArray(),
        AabbTreeCount = value.AabbTreeCount,
        AabbTrees = value.AabbTrees.Select(Aabb).ToArray(),
        NumSubModels = value.NumSubModels,
        CModels = value.CModels.Select(Model).ToArray(),
        NumBrushes = value.NumBrushes,
        Pad8ETo8F = value.Pad8ETo8F,
        Brushes = value.Brushes.Select(copies.Brush).ToArray(),
        BrushBounds = value.BrushBounds.Select(Bounds).ToArray(),
        BrushContents = value.BrushContents.ToArray(),
        SModelNodeCount = value.SModelNodeCount,
        PadA2ToA3 = value.PadA2ToA3,
        SModelNodes = value.SModelNodes.Select(SModelNode).ToArray(),
        DynEntCount = value.DynEntCount.ToArray(),
        DynEntDefList = value.DynEntDefList.Select(list => (IReadOnlyList<DynEntityDef>)list.Select(DynDef).ToArray()).ToArray(),
        DynEntPoseList = value.DynEntPoseList.Select(list => (IReadOnlyList<DynEntityPose>)list.Select(DynPose).ToArray()).ToArray(),
        DynEntClientList = value.DynEntClientList.Select(list => (IReadOnlyList<DynEntityClient>)list.Select(DynClient).ToArray()).ToArray(),
        DynEntCollList = value.DynEntCollList.Select(list => (IReadOnlyList<DynEntityColl>)list.Select(DynColl).ToArray()).ToArray(),
        Checksum = value.Checksum,
        PadD0ToFF = value.PadD0ToFF.ToArray()
        };
    }

    private static ClipStaticModel StaticModel(ClipStaticModel value) => new() { Origin = Vec(value.Origin), InvScaledAxis = value.InvScaledAxis.Select(Vec).ToArray(), AbsMin = Vec(value.AbsMin), AbsMax = Vec(value.AbsMax) };
    private static ClipMaterial Material(ClipMaterial value) => new() { Name = value.Name, SurfaceFlags = value.SurfaceFlags, Contents = value.Contents };
    private static CPlane Plane(CPlane value) => new() { Normal = Vec(value.Normal), Dist = value.Dist, Type = value.Type, SignBits = value.SignBits, Pad12 = value.Pad12.ToArray() };
    private static CBrushSide Side(CBrushSide value) => new() { Plane = value.Plane is null ? null : Plane(value.Plane), MaterialNum = value.MaterialNum, FirstAdjacentSideOffset = value.FirstAdjacentSideOffset, EdgeCount = value.EdgeCount };
    private static CNode Node(CNode value) => new() { Plane = value.Plane is null ? null : Plane(value.Plane), Children = value.Children.ToArray() };
    private static CLeaf Leaf(CLeaf value) => new() { FirstCollAabbIndex = value.FirstCollAabbIndex, CollAabbCount = value.CollAabbCount, BrushContents = value.BrushContents, TerrainContents = value.TerrainContents, Mins = Vec(value.Mins), Maxs = Vec(value.Maxs), LeafBrushNode = value.LeafBrushNode };
    private static CLeafBrushNode LeafBrushNode(CLeafBrushNode value) => new()
    {
        Axis = value.Axis, Pad01 = value.Pad01, LeafBrushCount = value.LeafBrushCount, Contents = value.Contents,
        Data = value.LeafBrushCount > 0
            ? new CLeafBrushNodeData { Brushes = value.Data.Brushes.ToArray(), LeafUnionPad = value.Data.LeafUnionPad.ToArray() }
            : new CLeafBrushNodeData
            {
                Children = new CLeafBrushNodeChildren
                {
                    Dist = value.Data.Children?.Dist ?? 0,
                    Range = value.Data.Children?.Range ?? 0,
                    ChildOffsets =
                        value.Data.Children?.ChildOffsets.ToArray() ?? []
                }
            }
    };
    private static CollisionBorder Border(CollisionBorder value) => new() { DistEq = value.DistEq.ToArray(), ZBase = value.ZBase, ZSlope = value.ZSlope, Start = value.Start, Length = value.Length };
    private static CollisionPartition Partition(CollisionPartition value) => new() { TriCount = value.TriCount, BorderCount = value.BorderCount, FirstVertSegment = value.FirstVertSegment, Pad03 = value.Pad03, FirstTri = value.FirstTri, Borders = value.Borders.Select(Border).ToArray() };
    private static CollisionAabbTree Aabb(CollisionAabbTree value) => new() { Origin = Vec(value.Origin), HalfSize = Vec(value.HalfSize), MaterialIndex = value.MaterialIndex, ChildCount = value.ChildCount, FirstChildOrPartitionIndex = value.FirstChildOrPartitionIndex };
    private static CModel Model(CModel value) => new() { Mins = Vec(value.Mins), Maxs = Vec(value.Maxs), Radius = value.Radius, Leaf = Leaf(value.Leaf) };
    private static CBrush Brush(CBrush value) => new() { NumSides = value.NumSides, GlassPieceIndex = value.GlassPieceIndex, Sides = value.Sides.Select(Side).ToArray(), BaseAdjacentSide = value.BaseAdjacentSide.ToArray(), AxialMaterialNum = value.AxialMaterialNum.ToArray(), FirstAdjacentSideOffsets = value.FirstAdjacentSideOffsets.ToArray(), EdgeCount = value.EdgeCount.ToArray() };
    private static Bounds Bounds(Bounds value) => new() { MidPoint = Vec(value.MidPoint), HalfSize = Vec(value.HalfSize) };
    private static SModelAabbNode SModelNode(SModelAabbNode value) => new() { Bounds = Bounds(value.Bounds), FirstChild = value.FirstChild, ChildCount = value.ChildCount };
    private static DynEntityDef DynDef(DynEntityDef value) => new() { Type = value.Type, Pose = Placement(value.Pose), BrushModel = value.BrushModel, PhysicsBrushModel = value.PhysicsBrushModel, Health = value.Health, Mass = Mass(value.Mass), Contents = value.Contents };
    private static DynEntityPose DynPose(DynEntityPose value) => new() { Pose = Placement(value.Pose), Radius = value.Radius };
    private static DynEntityClient DynClient(DynEntityClient value) => new() { PhysObjId = value.PhysObjId, Flags = value.Flags, LightingHandle = value.LightingHandle, Health = value.Health };
    private static DynEntityColl DynColl(DynEntityColl value) => new() { Sector = value.Sector, NextEntInSector = value.NextEntInSector, LinkMins = new Vec2 { a = value.LinkMins.a, b = value.LinkMins.b }, LinkMaxs = new Vec2 { a = value.LinkMaxs.a, b = value.LinkMaxs.b } };
    private static GfxPlacement Placement(GfxPlacement value) => new() { Quat = value.Quat.ToArray(), Origin = Vec(value.Origin) };
    private static PhysMass Mass(PhysMass value) => new() { CenterOfMass = Vec(value.CenterOfMass), MomentsOfInertia = Vec(value.MomentsOfInertia), ProductsOfInertia = Vec(value.ProductsOfInertia) };
    private static Vec3 Vec(Vec3 value) => new() { X = value.X, Y = value.Y, Z = value.Z };

    private sealed class CloneContext
    {
        private readonly Dictionary<CPlane, CPlane> _planes = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<CBrushSide, CBrushSide> _sides = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<CollisionBorder, CollisionBorder> _borders = new(ReferenceEqualityComparer.Instance);

        public CPlane Plane(CPlane value)
        {
            if (_planes.TryGetValue(value, out CPlane? existing)) return existing;
            var copy = new CPlane { Normal = Vec(value.Normal), Dist = value.Dist, Type = value.Type, SignBits = value.SignBits, Pad12 = value.Pad12.ToArray() };
            _planes.Add(value, copy);
            return copy;
        }

        public CBrushSide Side(CBrushSide value)
        {
            if (_sides.TryGetValue(value, out CBrushSide? existing)) return existing;
            var copy = new CBrushSide { Plane = value.Plane is null ? null : Plane(value.Plane), MaterialNum = value.MaterialNum, FirstAdjacentSideOffset = value.FirstAdjacentSideOffset, EdgeCount = value.EdgeCount };
            _sides.Add(value, copy);
            return copy;
        }

        public CNode Node(CNode value) => new() { Plane = value.Plane is null ? null : Plane(value.Plane), Children = value.Children.ToArray() };

        public CollisionBorder Border(CollisionBorder value)
        {
            if (_borders.TryGetValue(value, out CollisionBorder? existing)) return existing;
            var copy = new CollisionBorder { DistEq = value.DistEq.ToArray(), ZBase = value.ZBase, ZSlope = value.ZSlope, Start = value.Start, Length = value.Length };
            _borders.Add(value, copy);
            return copy;
        }

        public CollisionPartition Partition(CollisionPartition value) => new() { TriCount = value.TriCount, BorderCount = value.BorderCount, FirstVertSegment = value.FirstVertSegment, Pad03 = value.Pad03, FirstTri = value.FirstTri, Borders = value.Borders.Select(Border).ToArray() };
        public CBrush Brush(CBrush value) => new() { NumSides = value.NumSides, GlassPieceIndex = value.GlassPieceIndex, Sides = value.Sides.Select(Side).ToArray(), BaseAdjacentSide = value.BaseAdjacentSide.ToArray(), AxialMaterialNum = value.AxialMaterialNum.ToArray(), FirstAdjacentSideOffsets = value.FirstAdjacentSideOffsets.ToArray(), EdgeCount = value.EdgeCount.ToArray() };
    }
}

/// <summary>Bounded ColMap draft.  Geometry is currently displayed and
/// validated as one detached unit; target-row identity remains locked.</summary>
public sealed class ClipMapDraft
{
    private ClipMapBuildData _data;
    internal ClipMapDraft(ClipMapBuildData data) => _data = data.Copy();
    public ClipMapBuildData Data => _data.Copy();
    public void Replace(ClipMapBuildData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.SerializedType != _data.SerializedType)
            throw new InvalidOperationException("A ColMap draft cannot change its serialized ColMapSp/ColMapMp row type.");
        _data = data.Copy();
    }
    public void SuppressStaticModel(
        int staticModelIndex,
        float tombstoneZ = -65536f) =>
        _data = _data.WithSuppressedStaticModel(
            staticModelIndex,
            tombstoneZ);
    public void ReplaceNestedMapEntsEntityStringBytes(
        ReadOnlySpan<byte> entityStringBytes) =>
        _data = _data.WithNestedMapEntsEntityStringBytes(
            entityStringBytes);
    internal ClipMapDraft Clone() => new(_data);
}

public sealed class ClipMapAuthoringAdapter : AssetAuthoringAdapter<ClipMapAuthoredSnapshot, ClipMapDraft, ClipMapBuildData>
{
    private readonly ClipMapBodyEmitter _validator;
    public ClipMapAuthoringAdapter(XAssetType type) => _validator = new ClipMapBodyEmitter(type);
    public override XAssetType AssetType => _validator.AssetType;
    public override ClipMapAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => ClipMapAuthoredSnapshot.Import(source);
    public override ClipMapDraft CreateDraft(ClipMapAuthoredSnapshot snapshot) => new(snapshot.Data);
    public override ClipMapDraft CloneDraft(ClipMapDraft draft) => draft.Clone();
    public override IReadOnlyList<AssetValidationIssue> ValidateDraft(ClipMapDraft draft) => _validator.Validate(draft.Data).Select(value => new AssetValidationIssue(value.Path, value.Message, AssetValidationSeverity.Error)).ToArray();
    public override bool SemanticallyEquals(
        ClipMapDraft left,
        ClipMapDraft right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ClipMapBuildData leftData = left.Data;
        ClipMapBuildData rightData = right.Data;
        return JsonSerializer.Serialize(leftData) ==
                   JsonSerializer.Serialize(rightData) &&
               SameNestedMapEnts(
                   leftData.References.MapEntsLink,
                   rightData.References.MapEntsLink);
    }
    public override ClipMapBuildData ExportBuildData(ClipMapDraft draft)
    {
        ClipMapBuildData data = draft.Data;
        if (_validator.Validate(data).Count != 0)
            throw new InvalidOperationException("ColMap draft has validation errors and cannot produce build data.");
        return data;
    }

    private static bool SameNestedMapEnts(
        NestedXAssetBuildLink? left,
        NestedXAssetBuildLink? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null ||
            left.Reference != right.Reference ||
            left.SourceForm != right.SourceForm ||
            left.ImportedPackedRaw != right.ImportedPackedRaw)
        {
            return false;
        }

        if (left.IncomingDefinition is null ||
            right.IncomingDefinition is null)
        {
            return left.IncomingDefinition is null &&
                   right.IncomingDefinition is null;
        }
        if (left.IncomingDefinition is not IMapEntsBuildData leftMapEnts ||
            right.IncomingDefinition is not IMapEntsBuildData rightMapEnts)
        {
            return false;
        }

        return leftMapEnts.AssetType == rightMapEnts.AssetType &&
               string.Equals(
                   leftMapEnts.Name,
                   rightMapEnts.Name,
                   StringComparison.Ordinal) &&
               leftMapEnts.GetEntityStringBytesCopy().AsSpan().SequenceEqual(
                   rightMapEnts.GetEntityStringBytesCopy()) &&
               leftMapEnts.GetPad29To2BCopy().AsSpan().SequenceEqual(
                   rightMapEnts.GetPad29To2BCopy()) &&
               MapEntsAuthoringAdapter.SameTriggers(
                   leftMapEnts.Triggers,
                   rightMapEnts.Triggers) &&
               leftMapEnts.Stages.SequenceEqual(rightMapEnts.Stages);
    }
}
