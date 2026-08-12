using IW4.Assets.Assets.XModel;
using IW4.Assets.Math;
using IW4.FastFiles.Zone;

namespace IW4.Studio.Documents;

public sealed class XModelDraft
{
    internal XModelDraft(XModelAsset value) => Model = CopyRoot(value);

    public XModelAsset Model { get; }

    internal XModelDraft Clone() => new(Model);

    internal XModelAsset ToAsset() => CopyRoot(Model);

    private static XModelAsset CopyRoot(XModelAsset value)
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
            Pad07 = value.Pad07,
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
            NumCollSurfs = value.NumCollSurfs,
            Contents = value.Contents,
            CollSurfs = CopyCollSurfs(value.CollSurfs),
            BoneInfoPointer = value.BoneInfoPointer,
            BoneInfo = CopyBoneInfo(value.BoneInfo),
            Radius = value.Radius,
            Bounds = CopyBounds(value.Bounds),
            InvHighMipRadiusPointer = value.InvHighMipRadiusPointer,
            InvHighMipRadius = Copy(value.InvHighMipRadius),
            MemUsage = value.MemUsage,
            PhysPresetPointer = value.PhysPresetPointer,
            PhysPreset = value.PhysPreset,
            PhysCollmapPointer = value.PhysCollmapPointer,
            PhysCollmap = value.PhysCollmap
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
}

internal sealed class XModelAdapter : AssetAuthoringAdapter<XModelAsset, XModelDraft>
{
    public override XAssetType AssetType => XAssetType.XModel;

    public override XModelDraft CreateDraft(XModelAsset value) => new(value);

    public override XModelDraft CloneDraft(XModelDraft value) => value.Clone();

    public override XModelAsset CreateDefinition(XModelDraft value) => value.ToAsset();

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
            x.Pad07 == y.Pad07 &&
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
            ReferenceEquals(x.PhysCollmap, y.PhysCollmap);
    }

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
