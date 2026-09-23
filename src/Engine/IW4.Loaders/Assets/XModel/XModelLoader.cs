using IW4.Loaders.Database;
using IW4.Loaders.Assets.Material;
using IW4.Loaders.Assets.Physics;
using IW4.Game.Assets;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.XModel;
using ModelBounds = IW4.Game.Math.Bounds;
using IW4.Game.Pointers;
using IW4.Game.ScriptStrings;
using IW4.Game.Zone;
using IW4.Runtime.Database;
using IW4.Game.IO;
using XModelAssetModel = IW4.Game.Assets.XModel.XModelAsset;
using XModelSurfsAssetModel = IW4.Game.Assets.XModel.XModelSurfsAsset;
using PhysPresetAssetModel = IW4.Game.Assets.Physics.PhysPresetAsset;
using PhysCollmapAssetModel = IW4.Game.Assets.Physics.PhysCollmapAsset;
using XString = IW4.Game.Pointers.XPointer<string>;
using static IW4.Loaders.Assets.XModel.XModelPayloadReader;

namespace IW4.Loaders.Assets.XModel;

public sealed class XModelLoader : XAssetLoader<XModelAssetModel>
{
    private const int XModelSize = 0x120;
    private const int XModelLodInfoSize = 0x28;
    private const int PhysCollmapSize = 0x48;

    private readonly MaterialLoader _materialLoader = new();
    private readonly PhysPresetLoader _physPresetLoader = new();
    private readonly PhysCollmapLoader _physCollmapLoader = new();

    public XModelLoader()
        : base(XAssetType.XModel, XModelSize, "XModel")
    {
    }

    protected override XModelAssetModel RegisterAsset(
        XModelAssetModel asset,
        ProviderRegistrationOccurrence providerRegistration,
        DbLoadExecutionContext context)
    {
        return context.DB_AddXAsset(asset, providerRegistration);
    }

    protected override XModelAssetModel ReadBody(
        FastFileCursor cursor,
        XBlockAddress targetAddress,
        DbLoadExecutionContext context)
    {
        int sourceOffset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(cursor, XModelSize, out XBlockAddress rootAddress);
        if (rootAddress != targetAddress)
            throw new InvalidDataException($"XModel pointer patched to {targetAddress}, but root loaded at {rootAddress}.");

        var rootCursor = new FastFileCursor(rootBytes, rootAddress);

        XString namePointer = ReadXStringPointer(rootCursor, context);
        byte numBones = rootCursor.ReadByte();
        byte numRootBones = rootCursor.ReadByte();
        byte numSurfs = rootCursor.ReadByte();
        XModelLodRampType lodRampType =
            (XModelLodRampType)rootCursor.ReadByte();
        float scale = rootCursor.ReadSingle();
        IReadOnlyList<uint> noScalePartBits = ReadUInt32Values(rootCursor, 6);
        XPointer<ushort[]> boneNamesPointer = ReadPointer<ushort[]>(rootCursor, context, XPointerResolutionMode.Direct);
        XPointer<byte[]> parentListPointer = ReadPointer<byte[]>(rootCursor, context, XPointerResolutionMode.Direct);
        XPointer<short[]> quatsPointer = ReadPointer<short[]>(rootCursor, context, XPointerResolutionMode.Direct);
        XPointer<float[]> transPointer = ReadPointer<float[]>(rootCursor, context, XPointerResolutionMode.Direct);
        XPointer<byte[]> partClassificationPointer = ReadPointer<byte[]>(rootCursor, context, XPointerResolutionMode.Direct);
        XPointer<byte[]> baseMatPointer = ReadPointer<byte[]>(rootCursor, context, XPointerResolutionMode.Direct);
        XPointer<XPointer<MaterialAsset>[]> materialHandlesPointer = ReadPointer<XPointer<MaterialAsset>[]>(rootCursor, context, XPointerResolutionMode.Direct);

        rootCursor.Skip(0xe0 - rootCursor.Offset);
        byte maxLoadedLod = rootCursor.ReadByte();
        byte numLods = rootCursor.ReadByte();
        byte collLod = rootCursor.ReadByte();
        XModelFlags flags = (XModelFlags)rootCursor.ReadByte();
        XPointer<byte[]> collSurfsPointer = ReadPointer<byte[]>(rootCursor, context, XPointerResolutionMode.Direct);
        int numCollSurfs = rootCursor.ReadInt32();
        int contents = rootCursor.ReadInt32();
        XPointer<byte[]> boneInfoPointer = ReadPointer<byte[]>(rootCursor, context, XPointerResolutionMode.Direct);
        float radius = rootCursor.ReadSingle();
        ModelBounds bounds = ReadBounds(rootCursor);
        XPointer<ushort[]> invHighMipRadiusPointer = ReadPointer<ushort[]>(rootCursor, context, XPointerResolutionMode.Direct);
        int memUsage = rootCursor.ReadInt32();
        XPointerReference physPresetPointer = ReadPointer<PhysPresetAssetModel>(rootCursor, context, XPointerResolutionMode.AliasCell).Untyped;
        XPointerReference physCollmapPointer = ReadPointer<PhysCollmapAssetModel>(rootCursor, context, XPointerResolutionMode.AliasCell).Untyped;

        if (rootCursor.Offset != XModelSize)
            throw new InvalidDataException($"XModel consumed 0x{rootCursor.Offset:X} bytes instead of 0x{XModelSize:X}.");

        int partCount = Math.Max(0, numBones - numRootBones);
        string? name;
        XModelAssetModel model;

        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            name = ReadXString(cursor, namePointer, context);
            IReadOnlyList<ScriptStringReference> boneNames = ReadScriptStringArray(
                cursor,
                boneNamesPointer.Untyped,
                numBones,
                "XModel.BoneNames",
                context);
            IReadOnlyList<byte> parentList = ReadByteArray(cursor, parentListPointer.Untyped, partCount, context, out _);
            IReadOnlyList<short> quats = ReadInt16Array(cursor, quatsPointer.Untyped, partCount * 4, context, out _);
            IReadOnlyList<float> trans = ReadFloatArray(cursor, transPointer.Untyped, partCount * 3, context, out _);
            IReadOnlyList<byte> partClassification = ReadByteArray(cursor, partClassificationPointer.Untyped, numBones, context, out _);
            IReadOnlyList<DObjAnimMat> baseMat = ReadDObjAnimMatArray(cursor, baseMatPointer.Untyped, numBones, context, out _);
            IReadOnlyList<XPointer<MaterialAsset>> materialPointers =
                ReadAliasPointerArrayPayload<MaterialAsset>(
                    cursor,
                    materialHandlesPointer.Untyped,
                    numSurfs,
                    context);
            IReadOnlyList<MaterialAsset?> materials =
                ReadMaterialPointers(
                    cursor,
                    materialPointers,
                    context);

            var lods = new XModelLodInfo[4];
            for (int i = 0; i < 4; i++)
            {
                int lodOffset = 0x40 + (i * XModelLodInfoSize);
                var lodCursor = new FastFileCursor(rootBytes.AsSpan(lodOffset, XModelLodInfoSize).ToArray(), rootAddress with { Offset = rootAddress.Offset + lodOffset });
                float dist = lodCursor.ReadSingle();
                ushort lodNumSurfs = lodCursor.ReadUInt16();
                ushort surfIndex = lodCursor.ReadUInt16();
                XPointerReference modelSurfsPointer = ReadPointer<XModelSurfsAssetModel>(lodCursor, context, XPointerResolutionMode.AliasCell).Untyped;
                var partBits = new uint[6];
                for (int partBitIndex = 0; partBitIndex < partBits.Length; partBitIndex++)
                    partBits[partBitIndex] = lodCursor.ReadUInt32();
                uint[] serializedPartBits = partBits.ToArray();
                int surfsRuntimeCellOffset = lodCursor.Offset;
                XPointer<byte[]> surfsRuntimePointer = XPointerReference.FromRaw(
                        lodCursor.ReadInt32(),
                        XPointerResolutionMode.Direct,
                        lodCursor.AddressAt(surfsRuntimeCellOffset))
                    .AsPointer<byte[]>();
                XModelSurfsAssetModel? modelSurfs = new XModelSurfsLoader(lodNumSurfs)
                    .LoadFromPointer(cursor, modelSurfsPointer, context);
                if (modelSurfs is not null)
                {
                    (partBits, surfsRuntimePointer) = CopyCanonicalXModelSurfsToLodInfo(
                        rootAddress.Add(lodOffset),
                        modelSurfs,
                        context);
                }
                lods[i] = new XModelLodInfo
                {
                    Dist = dist,
                    NumSurfs = lodNumSurfs,
                    SerializedNumSurfs = lodNumSurfs,
                    SurfIndex = surfIndex,
                    SerializedSurfIndex = surfIndex,
                    ModelSurfsPointer = modelSurfsPointer.AsPointer<XModelSurfsAssetModel>(),
                    PartBits = partBits,
                    SerializedPartBits = serializedPartBits,
                    SurfsRuntimePointer = surfsRuntimePointer,
                    ModelSurfs = modelSurfs
                };
            }

            IReadOnlyList<XModelCollSurf> collSurfs = ReadXModelCollSurfArray(cursor, collSurfsPointer.Untyped, numCollSurfs, context, out _);
            IReadOnlyList<XBoneInfo> boneInfo = ReadXBoneInfoArray(cursor, boneInfoPointer.Untyped, numBones, context, out _);
            IReadOnlyList<ushort> invHighMipRadius = ReadUInt16Array(cursor, invHighMipRadiusPointer.Untyped, numSurfs, context, out _);
            PhysPresetAssetModel? physPreset = _physPresetLoader.LoadFromPointer(
                cursor,
                physPresetPointer,
                context);
            PhysCollmapAssetModel? physCollmap = _physCollmapLoader.LoadFromPointer(
                cursor,
                physCollmapPointer,
                context);


            model = new XModelAssetModel
            {
                Offset = sourceOffset,
                RuntimeAddress = rootAddress,
                NamePointer = namePointer,
                Name = name,
                NumBones = numBones,
                NumRootBones = numRootBones,
                NumSurfs = numSurfs,
                SerializedNumSurfs = numSurfs,
                LodRampType = lodRampType,
                Scale = scale,
                NoScalePartBits = noScalePartBits,
                BoneNamesPointer = boneNamesPointer,
                BoneNames = boneNames,
                ParentListPointer = parentListPointer,
                ParentList = parentList,
                QuatsPointer = quatsPointer,
                Quats = quats,
                TransPointer = transPointer,
                Trans = trans,
                PartClassificationPointer = partClassificationPointer,
                PartClassification = partClassification,
                BaseMatPointer = baseMatPointer,
                BaseMat = baseMat,
                MaterialHandlesPointer = materialHandlesPointer,
                MaterialPointers = materialPointers,
                Materials = materials,
                Lods = lods,
                MaxLoadedLod = maxLoadedLod,
                NumLods = numLods,
                CollLod = collLod,
                Flags = flags,
                CollSurfsPointer = collSurfsPointer,
                NumCollSurfs = numCollSurfs,
                Contents = contents,
                CollSurfs = collSurfs,
                BoneInfoPointer = boneInfoPointer,
                BoneInfo = boneInfo,
                Radius = radius,
                Bounds = bounds,
                InvHighMipRadiusPointer = invHighMipRadiusPointer,
                InvHighMipRadius = invHighMipRadius,
                MemUsage = memUsage,
                PhysPresetPointer = physPresetPointer.AsPointer<PhysPresetAssetModel>(),
                PhysPreset = physPreset,
                PhysCollmapPointer = physCollmapPointer.AsPointer<PhysCollmapAssetModel>(),
                PhysCollmap = physCollmap
            };

        }
        finally
        {
            context.Blocks.Pop();
        }

        return model;
    }

    // Copy the canonical XModelSurfs partBits and surface pointer into the
    // owning XModelLodInfo.
    private static (uint[] PartBits, XPointer<byte[]> SurfsRuntimePointer) CopyCanonicalXModelSurfsToLodInfo(
        XBlockAddress lodInfoAddress,
        XModelSurfsAssetModel modelSurfs,
        DbLoadExecutionContext context)
    {
        if (!context.TryGetCanonicalXModelSurfsEntry(modelSurfs, out var entry))
            throw new InvalidDataException($"XModelSurfs '{modelSurfs.Name}' has no canonical XAsset-pool entry.");

        var canonicalHeader = new FastFileCursor(entry.HeaderBytes);
        canonicalHeader.Skip(0x04);
        int surfsRaw = canonicalHeader.ReadInt32();
        canonicalHeader.Skip(0x04);
        var partBits = new uint[6];
        for (int i = 0; i < partBits.Length; i++)
            partBits[i] = canonicalHeader.ReadUInt32();

        for (int i = 0; i < partBits.Length; i++)
            context.Blocks.WriteInt32(lodInfoAddress.Add(0x0c + (i * sizeof(uint))), unchecked((int)partBits[i]));
        XBlockAddress surfsCellAddress = lodInfoAddress.Add(0x24);
        context.Blocks.WriteInt32(surfsCellAddress, surfsRaw);

        return (
            partBits,
            new XPointer<byte[]>(surfsRaw, XPointerResolutionMode.Direct, surfsCellAddress));
    }

    private IReadOnlyList<MaterialAsset?> ReadMaterialPointers(
        FastFileCursor cursor,
        IReadOnlyList<XPointer<MaterialAsset>> pointers,
        DbLoadExecutionContext context)
    {
        var materials = new MaterialAsset?[pointers.Count];
        for (int i = 0; i < pointers.Count; i++)
        {
            materials[i] = _materialLoader.LoadFromPointer(
                cursor,
                pointers[i].Untyped,
                context);
        }

        return materials;
    }

    private static IReadOnlyList<XPointer<T>> ReadAliasPointerArrayPayload<T>(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative alias pointer array count {count}.");

        int byteCount = checked(count * sizeof(int));
        if (pointer.Type == PointerType.Null)
            return [];

        if (!context.PointerReader.HasInlinePayload(pointer))
        {
            context.PointerReader.ValidateOffsetPointerRange<XPointer<T>[]>(pointer, byteCount, $"{typeof(T).Name}*[]");
            return [];
        }

        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        byte[] pointerBytes = context.Blocks.Load(cursor, byteCount, out XBlockAddress tableAddress);
        var pointerCursor = new FastFileCursor(pointerBytes, tableAddress);
        var pointers = new XPointer<T>[count];

        for (int i = 0; i < pointers.Length; i++)
            pointers[i] = ReadPointer<T>(pointerCursor, context, XPointerResolutionMode.AliasCell);

        return pointers;
    }

    private static IReadOnlyList<byte> ReadByteArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        out XBlockAddress? runtimeAddress)
    {
        return ReadRawBytes(cursor, pointer, count, alignment: 1, context, out runtimeAddress);
    }

    private static IReadOnlyList<short> ReadInt16Array(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        out XBlockAddress? runtimeAddress)
    {
        return ReadInt16Values(ReadRawBytes(cursor, pointer, checked(count * sizeof(short)), alignment: 2, context, out runtimeAddress));
    }

    private static IReadOnlyList<ScriptStringReference> ReadScriptStringArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        string memberName,
        DbLoadExecutionContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative ScriptString array count {count}.");

        string serializedView = $"ScriptString[{count}]";
        IReadOnlyList<byte> bytes = ReadRawBytes(
            cursor,
            pointer,
            checked(count * sizeof(ushort)),
            alignment: 2,
            context,
            out XBlockAddress? runtimeAddress);
        if (bytes.Count == 0)
            return [];

        XBlockAddress arrayAddress = runtimeAddress
            ?? throw new InvalidDataException($"{memberName} has no materialized destination address.");
        if (pointer.Type == PointerType.Offset &&
            context.TryGetMaterializedView<ScriptStringReference[]>(
                arrayAddress,
                serializedView,
                out ScriptStringReference[]? existing) &&
            existing is not null)
        {
            return existing;
        }

        var valueCursor = new FastFileCursor(bytes.ToArray(), arrayAddress);
        var values = new ScriptStringReference[count];
        for (int index = 0; index < values.Length; index++)
        {
            ushort rawLocalIndex = valueCursor.ReadUInt16();
            XBlockAddress destinationCell = arrayAddress.Add(index * sizeof(ushort));
            ScriptStringReference resolved = context.ZoneScriptStrings.Resolve(
                rawLocalIndex,
                destinationCell,
                $"{memberName}[{index}]");
            context.Blocks.WriteUInt16(destinationCell, resolved.RuntimeHandle.Value);
            values[index] = resolved;
        }

        return context.RegisterMaterializedView(
            arrayAddress,
            serializedView,
            values,
            $"{memberName} ScriptString[]");
    }

    private static IReadOnlyList<float> ReadFloatArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        out XBlockAddress? runtimeAddress)
    {
        return ReadFloatValues(ReadRawBytes(cursor, pointer, checked(count * sizeof(float)), alignment: 4, context, out runtimeAddress));
    }

    private static IReadOnlyList<DObjAnimMat> ReadDObjAnimMatArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        out XBlockAddress? runtimeAddress)
    {
        IReadOnlyList<byte> bytes = ReadRawBytes(cursor, pointer, checked(count * DObjAnimMat.SerializedSize), alignment: 4, context, out runtimeAddress);
        if (bytes.Count == 0)
            return [];

        RequireExactByteCount(bytes, count, DObjAnimMat.SerializedSize, nameof(DObjAnimMat));
        var values = new DObjAnimMat[count];
        for (int i = 0; i < values.Length; i++)
            values[i] = ReadDObjAnimMat(bytes, i * DObjAnimMat.SerializedSize);

        return values;
    }

    private static DObjAnimMat ReadDObjAnimMat(IReadOnlyList<byte> bytes, int offset)
    {
        var cursor = new FastFileCursor(bytes.Skip(offset).Take(DObjAnimMat.SerializedSize).ToArray());
        return new DObjAnimMat(
            new DObjQuat(cursor.ReadSingle(), cursor.ReadSingle(), cursor.ReadSingle(), cursor.ReadSingle()),
            ReadVec3(cursor),
            cursor.ReadSingle());
    }

    private static IReadOnlyList<XModelCollSurf> ReadXModelCollSurfArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        out XBlockAddress? runtimeAddress)
    {
        IReadOnlyList<byte> bytes = ReadRawBytes(cursor, pointer, checked(count * XModelCollSurf.SerializedSize), alignment: 4, context, out runtimeAddress);
        if (bytes.Count == 0)
            return [];

        RequireExactByteCount(bytes, count, XModelCollSurf.SerializedSize, nameof(XModelCollSurf));
        var values = new XModelCollSurf[count];
        for (int i = 0; i < values.Length; i++)
            values[i] = ReadXModelCollSurf(bytes, i * XModelCollSurf.SerializedSize);

        return values;
    }

    private static XModelCollSurf ReadXModelCollSurf(IReadOnlyList<byte> bytes, int offset)
    {
        var cursor = new FastFileCursor(bytes.Skip(offset).Take(XModelCollSurf.SerializedSize).ToArray());
        return new XModelCollSurf(
            ReadBounds(cursor),
            cursor.ReadInt32(),
            cursor.ReadInt32(),
            cursor.ReadInt32());
    }

    private static IReadOnlyList<XBoneInfo> ReadXBoneInfoArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        out XBlockAddress? runtimeAddress)
    {
        IReadOnlyList<byte> bytes = ReadRawBytes(cursor, pointer, checked(count * XBoneInfo.SerializedSize), alignment: 4, context, out runtimeAddress);
        if (bytes.Count == 0)
            return [];

        RequireExactByteCount(bytes, count, XBoneInfo.SerializedSize, nameof(XBoneInfo));
        var values = new XBoneInfo[count];
        for (int i = 0; i < values.Length; i++)
            values[i] = ReadXBoneInfo(bytes, i * XBoneInfo.SerializedSize);

        return values;
    }

    private static XBoneInfo ReadXBoneInfo(IReadOnlyList<byte> bytes, int offset)
    {
        var cursor = new FastFileCursor(bytes.Skip(offset).Take(XBoneInfo.SerializedSize).ToArray());
        return new XBoneInfo(ReadBounds(cursor), cursor.ReadSingle());
    }

    private static IReadOnlyList<short> ReadInt16Values(IReadOnlyList<byte> bytes)
    {
        var values = ReadUInt16Values(bytes);
        return values.Select(value => unchecked((short)value)).ToArray();
    }

    private static IReadOnlyList<float> ReadFloatValues(IReadOnlyList<byte> bytes)
    {
        var cursor = new FastFileCursor(bytes.ToArray());
        var values = new float[bytes.Count / sizeof(float)];
        for (int i = 0; i < values.Length; i++)
            values[i] = cursor.ReadSingle();

        return values;
    }

    private static IReadOnlyList<uint> ReadUInt32Values(FastFileCursor cursor, int count)
    {
        var values = new uint[count];
        for (int i = 0; i < values.Length; i++)
            values[i] = cursor.ReadUInt32();

        return values;
    }


    private static ModelBounds ReadBounds(FastFileCursor cursor)
    {
        return new ModelBounds
        {
            MidPoint = ReadVec3(cursor),
            HalfSize = ReadVec3(cursor)
        };
    }

}
