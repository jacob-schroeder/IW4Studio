using IW4.Assets.Assets.XModel;
using IW4.Assets.Assets.Physics;
using IW4.Assets.Math;
using IW4.Assets.XModel.Export;
using IW4.FastFiles.Zone;

namespace IW4.Studio.Documents;

public sealed class XModelDraft
{
    private readonly List<XModelLodDraft> _lodAssembly;
    private readonly List<XModelCollSurf> _collisionSurfaces;

    internal XModelDraft(XModelAsset value)
    {
        Model = CopyRoot(value);
        _lodAssembly = CreateAssembly(Model);
        _collisionSurfaces = CopyCollSurfs(Model.CollSurfs).ToList();
        CollisionLod = Model.CollLod;
        PhysPreset = Model.PhysPreset;
        PhysCollmap = Model.PhysCollmap;
    }

    private XModelDraft(XModelDraft value)
    {
        Model = CopyRoot(value.Model);
        _lodAssembly = value._lodAssembly.Select(lod => lod.Clone()).ToList();
        _collisionSurfaces = CopyCollSurfs(value._collisionSurfaces).ToList();
        CollisionLod = value.CollisionLod;
        PhysPreset = value.PhysPreset;
        PhysCollmap = value.PhysCollmap;
    }

    public XModelAsset Model { get; }
    public IReadOnlyList<XModelLodDraft> LodAssembly => _lodAssembly.AsReadOnly();
    public byte CollisionLod { get; private set; }
    public IReadOnlyList<XModelCollSurf> CollisionSurfaces => _collisionSurfaces.AsReadOnly();
    public PhysPresetAsset? PhysPreset { get; private set; }
    public PhysCollmapAsset? PhysCollmap { get; private set; }
    public bool HasStagedAssemblyChanges => !AssemblyEquals(CreateAssembly(Model), Model.CollLod, _lodAssembly, CollisionLod) || !CollSurfsEqual(Model.CollSurfs, _collisionSurfaces) || !ReferenceEquals(Model.PhysPreset, PhysPreset) || !ReferenceEquals(Model.PhysCollmap, PhysCollmap);

    internal XModelDraft Clone() => new(this);

    internal XModelAsset ToAsset() => CopyRoot(
        Model,
        _collisionSurfaces,
        PhysPreset,
        PhysCollmap,
        useAuthoredPhysicsReferences: true);

    public void AppendImportedLod(XModelExportDocument document, string? source)
    {
        ArgumentNullException.ThrowIfNull(document);
        int index = _lodAssembly.FindIndex(lod => !lod.IsOccupied);
        if (index < 0) throw new InvalidOperationException("An XModel contains at most four LODs.");
        float distance = index == 0 ? 0f : MathF.Max(_lodAssembly[index - 1].Distance + 1f, 1f);
        _lodAssembly[index] = new XModelLodDraft(index, distance, null, XModelLodDraft.Freeze(document), source, CreateMaterialMappings(document));
    }

    public void ReplaceLod(int index, XModelExportDocument document, string? source)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateIndex(index);
        if (!_lodAssembly[index].IsOccupied) throw new InvalidOperationException("Only an active LOD can be replaced.");
        XModelLodDraft previous = _lodAssembly[index];
        _lodAssembly[index] = new XModelLodDraft(index, previous.Distance, null, XModelLodDraft.Freeze(document), source, CreateMaterialMappings(document));
    }

    public void RemoveLod(int index)
    {
        ValidateIndex(index);
        if (!_lodAssembly[index].IsOccupied) throw new InvalidOperationException("Only an active LOD can be removed.");
        for (int i = index; i < _lodAssembly.Count - 1; i++)
        {
            XModelLodDraft next = _lodAssembly[i + 1];
            _lodAssembly[i] = new XModelLodDraft(i, next.Distance, next.BaselineLod, next.ImportedDocument is null ? null : XModelLodDraft.Freeze(next.ImportedDocument), next.ImportSource, next.MaterialSlots);
        }
        _lodAssembly[^1] = new XModelLodDraft(_lodAssembly.Count - 1, 0f, null, null, null);
        if (CollisionLod == index) CollisionLod = 0xFF;
        else if (CollisionLod > index && CollisionLod != 0xFF) CollisionLod--;
    }

    public void SetLodDistance(int index, float distance)
    {
        ValidateIndex(index);
        if (!_lodAssembly[index].IsOccupied) throw new InvalidOperationException("Only an active LOD has a distance.");
        if (!float.IsFinite(distance) || distance < 0) throw new ArgumentOutOfRangeException(nameof(distance), "LOD distance must be finite and nonnegative.");
        XModelLodDraft previous = _lodAssembly[index];
        _lodAssembly[index] = new XModelLodDraft(index, distance, previous.BaselineLod, previous.ImportedDocument, previous.ImportSource, previous.MaterialSlots);
    }

    public void SetCollisionLod(byte collisionLod)
    {
        if (collisionLod != 0xFF && (collisionLod >= _lodAssembly.Count || !_lodAssembly[collisionLod].IsOccupied)) throw new ArgumentOutOfRangeException(nameof(collisionLod), "Collision LOD must select an active LOD or None.");
        CollisionLod = collisionLod;
    }

    public void SetImportedMaterialSlot(int lodIndex, int materialIndex, int? sourceSlot)
    {
        ValidateIndex(lodIndex);
        XModelLodDraft previous = _lodAssembly[lodIndex];
        if (previous.ImportedDocument is null || (uint)materialIndex >= (uint)previous.MaterialSlots.Count)
            throw new InvalidOperationException("The selected row has no imported material mapping.");
        if (sourceSlot is < 0 || sourceSlot >= Model.Materials.Count || sourceSlot is int slot && Model.Materials[slot] is null)
            throw new ArgumentOutOfRangeException(nameof(sourceSlot));
        int?[] slots = previous.MaterialSlots.ToArray();
        slots[materialIndex] = sourceSlot;
        _lodAssembly[lodIndex] = new XModelLodDraft(lodIndex, previous.Distance, null, previous.ImportedDocument, previous.ImportSource, slots);
    }
    public void SetCollisionSurface(int index, Bounds bounds, int boneIndex, int contents, int surfaceFlags)
    {
        if ((uint)index >= (uint)_collisionSurfaces.Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (!float.IsFinite(bounds.MidPoint.X) || !float.IsFinite(bounds.MidPoint.Y) || !float.IsFinite(bounds.MidPoint.Z) || !float.IsFinite(bounds.HalfSize.X) || !float.IsFinite(bounds.HalfSize.Y) || !float.IsFinite(bounds.HalfSize.Z) || bounds.HalfSize.X < 0 || bounds.HalfSize.Y < 0 || bounds.HalfSize.Z < 0 || boneIndex < 0 || boneIndex >= Model.NumBones)
            throw new ArgumentOutOfRangeException(nameof(bounds));
        _collisionSurfaces[index] = new XModelCollSurf(CopyBounds(bounds), checked((ushort)boneIndex), contents, surfaceFlags);
    }
    public void SetPhysPreset(PhysPresetAsset? value) => PhysPreset = value;
    public void SetPhysCollmap(PhysCollmapAsset? value) => PhysCollmap = value;

    private void ValidateIndex(int index)
    {
        if ((uint)index >= (uint)_lodAssembly.Count) throw new ArgumentOutOfRangeException(nameof(index));
    }

    private static List<XModelLodDraft> CreateAssembly(XModelAsset model)
    {
        var result = new List<XModelLodDraft>(4);
        for (int i = 0; i < 4; i++)
        {
            XModelLodInfo? lod = i < model.NumLods ? model.Lods.ElementAtOrDefault(i) : null;
            result.Add(new XModelLodDraft(i, lod?.Dist ?? 0f, lod, null, null));
        }
        return result;
    }

    private IReadOnlyList<int?> CreateMaterialMappings(XModelExportDocument document)
    {
        return document.Materials.Select(imported =>
        {
            int[] matches = Model.Materials.Select((material, index) => (material, index))
                .Where(value => value.material is not null && string.Equals(value.material.Info.Name, imported.Name, StringComparison.Ordinal))
                .Select(value => value.index).ToArray();
            return matches.Length == 1 ? (int?)matches[0] : null;
        }).ToArray();
    }

    private static bool AssemblyEquals(IReadOnlyList<XModelLodDraft> left, byte leftCollision, IReadOnlyList<XModelLodDraft> right, byte rightCollision) =>
        leftCollision == rightCollision && left.Count == right.Count && left.Zip(right).All(pair =>
            pair.First.Distance.Equals(pair.Second.Distance) &&
            ReferenceEquals(pair.First.BaselineLod, pair.Second.BaselineLod) &&
            DocumentsEqual(pair.First.ImportedDocument, pair.Second.ImportedDocument) &&
            pair.First.MaterialSlots.SequenceEqual(pair.Second.MaterialSlots));

    private static bool DocumentsEqual(XModelExportDocument? left, XModelExportDocument? right) =>
        XModelExportDocumentsEqual(left, right);

    internal static bool XModelExportDocumentsEqual(XModelExportDocument? left, XModelExportDocument? right) =>
        ReferenceEquals(left, right) || left is not null && right is not null &&
        left.Bones.SequenceEqual(right.Bones) && left.Objects.SequenceEqual(right.Objects) && left.Materials.SequenceEqual(right.Materials) &&
        left.Triangles.SequenceEqual(right.Triangles) && left.Vertices.Count == right.Vertices.Count &&
        left.Vertices.Zip(right.Vertices).All(pair => pair.First.Position.Equals(pair.Second.Position) && pair.First.Weights.SequenceEqual(pair.Second.Weights));

    private static XModelAsset CopyRoot(
        XModelAsset value,
        IReadOnlyList<XModelCollSurf>? collisionSurfaces = null,
        PhysPresetAsset? physPreset = null,
        PhysCollmapAsset? physCollmap = null,
        bool useAuthoredPhysicsReferences = false)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new XModelAsset
        {
            Offset = value.Offset,
            RuntimeAddress = value.RuntimeAddress,
            NamePointer = value.NamePointer,
            Name = value.Name,
            NumBones = value.NumBones,
            NumRootBones = value.NumRootBones,
            NumSurfs = value.NumSurfs,
            SerializedNumSurfs = value.SerializedNumSurfs,
            LodRampType = value.LodRampType,
            Scale = value.Scale,
            NoScalePartBits = Copy(value.NoScalePartBits),
            BoneNamesPointer = value.BoneNamesPointer,
            BoneNames = Copy(value.BoneNames),
            ParentListPointer = value.ParentListPointer,
            ParentList = Copy(value.ParentList),
            QuatsPointer = value.QuatsPointer,
            Quats = Copy(value.Quats),
            TransPointer = value.TransPointer,
            Trans = Copy(value.Trans),
            PartClassificationPointer = value.PartClassificationPointer,
            PartClassification = Copy(value.PartClassification),
            BaseMatPointer = value.BaseMatPointer,
            BaseMat = Copy(value.BaseMat),
            MaterialHandlesPointer = value.MaterialHandlesPointer,
            MaterialPointers = Copy(value.MaterialPointers),
            Materials = Copy(value.Materials),
            Lods = Copy(value.Lods),
            MaxLoadedLod = value.MaxLoadedLod,
            NumLods = value.NumLods,
            CollLod = value.CollLod,
            Flags = value.Flags,
            CollSurfsPointer = value.CollSurfsPointer,
            NumCollSurfs = checked((byte)(collisionSurfaces ?? value.CollSurfs).Count),
            Contents = (collisionSurfaces ?? value.CollSurfs).Aggregate(0, (contents, row) => contents | row.Contents),
            CollSurfs = CopyCollSurfs(collisionSurfaces ?? value.CollSurfs),
            BoneInfoPointer = value.BoneInfoPointer,
            BoneInfo = CopyBoneInfo(value.BoneInfo),
            Radius = value.Radius,
            Bounds = CopyBounds(value.Bounds),
            InvHighMipRadiusPointer = value.InvHighMipRadiusPointer,
            InvHighMipRadius = Copy(value.InvHighMipRadius),
            MemUsage = value.MemUsage,
            PhysPresetPointer = useAuthoredPhysicsReferences
                ? default
                : value.PhysPresetPointer,
            PhysPreset = useAuthoredPhysicsReferences ? physPreset : value.PhysPreset,
            PhysCollmapPointer = useAuthoredPhysicsReferences
                ? default
                : value.PhysCollmapPointer,
            PhysCollmap = useAuthoredPhysicsReferences ? physCollmap : value.PhysCollmap
        };
    }

    private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values) =>
        Array.AsReadOnly(values.ToArray());

    private static IReadOnlyList<XModelCollSurf> CopyCollSurfs(
        IReadOnlyList<XModelCollSurf> values) =>
        Array.AsReadOnly(values.Select(value => new XModelCollSurf(
            CopyBounds(value.Bounds),
            value.BoneIndex,
            value.Contents,
            value.SurfaceFlags)).ToArray());

    private static IReadOnlyList<XBoneInfo> CopyBoneInfo(
        IReadOnlyList<XBoneInfo> values) =>
        Array.AsReadOnly(values.Select(value => new XBoneInfo(
            CopyBounds(value.Bounds),
            value.RadiusSquared)).ToArray());

    private static Bounds CopyBounds(Bounds value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Bounds
        {
            MidPoint = value.MidPoint,
            HalfSize = value.HalfSize
        };
    }

    private static bool CollSurfsEqual(IReadOnlyList<XModelCollSurf> left, IReadOnlyList<XModelCollSurf> right) =>
        left.Count == right.Count && left.Zip(right).All(pair =>
            pair.First.BoneIndex == pair.Second.BoneIndex &&
            pair.First.Contents == pair.Second.Contents &&
            pair.First.SurfaceFlags == pair.Second.SurfaceFlags &&
            pair.First.Bounds.MidPoint.Equals(pair.Second.Bounds.MidPoint) &&
            pair.First.Bounds.HalfSize.Equals(pair.Second.Bounds.HalfSize));
}

internal sealed class XModelAdapter : AssetAuthoringAdapter<XModelAsset, XModelDraft>
{
    public override XAssetType AssetType => XAssetType.XModel;

    public override XModelDraft CreateDraft(XModelAsset value) => new(value);

    public override XModelDraft CloneDraft(XModelDraft value) => value.Clone();

    public override XModelAsset CreateDefinition(XModelDraft value) => value.ToAsset();

    public override IReadOnlyList<AssetValidationIssue> Validate(XModelDraft value) =>
        XModelLodAssemblyValidator.Validate(value);

    public override bool SemanticallyEquals(XModelDraft left, XModelDraft right)
    {
        XModelAsset x = left.Model;
        XModelAsset y = right.Model;
        return x.Offset == y.Offset &&
            x.StagingAddress == y.StagingAddress &&
            x.RuntimeAddress == y.RuntimeAddress &&
            x.NamePointer == y.NamePointer &&
            string.Equals(x.Name, y.Name, StringComparison.Ordinal) &&
            x.NumBones == y.NumBones &&
            x.NumRootBones == y.NumRootBones &&
            x.NumSurfs == y.NumSurfs &&
            x.SerializedNumSurfs == y.SerializedNumSurfs &&
            x.LodRampType == y.LodRampType &&
            x.Scale.Equals(y.Scale) &&
            x.NoScalePartBits.SequenceEqual(y.NoScalePartBits) &&
            x.BoneNamesPointer == y.BoneNamesPointer &&
            x.BoneNames.SequenceEqual(y.BoneNames) &&
            x.ParentListPointer == y.ParentListPointer &&
            x.ParentList.SequenceEqual(y.ParentList) &&
            x.QuatsPointer == y.QuatsPointer &&
            x.Quats.SequenceEqual(y.Quats) &&
            x.TransPointer == y.TransPointer &&
            x.Trans.SequenceEqual(y.Trans) &&
            x.PartClassificationPointer == y.PartClassificationPointer &&
            x.PartClassification.SequenceEqual(y.PartClassification) &&
            x.BaseMatPointer == y.BaseMatPointer &&
            x.BaseMat.SequenceEqual(y.BaseMat) &&
            x.MaterialHandlesPointer == y.MaterialHandlesPointer &&
            x.MaterialPointers.SequenceEqual(y.MaterialPointers) &&
            x.Materials.SequenceEqual(y.Materials) &&
            x.Lods.SequenceEqual(y.Lods) &&
            x.MaxLoadedLod == y.MaxLoadedLod &&
            x.NumLods == y.NumLods &&
            x.CollLod == y.CollLod &&
            x.Flags == y.Flags &&
            x.CollSurfsPointer == y.CollSurfsPointer &&
            x.NumCollSurfs == y.NumCollSurfs &&
            x.Contents == y.Contents &&
            CollSurfsEqual(x.CollSurfs, y.CollSurfs) &&
            x.BoneInfoPointer == y.BoneInfoPointer &&
            BoneInfoEqual(x.BoneInfo, y.BoneInfo) &&
            x.Radius.Equals(y.Radius) &&
            BoundsEqual(x.Bounds, y.Bounds) &&
            x.InvHighMipRadiusPointer == y.InvHighMipRadiusPointer &&
            x.InvHighMipRadius.SequenceEqual(y.InvHighMipRadius) &&
            x.MemUsage == y.MemUsage &&
            x.PhysPresetPointer == y.PhysPresetPointer &&
            ReferenceEquals(x.PhysPreset, y.PhysPreset) &&
            x.PhysCollmapPointer == y.PhysCollmapPointer &&
            ReferenceEquals(x.PhysCollmap, y.PhysCollmap) &&
            AssemblyEqual(left, right) &&
            CollSurfsEqual(left.CollisionSurfaces, right.CollisionSurfaces) &&
            ReferenceEquals(left.PhysPreset, right.PhysPreset) &&
            ReferenceEquals(left.PhysCollmap, right.PhysCollmap);
    }

    private static bool AssemblyEqual(XModelDraft left, XModelDraft right) =>
        left.CollisionLod == right.CollisionLod &&
        left.LodAssembly.Count == right.LodAssembly.Count &&
        left.LodAssembly.Zip(right.LodAssembly).All(pair =>
            pair.First.Distance.Equals(pair.Second.Distance) &&
            ReferenceEquals(pair.First.BaselineLod, pair.Second.BaselineLod) &&
            DocumentsEqual(pair.First.ImportedDocument, pair.Second.ImportedDocument) &&
            pair.First.MaterialSlots.SequenceEqual(pair.Second.MaterialSlots));

    private static bool DocumentsEqual(XModelExportDocument? left, XModelExportDocument? right) =>
        XModelDraft.XModelExportDocumentsEqual(left, right);

    private static bool CollSurfsEqual(
        IReadOnlyList<XModelCollSurf> left,
        IReadOnlyList<XModelCollSurf> right)
    {
        if (left.Count != right.Count)
            return false;
        for (int index = 0; index < left.Count; index++)
        {
            XModelCollSurf x = left[index];
            XModelCollSurf y = right[index];
            if (x.BoneIndex != y.BoneIndex ||
                x.Contents != y.Contents ||
                x.SurfaceFlags != y.SurfaceFlags ||
                !BoundsEqual(x.Bounds, y.Bounds))
            {
                return false;
            }
        }

        return true;
    }

    private static bool BoneInfoEqual(
        IReadOnlyList<XBoneInfo> left,
        IReadOnlyList<XBoneInfo> right)
    {
        if (left.Count != right.Count)
            return false;
        for (int index = 0; index < left.Count; index++)
        {
            XBoneInfo x = left[index];
            XBoneInfo y = right[index];
            if (!x.RadiusSquared.Equals(y.RadiusSquared) ||
                !BoundsEqual(x.Bounds, y.Bounds))
            {
                return false;
            }
        }

        return true;
    }

    private static bool BoundsEqual(Bounds left, Bounds right) =>
        left.MidPoint.Equals(right.MidPoint) &&
        left.HalfSize.Equals(right.HalfSize);
}
