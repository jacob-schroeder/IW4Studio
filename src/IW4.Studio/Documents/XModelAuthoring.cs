using System.Text.Json;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Assets;

namespace IW4.Studio.Documents;

/// <summary>Detached XModel snapshot; nested XModelSurfs remains owned by its
/// parent LOD because the engine has no independent top-level dispatch case.</summary>
public sealed class XModelAuthoredSnapshot : ITargetZoneDetachedSemanticSnapshot
{
    internal XModelAuthoredSnapshot(XModelBuildData data) => Data = data.Copy(); internal XModelBuildData Data { get; } public XAssetType AssetType => XAssetType.XModel;
    internal static XModelAuthoredSnapshot Import(TargetZoneRowSource source) => source.AuthoredDefinition?.SemanticSnapshot is XModelAuthoredSnapshot snapshot ? snapshot : throw new InvalidDataException("XModel editing requires a capture-time detached semantic snapshot because nested provider pointers may be aliases.");
    internal static XModelAuthoredSnapshot FromLoaded(XModelAsset asset) => FromLoaded(asset, new XModelGraphClone());
    internal static XModelAuthoredSnapshot FromLoaded(XModelAsset asset, XModelGraphClone graph) => new(new XModelBuildData(asset.Name, asset.NumBones, asset.NumRootBones, asset.SerializedNumSurfs ?? asset.NumSurfs, asset.Pad07, asset.Scale, asset.NoScalePartBits, asset.BoneNames, asset.ParentList, asset.Quats, asset.Trans, asset.PartClassification, asset.BaseMat.Select(Mat), MaterialReferences(asset), asset.Lods.Select(value => Lod(value, graph)), asset.MaxLoadedLod, asset.NumLods, asset.CollLod, asset.Flags, asset.CollSurfs.Select(Coll), asset.Contents, asset.BoneInfo.Select(Bone), asset.Radius, Vector(asset.Bounds.MidPoint), Vector(asset.Bounds.HalfSize), asset.InvHighMipRadius, asset.MemUsage, Reference(XAssetType.PhysPreset, asset.PhysPresetIncomingDefinition?.Name ?? asset.PhysPreset?.Name), Reference(XAssetType.PhysCollmap, asset.PhysCollmapIncomingDefinition?.Name ?? asset.PhysCollmap?.Name), Provenance(asset, graph), PhysPresetLink(asset), PhysCollmapLink(asset), MaterialLinks(asset, graph)));
    private static XModelDObjAnimMatBuildData Mat(DObjAnimMat value) => new(new Float4BuildData(value.Quat.X, value.Quat.Y, value.Quat.Z, value.Quat.W), Vector(value.Trans), value.TransWeight);
    private static XModelLodBuildData Lod(XModelLodInfo value, XModelGraphClone graph)
    {
        XModelSurfsAsset? serializedSurfs =
            value.ModelSurfsIncomingDefinition ?? value.ModelSurfs;
        IReadOnlyList<uint> serializedPartBits =
            value.SerializedPartBits.Count == 6
                ? value.SerializedPartBits
                : value.PartBits;
        XModelSurfsBuildData? serializedBuild =
            serializedSurfs is null
                ? null
                : Surfs(serializedSurfs, graph, value.ModelSurfs);
        return new XModelLodBuildData(
            value.Dist,
            value.SerializedNumSurfs ?? value.NumSurfs,
            value.SerializedSurfIndex ?? value.SurfIndex,
            serializedPartBits,
            serializedBuild,
            SourceForm(value.ModelSurfsPointer.Type),
            ModelSurfsLink(value, serializedBuild));
    }
    private static XModelSurfsBuildData Surfs(
        XModelSurfsAsset value,
        XModelGraphClone graph,
        XModelSurfsAsset? canonicalFallback = null)
    {
        bool packedSurfaces =
            value.SurfsPointer.Type ==
            IW4.FastFiles.Pointers.PointerType.Offset;
        IReadOnlyList<XSurface> surfaces =
            packedSurfaces &&
            value.Surfaces.Count == 0 &&
            canonicalFallback is { Surfaces.Count: > 0 }
                ? canonicalFallback.Surfaces
                : value.Surfaces;
        return new XModelSurfsBuildData(
            value.Name,
            value.Pad0A,
            value.PartBits,
            surfaces.Select(surface => Surface(surface, graph)),
            value.NumSurfs,
            packedSurfaces ? value.SurfsPointer.Raw : null);
    }
    private static XModelSurfaceBuildData Surface(XSurface value, XModelGraphClone graph) => new(value.FlagsOrPad00, value.StreamFlags, value.Pad03, value.VertCount, value.TriCount, value.TriIndices, value.VertexInfo.Blend0, value.VertexInfo.Blend1, value.VertexInfo.Blend2, value.VertexInfo.Blend3, value.VertexInfo.VertsBlend, value.Verts0, value.Vb0.StreamSource, value.Vb0.DataOffset, value.Verts1, value.Vb1.StreamSource, value.Vb1.DataOffset, value.VertList.Select(Rigid).ToArray(), value.IndexBuffer.DataOffset, value.PartBits, new XModelSurfaceLinkerProvenance(Token(value.Verts0RuntimeAddress, graph), Token(value.Verts1RuntimeAddress, graph), Token(value.TriIndicesRuntimeAddress, graph)));
    private static XModelRigidVertListBuildData Rigid(XRigidVertList value) => new(value.BoneOffset, value.VertCount, value.TriOffset, value.TriCount, value.CollisionTree is null ? null : new XModelCollisionTreeBuildData(Vector(value.CollisionTree.Trans), Vector(value.CollisionTree.Scale), value.CollisionTree.Nodes.Select(node => new XModelCollisionNodeBuildData(node.Aabb.MinsX, node.Aabb.MinsY, node.Aabb.MinsZ, node.Aabb.MaxsX, node.Aabb.MaxsY, node.Aabb.MaxsZ, node.ChildBeginIndex, node.ChildCount)).ToArray(), value.CollisionTree.Leafs.Select(leaf => leaf.TriangleBeginIndex).ToArray()));
    private static XModelLinkerProvenance Provenance(XModelAsset value, XModelGraphClone graph) => new(Token(value.BoneNamesRuntimeAddress, graph), Token(value.ParentListRuntimeAddress, graph), Token(value.QuatsRuntimeAddress, graph), Token(value.TransRuntimeAddress, graph), Token(value.PartClassificationRuntimeAddress, graph), Token(value.BaseMatRuntimeAddress, graph), SourceForm(value.PhysPresetPointer.Type), SourceForm(value.PhysCollmapPointer.Type));
    private static XModelReusableStorageToken? Token(IW4.FastFiles.Zone.XBlockAddress? address, XModelGraphClone graph) => address is { } value ? graph.ReusableStorageToken(value) : null;
    private static XModelNestedPointerSourceForm SourceForm(IW4.FastFiles.Pointers.PointerType type) => type switch
    {
        IW4.FastFiles.Pointers.PointerType.Insert => XModelNestedPointerSourceForm.Insert,
        IW4.FastFiles.Pointers.PointerType.Offset => XModelNestedPointerSourceForm.PackedAlias,
        _ => XModelNestedPointerSourceForm.Inline
    };
    private static NestedXAssetBuildLink? PhysPresetLink(XModelAsset value)
    {
        IW4.Assets.Assets.Physics.PhysPresetAsset? incoming =
            value.PhysPresetIncomingDefinition;
        string? name = incoming?.Name ?? value.PhysPreset?.Name;
        if (name is null || value.PhysPresetPointer.Type == IW4.FastFiles.Pointers.PointerType.Null)
            return null;
        return new NestedXAssetBuildLink(
            new SymbolicXAssetReference(XAssetType.PhysPreset, name),
            NestedSourceForm(value.PhysPresetPointer.Type),
            incoming is null ? null : PhysPresetBuildData.FromLoaded(incoming),
            ImportedPackedRaw(value.PhysPresetPointer.Untyped));
    }
    private static NestedXAssetBuildLink? PhysCollmapLink(XModelAsset value)
    {
        IW4.Assets.Assets.Physics.PhysCollmapAsset? incoming =
            value.PhysCollmapIncomingDefinition;
        string? name = incoming?.Name ?? value.PhysCollmap?.Name;
        if (name is null || value.PhysCollmapPointer.Type == IW4.FastFiles.Pointers.PointerType.Null)
            return null;
        return new NestedXAssetBuildLink(
            new SymbolicXAssetReference(XAssetType.PhysCollmap, name),
            NestedSourceForm(value.PhysCollmapPointer.Type),
            incoming is null ? null : PhysCollmapBuildData.FromLoaded(incoming),
            ImportedPackedRaw(value.PhysCollmapPointer.Untyped));
    }
    private static IReadOnlyList<SymbolicXAssetReference?> MaterialReferences(
        XModelAsset value) =>
        Enumerable.Range(0, value.MaterialPointers.Count)
            .Select(index => Reference(
                XAssetType.Material,
                IncomingMaterial(value, index)?.Info.Name ??
                CanonicalMaterial(value, index)?.Info.Name))
            .ToArray();
    private static IReadOnlyList<NestedXAssetBuildLink?> MaterialLinks(
        XModelAsset value,
        XModelGraphClone graph) =>
        Enumerable.Range(0, value.MaterialPointers.Count)
            .Select(index =>
            {
                IW4.FastFiles.Pointers.XPointer<IW4.Assets.Assets.Material.MaterialAsset> pointer =
                    value.MaterialPointers[index];
                if (pointer.Type == IW4.FastFiles.Pointers.PointerType.Null)
                    return null;
                IW4.Assets.Assets.Material.MaterialAsset? incoming =
                    IncomingMaterial(value, index);
                IW4.Assets.Assets.Material.MaterialAsset? canonical =
                    CanonicalMaterial(value, index);
                string? name = incoming?.Info.Name ?? canonical?.Info.Name;
                if (name is null)
                    return null;
                return new NestedXAssetBuildLink(
                    new SymbolicXAssetReference(XAssetType.Material, name),
                    NestedSourceForm(pointer.Type),
                    incoming is null
                        ? null
                        : MaterialAuthoredSnapshot.FromLoaded(
                            incoming,
                            graph.Materials).Data,
                    ImportedPackedRaw(pointer.Untyped));
            })
            .ToArray();
    private static IW4.Assets.Assets.Material.MaterialAsset? IncomingMaterial(
        XModelAsset value,
        int index) =>
        index < value.MaterialIncomingDefinitions.Count
            ? value.MaterialIncomingDefinitions[index]
            : null;
    private static IW4.Assets.Assets.Material.MaterialAsset? CanonicalMaterial(
        XModelAsset value,
        int index) =>
        index < value.Materials.Count ? value.Materials[index] : null;
    private static NestedXAssetBuildLink? ModelSurfsLink(
        XModelLodInfo value,
        XModelSurfsBuildData? serializedBuild)
    {
        if (value.ModelSurfsPointer.Type == IW4.FastFiles.Pointers.PointerType.Null ||
            serializedBuild?.Name is not { } name)
        {
            return null;
        }
        IXModelSurfsBuildData? incoming =
            value.ModelSurfsPointer.Type is
                IW4.FastFiles.Pointers.PointerType.Inline or
                IW4.FastFiles.Pointers.PointerType.Insert
                ? serializedBuild
                : null;
        return new NestedXAssetBuildLink(
            new SymbolicXAssetReference(XAssetType.XModelSurfs, name),
            NestedSourceForm(value.ModelSurfsPointer.Type),
            incoming,
            ImportedPackedRaw(value.ModelSurfsPointer.Untyped));
    }
    private static int? ImportedPackedRaw(
        IW4.FastFiles.Pointers.XPointerReference pointer) =>
        pointer.Type == IW4.FastFiles.Pointers.PointerType.Offset
            ? pointer.Raw
            : null;
    private static NestedXAssetPointerSourceForm NestedSourceForm(IW4.FastFiles.Pointers.PointerType type) => type switch
    {
        IW4.FastFiles.Pointers.PointerType.Inline => NestedXAssetPointerSourceForm.Inline,
        IW4.FastFiles.Pointers.PointerType.Insert => NestedXAssetPointerSourceForm.Insert,
        IW4.FastFiles.Pointers.PointerType.Offset => NestedXAssetPointerSourceForm.PackedAlias,
        _ => throw new InvalidDataException(
            $"Unsupported nested XModel physics pointer source form {type}.")
    };
    private static XModelCollSurfBuildData Coll(XModelCollSurf value) => new(Vector(value.Bounds.MidPoint), Vector(value.Bounds.HalfSize), value.BoneIndex, value.Contents, value.SurfaceFlags); private static XModelBoneInfoBuildData Bone(XBoneInfo value) => new(Vector(value.Bounds.MidPoint), Vector(value.Bounds.HalfSize), value.RadiusSquared); private static Float3BuildData Vector(IW4.Assets.Math.Vec3 value) => new(value.X, value.Y, value.Z); private static SymbolicXAssetReference? Reference(XAssetType type, string? value) => value is null ? null : new(type, value.StartsWith(",", StringComparison.Ordinal) ? value : $",{value}");
}

public sealed class XModelSurfsBuildData : IXModelSurfsBuildData
{
    private readonly uint[] _partBits; private readonly XModelSurfaceBuildData[] _surfaces; internal XModelSurfsBuildData(string? name, ushort pad0A, IEnumerable<uint> partBits, IEnumerable<XModelSurfaceBuildData> surfaces, ushort? numSurfs = null, int? importedSurfacesPackedRaw = null) { Name = name; Pad0A = pad0A; _partBits = partBits.ToArray(); _surfaces = surfaces.Select(Copy).ToArray(); NumSurfs = numSurfs ?? checked((ushort)_surfaces.Length); ImportedSurfacesPackedRaw = importedSurfacesPackedRaw; }
    public XAssetType AssetType => XAssetType.XModelSurfs; public string? Name { get; } public ushort NumSurfs { get; } public ushort Pad0A { get; } public IReadOnlyList<uint> PartBits => Array.AsReadOnly(_partBits); public IReadOnlyList<XModelSurfaceBuildData> Surfaces => Array.AsReadOnly(_surfaces.Select(Copy).ToArray()); public int? ImportedSurfacesPackedRaw { get; } internal XModelSurfsBuildData Copy() => new(Name, Pad0A, _partBits, _surfaces, NumSurfs, ImportedSurfacesPackedRaw);
    internal static XModelSurfaceBuildData Copy(XModelSurfaceBuildData value) => new(value.FlagsOrPad00, value.StreamFlags, value.Pad03, value.VertCount, value.TriCount, value.TriIndices, value.Blend0, value.Blend1, value.Blend2, value.Blend3, value.VertsBlend, value.Verts0, value.Vb0StreamSource, value.Vb0DataOffset, value.Verts1, value.Vb1StreamSource, value.Vb1DataOffset, value.RigidVertLists.Select(Copy).ToArray(), value.IndexBufferDataOffset, value.PartBits, value.LinkerProvenance);
    private static XModelRigidVertListBuildData Copy(XModelRigidVertListBuildData value) => new(value.BoneOffset, value.VertCount, value.TriOffset, value.TriCount, value.CollisionTree is null ? null : new XModelCollisionTreeBuildData(value.CollisionTree.Trans, value.CollisionTree.Scale, value.CollisionTree.Nodes.Select(node => new XModelCollisionNodeBuildData(node.MinsX, node.MinsY, node.MinsZ, node.MaxsX, node.MaxsY, node.MaxsZ, node.ChildBeginIndex, node.ChildCount)).ToArray(), value.CollisionTree.Leafs.ToArray()));
    internal static XModelSurfsBuildData Copy(IXModelSurfsBuildData value) => value is XModelSurfsBuildData typed ? typed.Copy() : new(value.Name, value.Pad0A, value.PartBits, value.Surfaces, value.NumSurfs, value.ImportedSurfacesPackedRaw);
}

public sealed class XModelBuildData : IXModelBuildData
{
    private readonly uint[] _noScalePartBits; private readonly ushort[] _boneNames, _invHigh; private readonly byte[] _parents, _classification; private readonly short[] _quats; private readonly float[] _trans; private readonly XModelDObjAnimMatBuildData[] _baseMat; private readonly SymbolicXAssetReference?[] _materials; private readonly NestedXAssetBuildLink?[] _materialLinks; private readonly XModelLodBuildData[] _lods; private readonly XModelCollSurfBuildData[] _coll; private readonly XModelBoneInfoBuildData[] _boneInfo;
    internal XModelBuildData(string? name, byte numBones, byte numRootBones, byte numSurfs, byte pad07, float scale, IEnumerable<uint> noScalePartBits, IEnumerable<ushort> boneNames, IEnumerable<byte> parentList, IEnumerable<short> quats, IEnumerable<float> trans, IEnumerable<byte> partClassification, IEnumerable<XModelDObjAnimMatBuildData> baseMat, IEnumerable<SymbolicXAssetReference?> materialReferences, IEnumerable<XModelLodBuildData> lods, byte maxLoadedLod, byte numLods, byte collLod, byte flags, IEnumerable<XModelCollSurfBuildData> collSurfs, int contents, IEnumerable<XModelBoneInfoBuildData> boneInfo, float radius, Float3BuildData boundsMidpoint, Float3BuildData boundsHalfSize, IEnumerable<ushort> invHighMipRadius, int memUsage, SymbolicXAssetReference? physPresetReference, SymbolicXAssetReference? physCollmapReference, XModelLinkerProvenance? linkerProvenance = null, NestedXAssetBuildLink? physPresetLink = null, NestedXAssetBuildLink? physCollmapLink = null, IEnumerable<NestedXAssetBuildLink?>? materialLinks = null)
    { Name = name; NumBones = numBones; NumRootBones = numRootBones; NumSurfs = numSurfs; Pad07 = pad07; Scale = scale; _noScalePartBits = noScalePartBits.ToArray(); _boneNames = boneNames.ToArray(); _parents = parentList.ToArray(); _quats = quats.ToArray(); _trans = trans.ToArray(); _classification = partClassification.ToArray(); _baseMat = baseMat.ToArray(); _materials = materialReferences.ToArray(); _materialLinks = materialLinks?.ToArray() ?? []; _lods = lods.Select(Copy).ToArray(); MaxLoadedLod = maxLoadedLod; NumLods = numLods; CollLod = collLod; Flags = flags; _coll = collSurfs.ToArray(); Contents = contents; _boneInfo = boneInfo.ToArray(); Radius = radius; BoundsMidpoint = boundsMidpoint; BoundsHalfSize = boundsHalfSize; _invHigh = invHighMipRadius.ToArray(); MemUsage = memUsage; PhysPresetReference = physPresetReference; PhysCollmapReference = physCollmapReference; LinkerProvenance = linkerProvenance ?? XModelLinkerProvenance.Empty; PhysPresetLink = physPresetLink; PhysCollmapLink = physCollmapLink; }
    public XAssetType AssetType => XAssetType.XModel; public string? Name { get; } public byte NumBones { get; } public byte NumRootBones { get; } public byte NumSurfs { get; } public byte Pad07 { get; } public float Scale { get; } public IReadOnlyList<uint> NoScalePartBits => Array.AsReadOnly(_noScalePartBits); public IReadOnlyList<ushort> BoneNames => Array.AsReadOnly(_boneNames); public IReadOnlyList<byte> ParentList => Array.AsReadOnly(_parents); public IReadOnlyList<short> Quats => Array.AsReadOnly(_quats); public IReadOnlyList<float> Trans => Array.AsReadOnly(_trans); public IReadOnlyList<byte> PartClassification => Array.AsReadOnly(_classification); public IReadOnlyList<XModelDObjAnimMatBuildData> BaseMat => Array.AsReadOnly(_baseMat); public IReadOnlyList<SymbolicXAssetReference?> MaterialReferences => Array.AsReadOnly(_materials); public IReadOnlyList<NestedXAssetBuildLink?> MaterialLinks => Array.AsReadOnly(_materialLinks); public IReadOnlyList<XModelLodBuildData> Lods => Array.AsReadOnly(_lods.Select(Copy).ToArray()); public byte MaxLoadedLod { get; } public byte NumLods { get; } public byte CollLod { get; } public byte Flags { get; } public IReadOnlyList<XModelCollSurfBuildData> CollSurfs => Array.AsReadOnly(_coll); public int Contents { get; } public IReadOnlyList<XModelBoneInfoBuildData> BoneInfo => Array.AsReadOnly(_boneInfo); public float Radius { get; } public Float3BuildData BoundsMidpoint { get; } public Float3BuildData BoundsHalfSize { get; } public IReadOnlyList<ushort> InvHighMipRadius => Array.AsReadOnly(_invHigh); public int MemUsage { get; } public SymbolicXAssetReference? PhysPresetReference { get; } public SymbolicXAssetReference? PhysCollmapReference { get; } public XModelLinkerProvenance LinkerProvenance { get; } public NestedXAssetBuildLink? PhysPresetLink { get; } public NestedXAssetBuildLink? PhysCollmapLink { get; }
    internal XModelBuildData Copy() => new(Name, NumBones, NumRootBones, NumSurfs, Pad07, Scale, _noScalePartBits, _boneNames, _parents, _quats, _trans, _classification, _baseMat, _materials, _lods, MaxLoadedLod, NumLods, CollLod, Flags, _coll, Contents, _boneInfo, Radius, BoundsMidpoint, BoundsHalfSize, _invHigh, MemUsage, PhysPresetReference, PhysCollmapReference, LinkerProvenance, PhysPresetLink, PhysCollmapLink, _materialLinks);
    private static XModelLodBuildData Copy(XModelLodBuildData value) => new(value.Dist, value.NumSurfs, value.SurfIndex, value.PartBits, value.ModelSurfs is null ? null : XModelSurfsBuildData.Copy(value.ModelSurfs), value.ModelSurfsSourceForm, value.ModelSurfsLink);
}

internal sealed class XModelGraphClone
{
    private const int TokenNamespace = 0x10000000;
    private readonly Dictionary<IW4.FastFiles.Zone.XBlockAddress, XModelReusableStorageToken> _storage = [];
    private int _nextToken = 1;

    internal XModelGraphClone()
        : this(new MaterialGraphClone())
    {
    }

    internal XModelGraphClone(MaterialGraphClone materials)
    {
        Materials = materials;
    }

    internal MaterialGraphClone Materials { get; }

    internal XModelReusableStorageToken ReusableStorageToken(
        IW4.FastFiles.Zone.XBlockAddress address)
    {
        if (_storage.TryGetValue(address, out XModelReusableStorageToken existing))
            return existing;
        var created = new XModelReusableStorageToken(
            checked(TokenNamespace + _nextToken++));
        _storage.Add(address, created);
        return created;
    }
}

public sealed class XModelDraft
{
    private XModelBuildData _data; internal XModelDraft(XModelBuildData data) => _data = data.Copy(); public XModelBuildData Data => _data.Copy(); public void SetScale(float value) => _data = Replace(scale: value); public void SetBounds(Float3BuildData midpoint, Float3BuildData halfSize) => _data = Replace(midpoint: midpoint, halfSize: halfSize); public void ReplaceLods(IEnumerable<XModelLodBuildData> value) { ArgumentNullException.ThrowIfNull(value); _data = Replace(lods: value); } internal XModelDraft Clone() => new(_data);
    private XModelBuildData Replace(float? scale = null, Float3BuildData? midpoint = null, Float3BuildData? halfSize = null, IEnumerable<XModelLodBuildData>? lods = null) => new(_data.Name, _data.NumBones, _data.NumRootBones, _data.NumSurfs, _data.Pad07, scale ?? _data.Scale, _data.NoScalePartBits, _data.BoneNames, _data.ParentList, _data.Quats, _data.Trans, _data.PartClassification, _data.BaseMat, _data.MaterialReferences, lods ?? _data.Lods, _data.MaxLoadedLod, _data.NumLods, _data.CollLod, _data.Flags, _data.CollSurfs, _data.Contents, _data.BoneInfo, _data.Radius, midpoint ?? _data.BoundsMidpoint, halfSize ?? _data.BoundsHalfSize, _data.InvHighMipRadius, _data.MemUsage, _data.PhysPresetReference, _data.PhysCollmapReference, _data.LinkerProvenance, _data.PhysPresetLink, _data.PhysCollmapLink, _data.MaterialLinks);
}

public sealed class XModelAuthoringAdapter : AssetAuthoringAdapter<XModelAuthoredSnapshot, XModelDraft, XModelBuildData>
{
    private static readonly XModelBodyEmitter Validator = new(); public override XAssetType AssetType => XAssetType.XModel; public override XModelAuthoredSnapshot ImportAuthoredSnapshot(TargetZoneRowSource source) => XModelAuthoredSnapshot.Import(source); public override XModelDraft CreateDraft(XModelAuthoredSnapshot snapshot) => new(snapshot.Data); public override XModelDraft CloneDraft(XModelDraft draft) => draft.Clone(); public override IReadOnlyList<AssetValidationIssue> ValidateDraft(XModelDraft draft) => Validator.Validate(draft.Data).Select(value => new AssetValidationIssue(value.Path, value.Message, AssetValidationSeverity.Error)).ToArray(); public override bool SemanticallyEquals(XModelDraft left, XModelDraft right) => JsonSerializer.Serialize(left.Data) == JsonSerializer.Serialize(right.Data); public override XModelBuildData ExportBuildData(XModelDraft draft) { XModelBuildData data = draft.Data; if (Validator.Validate(data).Count != 0) throw new InvalidOperationException("XModel draft has validation errors and cannot produce build data."); return data; }
}
