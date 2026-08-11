using IW4.Assets.Assets;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Physics;
using IW4.Assets.Assets.XModel;
using IW4.Assets.Math;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Strings;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Plans;

/// <summary>
/// Frozen XModel provider. Wire scalars retained before DB canonicalization
/// are used for imported definitions; authored definitions use their semantic
/// fields directly.
/// </summary>
internal sealed class XModelLinkPlan : AssetLinkPlan
{
    private XModelLinkPlan(
        AssetKey key,
        string originalSerializedName,
        XModelAsset definition,
        LinkAssetFreezeScope freeze)
        : base(
            key,
            originalSerializedName,
            freeze.FreezeProviderName(originalSerializedName, 0, "Asset.Name"))
    {
        Root = CreateOwnedRoot(definition, freeze);
    }

    internal override LinkStorageSymbol Root { get; }

    public static AssetLinkPlan Freeze(
        AssetKey key,
        string originalSerializedName,
        XModelAsset definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (originalSerializedName.StartsWith(','))
        {
            ValidateReferenceShape(definition);
            return ExternalAssetLinkPlan.Create(
                key,
                XAssetType.XModel,
                originalSerializedName,
                freeze);
        }

        return new XModelLinkPlan(
            key,
            originalSerializedName,
            definition,
            freeze);
    }

    private static void ValidateReferenceShape(XModelAsset definition)
    {
        if (!IsZeroReference(definition))
        {
            throw new InvalidDataException(
                "A comma-prefixed XModel provider must have a zeroed reference body.");
        }
    }

    private LinkStorageSymbol CreateOwnedRoot(
        XModelAsset definition,
        LinkAssetFreezeScope freeze)
    {
        bool imported = definition.SerializedNumSurfs.HasValue;
        byte numSurfs = imported
            ? definition.SerializedNumSurfs!.Value
            : definition.NumSurfs;
        if (definition.NumRootBones > definition.NumBones)
            throw new InvalidDataException("XModel.NumRootBones cannot exceed NumBones.");
        if (definition.NoScalePartBits.Count != 6)
            throw new InvalidDataException("XModel.NoScalePartBits requires exactly six words.");
        if (definition.Lods.Count != 4)
            throw new InvalidDataException("XModel requires exactly four serialized LOD rows.");
        if (definition.NumLods > 4)
            throw new InvalidDataException("XModel.NumLods cannot exceed four.");
        if ((definition.MaxLoadedLod > 3 && definition.MaxLoadedLod != byte.MaxValue) ||
            (definition.CollLod > 3 && definition.CollLod != byte.MaxValue))
        {
            throw new InvalidDataException(
                "XModel LOD indices must address one of four slots or use the 0xFF sentinel.");
        }

        int partCount = definition.NumBones - definition.NumRootBones;
        RequireCount(definition.BoneNames, definition.NumBones, "XModel.BoneNames");
        RequireCount(definition.ParentList, partCount, "XModel.ParentList");
        RequireCount(definition.Quats, checked(partCount * 4), "XModel.Quats");
        RequireCount(definition.Trans, checked(partCount * 3), "XModel.Trans");
        RequireCount(definition.PartClassification, definition.NumBones, "XModel.PartClassification");
        RequireCount(definition.BaseMat, definition.NumBones, "XModel.BaseMat");
        RequireCount(definition.Materials, numSurfs, "XModel.Materials");
        if (definition.MaterialPointers.Count is not 0 &&
            definition.MaterialPointers.Count != numSurfs)
        {
            throw new InvalidDataException(
                "XModel.MaterialPointers must be absent or match the serialized surface count.");
        }
        RequireCount(definition.BoneInfo, definition.NumBones, "XModel.BoneInfo");
        RequireCount(definition.InvHighMipRadius, numSurfs, "XModel.InvHighMipRadius");
        if (definition.NumCollSurfs < 0 || definition.CollSurfs.Count != definition.NumCollSurfs)
            throw new InvalidDataException("XModel.CollSurfs count must equal nonnegative NumCollSurfs.");
        if (definition.ParentList.Any(parent => parent >= definition.NumBones))
            throw new InvalidDataException("XModel.ParentList contains an out-of-range bone index.");

        LinkStorageTarget? boneNames = ResolveBoneNames(definition, freeze);
        LinkStorageTarget? parents = ResolveBytes(
            definition.ParentListPointer.Untyped,
            definition.ParentList,
            XFileBlockType.LARGE,
            alignment: 1,
            freeze,
            "XModel.ParentList");
        LinkStorageTarget? quats = ResolveInt16s(
            definition.QuatsPointer.Untyped,
            definition.Quats,
            freeze,
            "XModel.Quats");
        LinkStorageTarget? trans = ResolveSingles(
            definition.TransPointer.Untyped,
            definition.Trans,
            freeze,
            "XModel.Trans");
        LinkStorageTarget? classifications = ResolveBytes(
            definition.PartClassificationPointer.Untyped,
            definition.PartClassification,
            XFileBlockType.LARGE,
            alignment: 1,
            freeze,
            "XModel.PartClassification");
        LinkStorageTarget? baseMat = ResolveBaseMat(definition, freeze);
        LinkStorageTarget? materials = ResolveMaterialTable(definition, numSurfs, freeze);
        LodWire[] lods = FreezeLods(definition, imported);
        LinkStorageTarget? collSurfs = ResolveCollSurfs(definition, freeze);
        LinkStorageTarget? boneInfo = ResolveBoneInfo(definition, freeze);
        LinkStorageTarget? invHigh = ResolveUInt16s(
            definition.InvHighMipRadiusPointer.Untyped,
            definition.InvHighMipRadius,
            freeze,
            "XModel.InvHighMipRadius");
        AssetDependency? physPreset = FreezeProviderDependency(
            definition.PhysPresetPointer.Untyped,
            definition.PhysPreset,
            XAssetType.PhysPreset,
            "XModel.PhysPreset");
        AssetDependency? physCollmap = FreezeProviderDependency(
            definition.PhysCollmapPointer.Untyped,
            definition.PhysCollmap,
            XAssetType.PhysCollmap,
            "XModel.PhysCollmap");

        LinkStorageSymbol? vertexReservation = numSurfs == 0
            ? null
            : LinkStorageSymbol.SourceFree(
                XFileBlockType.VERTEX,
                checked(numSurfs * sizeof(uint)),
                alignment: sizeof(uint),
                LinkMaterializationKind.VertexReservation);
        byte[] rootBytes = BuildRootBytes(
            definition,
            numSurfs,
            lods);
        return LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            rootBytes,
            alignment: 4,
            root => RootOperations(
                root,
                vertexReservation,
                boneNames,
                parents,
                quats,
                trans,
                classifications,
                baseMat,
                materials,
                lods,
                collSurfs,
                boneInfo,
                invHigh,
                physPreset,
                physCollmap));
    }

    private IEnumerable<LinkOperation> RootOperations(
        LinkStorageSymbol root,
        LinkStorageSymbol? vertexReservation,
        LinkStorageTarget? boneNames,
        LinkStorageTarget? parents,
        LinkStorageTarget? quats,
        LinkStorageTarget? trans,
        LinkStorageTarget? classifications,
        LinkStorageTarget? baseMat,
        LinkStorageTarget? materials,
        IReadOnlyList<LodWire> lods,
        LinkStorageTarget? collSurfs,
        LinkStorageTarget? boneInfo,
        LinkStorageTarget? invHigh,
        AssetDependency? physPreset,
        AssetDependency? physCollmap)
    {
        if (vertexReservation is not null)
        {
            yield return new MaterializeStorageLinkOperation(
                vertexReservation,
                "XModel.RuntimeSurfacePairs");
        }
        yield return NameOperation(root, 0);
        foreach (LinkOperation operation in OrderedDirectOperations(
            root,
            (0x24, boneNames, "XModel.BoneNames"),
            (0x28, parents, "XModel.ParentList"),
            (0x2c, quats, "XModel.Quats"),
            (0x30, trans, "XModel.Trans"),
            (0x34, classifications, "XModel.PartClassification"),
            (0x38, baseMat, "XModel.BaseMat"),
            (0x3c, materials, "XModel.Materials")))
        {
            yield return operation;
        }

        for (int index = 0; index < lods.Count; index++)
        {
            if (lods[index].ModelSurfs is { } dependency)
            {
                yield return ProviderOperation(
                    root,
                    checked(0x48 + index * XModelLodInfo.SerializedSize),
                    dependency);
            }
        }
        foreach (LinkOperation operation in OrderedDirectOperations(
            root,
            (0xe4, collSurfs, "XModel.CollSurfs"),
            (0xf0, boneInfo, "XModel.BoneInfo"),
            (0x110, invHigh, "XModel.InvHighMipRadius")))
        {
            yield return operation;
        }
        if (physPreset is { } preset)
            yield return ProviderOperation(root, 0x118, preset);
        if (physCollmap is { } collmap)
            yield return ProviderOperation(root, 0x11c, collmap);
    }

    private static byte[] BuildRootBytes(
        XModelAsset definition,
        byte numSurfs,
        IReadOnlyList<LodWire> lods)
    {
        var writer = new LinkTemplateWriter(XModelAsset.SerializedSize);
        writer.Skip(sizeof(int));
        writer.WriteByte(definition.NumBones);
        writer.WriteByte(definition.NumRootBones);
        writer.WriteByte(numSurfs);
        writer.WriteByte(definition.Pad07);
        WriteSingle(writer, definition.Scale, "XModel.Scale");
        foreach (uint value in definition.NoScalePartBits)
            writer.WriteUInt32(value);
        for (int index = 0; index < 7; index++)
            writer.Skip(sizeof(int));
        foreach (LodWire lod in lods)
        {
            WriteSingle(writer, lod.Dist, "XModel.Lod.Dist");
            writer.WriteUInt16(lod.NumSurfs);
            writer.WriteUInt16(lod.SurfIndex);
            writer.Skip(sizeof(int));
            foreach (uint value in lod.PartBits)
                writer.WriteUInt32(value);
            writer.WriteInt32(0);
        }
        writer.WriteByte(definition.MaxLoadedLod);
        writer.WriteByte(definition.NumLods);
        writer.WriteByte(definition.CollLod);
        writer.WriteByte(definition.Flags);
        writer.Skip(sizeof(int));
        writer.WriteInt32(definition.NumCollSurfs);
        writer.WriteInt32(definition.Contents);
        writer.Skip(sizeof(int));
        WriteSingle(writer, definition.Radius, "XModel.Radius");
        WriteBounds(writer, definition.Bounds, "XModel.Bounds");
        writer.Skip(sizeof(int));
        writer.WriteInt32(definition.MemUsage);
        writer.Skip(sizeof(int));
        writer.Skip(sizeof(int));
        return writer.Complete();
    }

    private static LodWire[] FreezeLods(XModelAsset definition, bool imported)
    {
        var result = new LodWire[definition.Lods.Count];
        for (int index = 0; index < definition.Lods.Count; index++)
        {
            XModelLodInfo lod = definition.Lods[index] ?? throw new InvalidDataException(
                $"XModel.Lods[{index}] cannot be null.");
            IReadOnlyList<uint> partBits;
            ushort numSurfs;
            ushort surfIndex;
            if (imported)
            {
                numSurfs = lod.SerializedNumSurfs ?? throw new NotSupportedException(
                    $"Imported XModel LOD {index} has no retained pre-canonical NumSurfs.");
                surfIndex = lod.SerializedSurfIndex ?? throw new NotSupportedException(
                    $"Imported XModel LOD {index} has no retained pre-canonical SurfIndex.");
                partBits = lod.SerializedPartBits ?? throw new NotSupportedException(
                    $"Imported XModel LOD {index} has no retained pre-canonical PartBits.");
            }
            else
            {
                numSurfs = lod.NumSurfs;
                surfIndex = lod.SurfIndex;
                partBits = lod.PartBits;
            }
            if (partBits.Count != 6)
                throw new InvalidDataException($"XModel.Lods[{index}].PartBits requires six words.");
            AssetDependency? modelSurfs = FreezeProviderDependency(
                lod.ModelSurfsPointer.Untyped,
                lod.ModelSurfs,
                XAssetType.XModelSurfs,
                $"XModel.Lods[{index}].ModelSurfs");
            bool hasSurfaceTable = lod.ModelSurfs is { } retained &&
                (retained.Surfaces.Count != 0 ||
                 retained.SurfsPointer.Type != PointerType.Null);
            if (hasSurfaceTable && numSurfs != lod.ModelSurfs!.Surfaces.Count)
            {
                throw new NotSupportedException(
                    $"XModel.Lods[{index}] wire surface count does not equal its retained XModelSurfs rows.");
            }
            result[index] = new LodWire(
                lod.Dist,
                numSurfs,
                surfIndex,
                partBits.ToArray(),
                modelSurfs);
        }
        return result;
    }

    private static LinkStorageTarget? ResolveBoneNames(
        XModelAsset definition,
        LinkAssetFreezeScope freeze)
    {
        XPointerReference pointer = definition.BoneNamesPointer.Untyped;
        int byteCount = checked(definition.BoneNames.Count * sizeof(ushort));
        if (definition.BoneNames.Count == 0)
        {
            if (pointer.Type != PointerType.Null)
                throw new NotSupportedException("XModel.BoneNames cannot preserve present-empty storage.");
            return null;
        }
        if (pointer.Type != PointerType.Offset)
            RequireAuthoredOrInline(pointer, "XModel.BoneNames");
        foreach ((ScriptStringReference value, int index) in
            definition.BoneNames.Select((value, index) => (value, index)))
        {
            if (value is null)
                throw new InvalidDataException($"XModel.BoneNames[{index}] cannot be null.");
        }

        byte[] bytes = new byte[byteCount];
        Func<LinkStorageSymbol, int, IEnumerable<LinkOperation>> operations =
            (owner, addend) => definition.BoneNames.Select((value, index) =>
                new ScriptStringLinkOperation(
                    new LinkStorageCell(
                        owner,
                        checked(addend + index * sizeof(ushort))),
                    value,
                    $"XModel.BoneNames[{index}]"));
        return pointer.Type == PointerType.Offset
            ? freeze.FreezeStorageRange(
                pointer,
                bytes,
                XFileBlockType.LARGE,
                alignment: 2,
                operations,
                "XModel.BoneNames")
            : freeze.FreezeStorage(
                pointer,
                bytes,
                XFileBlockType.LARGE,
                alignment: 2,
                operations,
                "XModel.BoneNames");
    }

    private static LinkStorageTarget? ResolveMaterialTable(
        XModelAsset definition,
        int numSurfs,
        LinkAssetFreezeScope freeze)
    {
        XPointerReference pointer = definition.MaterialHandlesPointer.Untyped;
        int byteCount = checked(numSurfs * sizeof(int));
        if (numSurfs == 0)
        {
            if (pointer.Type != PointerType.Null)
                throw new NotSupportedException("XModel.Materials cannot preserve present-empty storage.");
            return null;
        }
        if (pointer.Type != PointerType.Offset)
            RequireAuthoredOrInline(pointer, "XModel.Materials");
        AssetDependency?[] dependencies = definition.Materials
            .Select((material, index) => FreezeProviderDependency(
                definition.MaterialPointers.Count == 0
                    ? default
                    : definition.MaterialPointers[index].Untyped,
                material,
                XAssetType.Material,
                $"XModel.Materials[{index}]"))
            .ToArray();
        byte[] bytes = new byte[byteCount];
        Func<LinkStorageSymbol, int, IEnumerable<LinkOperation>> operations =
            (owner, addend) => dependencies
                .Select((dependency, index) => (dependency, index))
                .Where(item => item.dependency is not null)
                .Select(item => new ProviderLinkOperation(
                    new LinkStorageCell(
                        owner,
                        checked(addend + item.index * sizeof(int))),
                    item.dependency!.Value));
        return pointer.Type == PointerType.Offset
            ? freeze.FreezeStorageRange(
                pointer,
                bytes,
                XFileBlockType.LARGE,
                alignment: 4,
                operations,
                "XModel.Materials")
            : freeze.FreezeStorage(
                pointer,
                bytes,
                XFileBlockType.LARGE,
                alignment: 4,
                operations,
                "XModel.Materials");
    }

    private static LinkStorageTarget? ResolveBaseMat(
        XModelAsset definition,
        LinkAssetFreezeScope freeze)
    {
        var writer = new LinkTemplateWriter(
            checked(definition.BaseMat.Count * DObjAnimMat.SerializedSize));
        for (int index = 0; index < definition.BaseMat.Count; index++)
        {
            DObjAnimMat value = definition.BaseMat[index] ?? throw new InvalidDataException(
                $"XModel.BaseMat[{index}] cannot be null.");
            WriteSingle(writer, value.Quat.X, $"XModel.BaseMat[{index}].Quat.X");
            WriteSingle(writer, value.Quat.Y, $"XModel.BaseMat[{index}].Quat.Y");
            WriteSingle(writer, value.Quat.Z, $"XModel.BaseMat[{index}].Quat.Z");
            WriteSingle(writer, value.Quat.W, $"XModel.BaseMat[{index}].Quat.W");
            WriteVec3(writer, value.Trans, $"XModel.BaseMat[{index}].Trans");
            WriteSingle(writer, value.TransWeight, $"XModel.BaseMat[{index}].TransWeight");
        }
        return ResolveSerialized(
            definition.BaseMatPointer.Untyped,
            writer.Complete(),
            alignment: 4,
            freeze,
            "XModel.BaseMat");
    }

    private static LinkStorageTarget? ResolveCollSurfs(
        XModelAsset definition,
        LinkAssetFreezeScope freeze)
    {
        var writer = new LinkTemplateWriter(
            checked(definition.CollSurfs.Count * XModelCollSurf.SerializedSize));
        for (int index = 0; index < definition.CollSurfs.Count; index++)
        {
            XModelCollSurf value = definition.CollSurfs[index] ?? throw new InvalidDataException(
                $"XModel.CollSurfs[{index}] cannot be null.");
            if (value.BoneIndex < 0 || value.BoneIndex >= definition.NumBones)
                throw new InvalidDataException($"XModel.CollSurfs[{index}].BoneIndex is out of range.");
            WriteBounds(writer, value.Bounds, $"XModel.CollSurfs[{index}].Bounds");
            writer.WriteInt32(value.BoneIndex);
            writer.WriteInt32(value.Contents);
            writer.WriteInt32(value.SurfaceFlags);
        }
        return ResolveSerialized(
            definition.CollSurfsPointer.Untyped,
            writer.Complete(),
            alignment: 4,
            freeze,
            "XModel.CollSurfs");
    }

    private static LinkStorageTarget? ResolveBoneInfo(
        XModelAsset definition,
        LinkAssetFreezeScope freeze)
    {
        var writer = new LinkTemplateWriter(
            checked(definition.BoneInfo.Count * XBoneInfo.SerializedSize));
        for (int index = 0; index < definition.BoneInfo.Count; index++)
        {
            XBoneInfo value = definition.BoneInfo[index] ?? throw new InvalidDataException(
                $"XModel.BoneInfo[{index}] cannot be null.");
            WriteBounds(writer, value.Bounds, $"XModel.BoneInfo[{index}].Bounds");
            WriteSingle(writer, value.RadiusSquared, $"XModel.BoneInfo[{index}].RadiusSquared");
        }
        return ResolveSerialized(
            definition.BoneInfoPointer.Untyped,
            writer.Complete(),
            alignment: 4,
            freeze,
            "XModel.BoneInfo");
    }

    private static LinkStorageTarget? ResolveBytes(
        XPointerReference pointer,
        IReadOnlyList<byte> values,
        XFileBlockType block,
        int alignment,
        LinkAssetFreezeScope freeze,
        string fieldPath) =>
        ResolveSerialized(
            pointer,
            values.ToArray(),
            alignment,
            freeze,
            fieldPath,
            block);

    private static LinkStorageTarget? ResolveInt16s(
        XPointerReference pointer,
        IReadOnlyList<short> values,
        LinkAssetFreezeScope freeze,
        string fieldPath)
    {
        var writer = new LinkTemplateWriter(checked(values.Count * sizeof(short)));
        foreach (short value in values)
            writer.WriteUInt16(unchecked((ushort)value));
        return ResolveSerialized(pointer, writer.Complete(), 2, freeze, fieldPath);
    }

    private static LinkStorageTarget? ResolveUInt16s(
        XPointerReference pointer,
        IReadOnlyList<ushort> values,
        LinkAssetFreezeScope freeze,
        string fieldPath)
    {
        var writer = new LinkTemplateWriter(checked(values.Count * sizeof(ushort)));
        foreach (ushort value in values)
            writer.WriteUInt16(value);
        return ResolveSerialized(pointer, writer.Complete(), 2, freeze, fieldPath);
    }

    private static LinkStorageTarget? ResolveSingles(
        XPointerReference pointer,
        IReadOnlyList<float> values,
        LinkAssetFreezeScope freeze,
        string fieldPath)
    {
        var writer = new LinkTemplateWriter(checked(values.Count * sizeof(float)));
        for (int index = 0; index < values.Count; index++)
            WriteSingle(writer, values[index], $"{fieldPath}[{index}]");
        return ResolveSerialized(pointer, writer.Complete(), 4, freeze, fieldPath);
    }

    private static LinkStorageTarget? ResolveSerialized(
        XPointerReference pointer,
        byte[] bytes,
        int alignment,
        LinkAssetFreezeScope freeze,
        string fieldPath,
        XFileBlockType block = XFileBlockType.LARGE)
    {
        if (bytes.Length == 0)
        {
            if (pointer.Type != PointerType.Null)
                throw new NotSupportedException($"{fieldPath} cannot preserve present-empty storage.");
            return null;
        }
        if (pointer.Type != PointerType.Offset)
            RequireAuthoredOrInline(pointer, fieldPath);
        return pointer.Type == PointerType.Offset
            ? freeze.FreezeStorageRange(
                pointer,
                bytes,
                block,
                alignment,
                operations: null,
                fieldPath)
            : freeze.FreezeStorage(
                pointer,
                bytes,
                block,
                alignment,
                operations: null,
                fieldPath);
    }

    private static IEnumerable<LinkOperation> OrderedDirectOperations(
        LinkStorageSymbol owner,
        params (int Offset, LinkStorageTarget? Target, string Path)[] values)
    {
        foreach ((int offset, LinkStorageTarget? target, string path) in values)
        {
            if (target is { } value)
            {
                yield return new DirectStorageLinkOperation(
                    new LinkStorageCell(owner, offset),
                    value.View,
                    value.CanMaterializeRoot,
                    path);
            }
        }
    }

    private static void RequireAuthoredOrInline(
        XPointerReference pointer,
        string fieldPath)
    {
        if (pointer.Type is not (PointerType.Null or PointerType.Inline))
            throw new NotSupportedException($"{fieldPath} uses unsupported source form {pointer.Type}.");
    }

    private static void RequireCount<T>(
        IReadOnlyList<T> values,
        int expected,
        string fieldPath)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count != expected)
            throw new InvalidDataException($"{fieldPath} requires exactly {expected} values.");
    }

    private static void WriteBounds(
        LinkTemplateWriter writer,
        Bounds bounds,
        string fieldPath)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        WriteVec3(writer, bounds.MidPoint, $"{fieldPath}.MidPoint");
        WriteVec3(writer, bounds.HalfSize, $"{fieldPath}.HalfSize");
    }

    private static void WriteVec3(
        LinkTemplateWriter writer,
        Vec3 value,
        string fieldPath)
    {
        WriteSingle(writer, value.X, $"{fieldPath}.X");
        WriteSingle(writer, value.Y, $"{fieldPath}.Y");
        WriteSingle(writer, value.Z, $"{fieldPath}.Z");
    }

    private static void WriteSingle(
        LinkTemplateWriter writer,
        float value,
        string fieldPath)
    {
        if (!float.IsFinite(value))
            throw new InvalidDataException($"{fieldPath} must be finite.");
        writer.WriteInt32(BitConverter.SingleToInt32Bits(value));
    }

    private static bool IsZeroReference(XModelAsset definition)
    {
        bool zeroLods = definition.Lods.Count is 0 or 4 && definition.Lods.All(lod =>
            BitConverter.SingleToInt32Bits(lod.Dist) == 0 &&
            lod.NumSurfs == 0 && lod.SurfIndex == 0 &&
            lod.PartBits.All(value => value == 0) &&
            lod.PartBits.Count is 0 or 6 &&
            lod.ModelSurfsPointer.Type == PointerType.Null &&
            lod.ModelSurfs is null);
        return definition.NumBones == 0 && definition.NumRootBones == 0 &&
            definition.NumSurfs == 0 && definition.Pad07 == 0 &&
            BitConverter.SingleToInt32Bits(definition.Scale) == 0 &&
            definition.NoScalePartBits.All(value => value == 0) &&
            definition.NoScalePartBits.Count is 0 or 6 &&
            definition.BoneNames.Count == 0 && definition.ParentList.Count == 0 &&
            definition.Quats.Count == 0 && definition.Trans.Count == 0 &&
            definition.PartClassification.Count == 0 && definition.BaseMat.Count == 0 &&
            definition.MaterialHandlesPointer.Type == PointerType.Null &&
            definition.MaterialPointers.All(pointer => pointer.Type == PointerType.Null) &&
            definition.Materials.Count == 0 && zeroLods &&
            definition.MaxLoadedLod == 0 && definition.NumLods == 0 &&
            definition.CollLod == 0 && definition.Flags == 0 &&
            definition.NumCollSurfs == 0 && definition.Contents == 0 &&
            definition.CollSurfs.Count == 0 && definition.BoneInfo.Count == 0 &&
            BitConverter.SingleToInt32Bits(definition.Radius) == 0 &&
            IsZero(definition.Bounds.MidPoint) && IsZero(definition.Bounds.HalfSize) &&
            definition.InvHighMipRadius.Count == 0 && definition.MemUsage == 0 &&
            definition.PhysPresetPointer.Type == PointerType.Null &&
            definition.PhysPreset is null &&
            definition.PhysCollmapPointer.Type == PointerType.Null &&
            definition.PhysCollmap is null;
    }

    private static bool IsZero(Vec3 value) =>
        BitConverter.SingleToInt32Bits(value.X) == 0 &&
        BitConverter.SingleToInt32Bits(value.Y) == 0 &&
        BitConverter.SingleToInt32Bits(value.Z) == 0;

    private readonly record struct LodWire(
        float Dist,
        ushort NumSurfs,
        ushort SurfIndex,
        IReadOnlyList<uint> PartBits,
        AssetDependency? ModelSurfs);
}
