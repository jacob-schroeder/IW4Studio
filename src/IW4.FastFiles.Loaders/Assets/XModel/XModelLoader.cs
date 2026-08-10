using IW4.FastFiles.Loaders.Database;
using IW4.FastFiles.Loaders.Assets.Material;
using IW4.FastFiles.Loaders.Assets.Physics;
using IW4.Assets.Assets;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using ModelBounds = IW4.Assets.Math.Bounds;
using ModelVec3 = IW4.Assets.Math.Vec3;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;
using XModelAssetModel = IW4.Assets.Assets.XModel.XModelAsset;
using XModelSurfsAssetModel = IW4.Assets.Assets.XModel.XModelSurfsAsset;
using PhysPresetAssetModel = IW4.Assets.Assets.Physics.PhysPresetAsset;
using PhysCollmapAssetModel = IW4.Assets.Assets.Physics.PhysCollmapAsset;
using XString = IW4.FastFiles.Pointers.XPointer<string>;

namespace IW4.FastFiles.Loaders.Assets.XModel;

/// <summary>
/// Result of one nested XModel pointer wrapper.  <see cref="Canonical"/> is
/// the asset selected by DB_AddXAsset; <see cref="IncomingDefinition"/> is
/// the serialized body that was consumed from this pointer, when the source
/// form was inline/insert.  Keeping both prevents callers that own a nested
/// definition from accidentally authoring a dependency's canonical body.
/// </summary>
public sealed record XModelPointerLoadResult(
    XModelAssetModel? Canonical,
    XModelAssetModel? IncomingDefinition);

public sealed record XModelSurfsPointerLoadResult(
    XModelSurfsAssetModel? Canonical,
    XModelSurfsAssetModel? IncomingDefinition);

public sealed class XModelLoader
{
    private const int XModelSize = 0x120;
    private const int XModelLodInfoSize = 0x28;
    private const int XModelSurfsSize = 0x24;
    private const int XSurfaceSize = 0x54;
    private const int XRigidVertListSize = 0x0c;
    private const int XSurfaceCollisionTreeSize = 0x28;
    private const int PhysCollmapSize = 0x48;

    private readonly MaterialLoader _materialLoader = new();
    private readonly PhysPresetLoader _physPresetLoader = new();
    private readonly PhysCollmapLoader _physCollmapLoader = new();

    public XModelAssetModel LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        return LoadFromPointerCore(cursor, pointer, context, requireAsset: true).Canonical
            ?? throw new InvalidDataException("Top-level XModel pointer resolved to null.");
    }

    public XModelAssetModel? LoadFromPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        return LoadFromPointerCore(cursor, pointer, context, requireAsset: false).Canonical;
    }

    public XModelPointerLoadResult LoadFromPointerWithMaterialization(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context) =>
        LoadFromPointerCore(cursor, pointer, context, requireAsset: false);

    private XModelPointerLoadResult LoadFromPointerCore(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context,
        bool requireAsset)
    {
        if (pointer.Type == PointerType.Null)
        {
            if (requireAsset)
                throw new InvalidDataException("Top-level XModel pointer is null.");

            return new XModelPointerLoadResult(null, null);
        }

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<XModelAssetModel>(pointer, XModelSize, "XModel");
            XModelAssetModel? canonical = context.ResolveCanonicalAsset<XModelAssetModel>(
                pointer,
                XAssetType.XModel);
            if (canonical is null)
            {
                throw new InvalidDataException(
                    $"XModel pointer 0x{unchecked((uint)pointer.Raw):X8} does not resolve to a canonical XModel asset.");
            }

            PatchCanonicalPointerCell(pointer, canonical, context);
            return new XModelPointerLoadResult(canonical, null);
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
        {
            throw new InvalidDataException(
                $"XModel pointer 0x{unchecked((uint)pointer.Raw):X8} uses unsupported source sentinel {pointer.Type}.");
        }

        return LoadInlineOrInsertXModel(cursor, pointer, context);
    }

    private XModelPointerLoadResult LoadInlineOrInsertXModel(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        ProviderRegistrationOccurrence providerRegistration = context.BeginProviderRegistration(pointer);

        return ReadInlineXModel(cursor, pointer, providerRegistration, context);
    }

    private XModelPointerLoadResult ReadInlineXModel(
        FastFileCursor cursor,
        XPointerReference pointer,
        ProviderRegistrationOccurrence providerRegistration,
        DbLoadExecutionContext context)
    {
        int sourceOffset = cursor.Offset;
        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            XBlockAddress targetAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            byte[] rootBytes = context.Blocks.Load(cursor, XModelSize, out XBlockAddress rootAddress);
            if (rootAddress != targetAddress)
                throw new InvalidDataException($"XModel pointer patched to {targetAddress}, but root loaded at {rootAddress}.");

            var rootCursor = new FastFileCursor(rootBytes, rootAddress);

            XString namePointer = ReadXStringPointer(rootCursor, context);
            byte numBones = rootCursor.ReadByte();
            byte numRootBones = rootCursor.ReadByte();
            byte numSurfs = rootCursor.ReadByte();
            byte pad07 = rootCursor.ReadByte();
            float scale = ReadSingle(rootCursor);
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
            byte flags = rootCursor.ReadByte();
            XPointer<byte[]> collSurfsPointer = ReadPointer<byte[]>(rootCursor, context, XPointerResolutionMode.Direct);
            int numCollSurfs = rootCursor.ReadInt32();
            int contents = rootCursor.ReadInt32();
            XPointer<byte[]> boneInfoPointer = ReadPointer<byte[]>(rootCursor, context, XPointerResolutionMode.Direct);
            float radius = ReadSingle(rootCursor);
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
                IReadOnlyList<ushort> boneNames = ReadUInt16Array(cursor, boneNamesPointer.Untyped, numBones, context, out XBlockAddress? boneNamesAddress);
                IReadOnlyList<byte> parentList = ReadByteArray(cursor, parentListPointer.Untyped, partCount, context, out XBlockAddress? parentListAddress);
                IReadOnlyList<short> quats = ReadInt16Array(cursor, quatsPointer.Untyped, partCount * 4, context, out XBlockAddress? quatsAddress);
                IReadOnlyList<float> trans = ReadFloatArray(cursor, transPointer.Untyped, partCount * 3, context, out XBlockAddress? transAddress);
                IReadOnlyList<byte> partClassification = ReadByteArray(cursor, partClassificationPointer.Untyped, numBones, context, out XBlockAddress? partClassificationAddress);
                IReadOnlyList<DObjAnimMat> baseMat = ReadDObjAnimMatArray(cursor, baseMatPointer.Untyped, numBones, context, out XBlockAddress? baseMatAddress);
                IReadOnlyList<XPointer<MaterialAsset>> materialPointers =
                    ReadAliasPointerArrayPayload<MaterialAsset>(cursor, materialHandlesPointer.Untyped, numSurfs, context);
                IReadOnlyList<MaterialAsset?> materials =
                    ReadMaterialPointers(
                        cursor,
                        materialPointers,
                        context,
                        out IReadOnlyList<MaterialAsset?> materialIncomingDefinitions);

                var lods = new XModelLodInfo[4];
                for (int i = 0; i < 4; i++)
                {
                    int lodOffset = 0x40 + (i * XModelLodInfoSize);
                    var lodCursor = new FastFileCursor(rootBytes.AsSpan(lodOffset, XModelLodInfoSize).ToArray(), rootAddress with { Offset = rootAddress.Offset + lodOffset });
                    float dist = ReadSingle(lodCursor);
                    ushort lodNumSurfs = lodCursor.ReadUInt16();
                    ushort surfIndex = lodCursor.ReadUInt16();
                    XPointerReference modelSurfsPointer = ReadPointer<XModelSurfsAssetModel>(lodCursor, context, XPointerResolutionMode.AliasCell).Untyped;
                    var partBits = new uint[6];
                    for (int partBitIndex = 0; partBitIndex < partBits.Length; partBitIndex++)
                        partBits[partBitIndex] = lodCursor.ReadUInt32();
                    IReadOnlyList<uint> serializedPartBits =
                        Array.AsReadOnly(partBits.ToArray());
                    int surfsRuntimeCellOffset = lodCursor.Offset;
                    XPointer<byte[]> surfsRuntimePointer = XPointerReference.FromRaw(
                            lodCursor.ReadInt32(),
                            XPointerResolutionMode.Direct,
                            lodCursor.AddressAt(surfsRuntimeCellOffset))
                        .AsPointer<byte[]>();
                    XModelSurfsPointerLoadResult modelSurfsLoad =
                        Load_XModelSurfsPtr(cursor, modelSurfsPointer, lodNumSurfs, context);
                    XModelSurfsAssetModel? modelSurfs = modelSurfsLoad.Canonical;
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
                        ModelSurfs = modelSurfs,
                        ModelSurfsIncomingDefinition = modelSurfsLoad.IncomingDefinition
                    };
                }

                IReadOnlyList<XModelCollSurf> collSurfs = ReadXModelCollSurfArray(cursor, collSurfsPointer.Untyped, numCollSurfs, context, out _);
                IReadOnlyList<XBoneInfo> boneInfo = ReadXBoneInfoArray(cursor, boneInfoPointer.Untyped, numBones, context, out _);
                IReadOnlyList<ushort> invHighMipRadius = ReadUInt16Array(cursor, invHighMipRadiusPointer.Untyped, numSurfs, context, out _);
                PhysPresetPointerLoadResult physPresetLoad =
                    _physPresetLoader.LoadFromPointerWithMaterialization(
                        cursor,
                        physPresetPointer,
                        context);
                PhysCollmapPointerLoadResult physCollmapLoad =
                    _physCollmapLoader.LoadFromPointerWithMaterialization(
                        cursor,
                        physCollmapPointer,
                        context);
                PhysPresetAssetModel? physPreset = physPresetLoad.Canonical;
                PhysCollmapAssetModel? physCollmap = physCollmapLoad.Canonical;


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
                    Pad07 = pad07,
                    Scale = scale,
                    NoScalePartBits = noScalePartBits,
                    BoneNamesPointer = boneNamesPointer,
                    BoneNamesRuntimeAddress = boneNamesAddress,
                    BoneNames = boneNames,
                    ParentListPointer = parentListPointer,
                    ParentListRuntimeAddress = parentListAddress,
                    ParentList = parentList,
                    QuatsPointer = quatsPointer,
                    QuatsRuntimeAddress = quatsAddress,
                    Quats = quats,
                    TransPointer = transPointer,
                    TransRuntimeAddress = transAddress,
                    Trans = trans,
                    PartClassificationPointer = partClassificationPointer,
                    PartClassificationRuntimeAddress = partClassificationAddress,
                    PartClassification = partClassification,
                    BaseMatPointer = baseMatPointer,
                    BaseMatRuntimeAddress = baseMatAddress,
                    BaseMat = baseMat,
                    MaterialHandlesPointer = materialHandlesPointer,
                    MaterialPointers = materialPointers,
                    Materials = materials,
                    MaterialIncomingDefinitions = materialIncomingDefinitions,
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
                    PhysPresetIncomingDefinition = physPresetLoad.IncomingDefinition,
                    PhysCollmapPointer = physCollmapPointer.AsPointer<PhysCollmapAssetModel>(),
                    PhysCollmap = physCollmap,
                    PhysCollmapIncomingDefinition = physCollmapLoad.IncomingDefinition
                };

            }
            finally
            {
                context.Blocks.Pop();
            }

            XModelAssetModel canonical = context.DB_AddXAsset(model, providerRegistration);

            return new XModelPointerLoadResult(canonical, model);
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    // Load an XModelSurfs pointer and register inline payloads canonically.
    private XModelSurfsPointerLoadResult Load_XModelSurfsPtr(
        FastFileCursor cursor,
        XPointerReference pointer,
        ushort lodNumSurfs,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            return new XModelSurfsPointerLoadResult(null, null);

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<XModelSurfsAssetModel>(pointer, XModelSurfsSize, "XModelSurfs");
            XModelSurfsAssetModel? canonical = context.ResolveCanonicalAsset<XModelSurfsAssetModel>(
                pointer,
                XAssetType.XModelSurfs);
            if (canonical is null)
            {
                throw new InvalidDataException(
                    $"XModelSurfs pointer 0x{unchecked((uint)pointer.Raw):X8} does not resolve to a canonical XModelSurfs asset.");
            }

            PatchCanonicalPointerCell(pointer, canonical, context);
            return new XModelSurfsPointerLoadResult(canonical, null);
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
        {
            throw new InvalidDataException(
                $"XModelSurfs pointer 0x{unchecked((uint)pointer.Raw):X8} uses unsupported source sentinel {pointer.Type}.");
        }

        ProviderRegistrationOccurrence providerRegistration = context.BeginProviderRegistration(pointer);

        int sourceOffset = cursor.Offset;
        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            XBlockAddress rootAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            XModelSurfsAssetModel modelSurfs = Load_XModelSurfs(
                cursor,
                rootAddress,
                lodNumSurfs,
                sourceOffset,
                context);
            XModelSurfsAssetModel canonical = context.DB_AddXAsset(
                XAssetType.XModelSurfs,
                modelSurfs.Name,
                modelSurfs,
                providerRegistration);

            return new XModelSurfsPointerLoadResult(canonical, modelSurfs);
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    // Copy only the 0x24-byte header into TEMP; the XSurface array and its
    // children are materialized in LARGE.
    private XModelSurfsAssetModel Load_XModelSurfs(
        FastFileCursor cursor,
        XBlockAddress rootAddress,
        ushort lodNumSurfs,
        int sourceOffset,
        DbLoadExecutionContext context)
    {
        byte[] rootBytes = context.Blocks.Load(cursor, XModelSurfsSize, out XBlockAddress loadedAddress);
        if (loadedAddress != rootAddress)
            throw new InvalidDataException($"XModelSurfs pointer patched to {rootAddress}, but Load_Stream wrote its root at {loadedAddress}.");

        var rootCursor = new FastFileCursor(rootBytes, rootAddress);
        XString namePointer = ReadXStringPointer(rootCursor, context);
        XPointer<byte[]> surfsPointer = ReadPointer<byte[]>(rootCursor, context, XPointerResolutionMode.Direct);
        ushort numSurfs = rootCursor.ReadUInt16();
        ushort pad0A = rootCursor.ReadUInt16();
        var partBits = new uint[6];
        for (int i = 0; i < partBits.Length; i++)
            partBits[i] = rootCursor.ReadUInt32();

        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            string? name = ReadXString(cursor, namePointer, context);
            // Take the XSurface count from the owning XModelLodInfo. The count
            // stored in XModelSurfs is used later by the canonical XModel
            // fixup; it is not this body's load count and cannot support a
            // context-free top-level route.
            IReadOnlyList<XSurface> surfaces = ReadXSurfaceArray(
                cursor,
                surfsPointer.Untyped,
                lodNumSurfs,
                context);

            return new XModelSurfsAssetModel
            {
                Offset = sourceOffset,
                RuntimeAddress = rootAddress,
                NamePointer = namePointer,
                Name = name,
                SurfsPointer = surfsPointer,
                NumSurfs = numSurfs,
                Pad0A = pad0A,
                PartBits = partBits,
                Surfaces = surfaces
            };
        }
        finally
        {
            context.Blocks.Pop();
        }
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

    private IReadOnlyList<XSurface> ReadXSurfaceArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count <= 0 || pointer.Type == PointerType.Null)
            return [];

        if (!context.PointerReader.HasInlinePayload(pointer))
        {
            context.PointerReader.ValidateOffsetPointerRange<XSurface[]>(pointer, checked(count * XSurfaceSize), "XSurface[]");
            return [];
        }

        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        byte[] surfaceBytes = context.Blocks.Load(cursor, checked(count * XSurfaceSize), out XBlockAddress arrayAddress);
        var surfaces = new XSurface[count];

        for (int i = 0; i < count; i++)
        {
            int offset = i * XSurfaceSize;
            var surfaceCursor = new FastFileCursor(surfaceBytes.AsSpan(offset, XSurfaceSize).ToArray(), arrayAddress with { Offset = arrayAddress.Offset + offset });
            surfaces[i] = ReadXSurfaceChildren(cursor, surfaceCursor, context);
        }

        return surfaces;
    }

    private XSurface ReadXSurfaceChildren(
        FastFileCursor cursor,
        FastFileCursor surfaceCursor,
        DbLoadExecutionContext context)
    {
        ushort flagsOrPad00 = surfaceCursor.ReadUInt16();
        byte streamFlags = surfaceCursor.ReadByte();
        byte pad03 = surfaceCursor.ReadByte();
        ushort vertCount = surfaceCursor.ReadUInt16();
        ushort triCount = surfaceCursor.ReadUInt16();
        XPointer<ushort[]> triIndicesPointer = ReadPointer<ushort[]>(surfaceCursor, context, XPointerResolutionMode.Direct);
        ushort blend0 = surfaceCursor.ReadUInt16();
        ushort blend1 = surfaceCursor.ReadUInt16();
        ushort blend2 = surfaceCursor.ReadUInt16();
        ushort blend3 = surfaceCursor.ReadUInt16();
        XPointer<ushort[]> vertsBlendPointer = ReadPointer<ushort[]>(surfaceCursor, context, XPointerResolutionMode.Direct);
        XPointer<byte[]> verts0Pointer = ReadPointer<byte[]>(surfaceCursor, context, XPointerResolutionMode.Direct);
        GfxVertexBuffer vb0 = ReadGfxVertexBuffer(surfaceCursor);
        XPointer<byte[]> verts1Pointer = ReadPointer<byte[]>(surfaceCursor, context, XPointerResolutionMode.Direct);
        GfxVertexBuffer vb1 = ReadGfxVertexBuffer(surfaceCursor);
        int vertListCount = surfaceCursor.ReadInt32();
        XPointer<XRigidVertList[]> vertListPointer = ReadPointer<XRigidVertList[]>(surfaceCursor, context, XPointerResolutionMode.Direct);
        GfxIndexBuffer indexBuffer = ReadGfxIndexBuffer(surfaceCursor);
        var partBits = new uint[6];
        for (int i = 0; i < partBits.Length; i++)
            partBits[i] = surfaceCursor.ReadUInt32();

        int blendCount = blend0 + (blend1 * 3) + (blend2 * 5) + (blend3 * 7);

        IReadOnlyList<ushort> vertsBlend = ReadUInt16Array(cursor, vertsBlendPointer.Untyped, blendCount, context, out XBlockAddress? vertsBlendAddress);
        IReadOnlyList<byte> verts0 = ReadSurfaceStreamBytes(cursor, verts0Pointer.Untyped, checked(vertCount * 0x10), alignment: 16, pushPhysical: (streamFlags & 0x01) == 0, context, out XBlockAddress? verts0Address);
        IReadOnlyList<byte> verts1 = ReadSurfaceStreamBytes(cursor, verts1Pointer.Untyped, checked(vertCount * 0x10), alignment: 16, pushPhysical: (streamFlags & 0x02) == 0, context, out XBlockAddress? verts1Address);
        IReadOnlyList<XRigidVertList> vertList = ReadRigidVertListArray(cursor, vertListPointer.Untyped, vertListCount, context);
        IReadOnlyList<ushort> triIndices = ReadSurfaceStreamUshorts(cursor, triIndicesPointer.Untyped, checked(triCount * 3), alignment: 16, pushPhysical: (streamFlags & 0x04) == 0, context, out XBlockAddress? triIndicesAddress);

        return new XSurface
        {
            FlagsOrPad00 = flagsOrPad00,
            StreamFlags = streamFlags,
            Pad03 = pad03,
            VertCount = vertCount,
            TriCount = triCount,
            TriIndicesPointer = triIndicesPointer,
            TriIndicesRuntimeAddress = triIndicesAddress,
            TriIndices = triIndices,
            VertexInfo = new XSurfaceVertexInfo
            {
                Blend0 = blend0,
                Blend1 = blend1,
                Blend2 = blend2,
                Blend3 = blend3,
                VertsBlendPointer = vertsBlendPointer,
                VertsBlendRuntimeAddress = vertsBlendAddress,
                VertsBlend = vertsBlend
            },
            Verts0Pointer = verts0Pointer,
            Verts0RuntimeAddress = verts0Address,
            Verts0 = verts0,
            Vb0 = vb0,
            Verts1Pointer = verts1Pointer,
            Verts1RuntimeAddress = verts1Address,
            Verts1 = verts1,
            Vb1 = vb1,
            VertListCount = vertListCount,
            VertListPointer = vertListPointer,
            VertList = vertList,
            IndexBuffer = indexBuffer,
            PartBits = partBits
        };
    }

    private static GfxVertexBuffer ReadGfxVertexBuffer(FastFileCursor cursor)
    {
        return new GfxVertexBuffer
        {
            StreamSource = cursor.ReadInt32(),
            DataOffset = cursor.ReadInt32()
        };
    }

    private static GfxIndexBuffer ReadGfxIndexBuffer(FastFileCursor cursor)
    {
        return new GfxIndexBuffer
        {
            DataOffset = cursor.ReadInt32()
        };
    }

    private IReadOnlyList<XRigidVertList> ReadRigidVertListArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count <= 0 || pointer.Type == PointerType.Null)
            return [];

        if (!context.PointerReader.HasInlinePayload(pointer))
        {
            context.PointerReader.ValidateOffsetPointerRange<XRigidVertList[]>(pointer, checked(count * XRigidVertListSize), "XRigidVertList[]");
            return [];
        }

        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        byte[] listBytes = context.Blocks.Load(cursor, checked(count * XRigidVertListSize), out XBlockAddress listAddress);
        var lists = new XRigidVertList[count];
        for (int i = 0; i < count; i++)
        {
            int offset = i * XRigidVertListSize;
            var listCursor = new FastFileCursor(listBytes.AsSpan(offset, XRigidVertListSize).ToArray(), listAddress with { Offset = listAddress.Offset + offset });
            ushort boneOffset = listCursor.ReadUInt16();
            ushort vertCount = listCursor.ReadUInt16();
            ushort triOffset = listCursor.ReadUInt16();
            ushort triCount = listCursor.ReadUInt16();
            XPointer<XSurfaceCollisionTree> collisionTreePointer = ReadPointer<XSurfaceCollisionTree>(listCursor, context, XPointerResolutionMode.Direct);
            lists[i] = new XRigidVertList
            {
                BoneOffset = boneOffset,
                VertCount = vertCount,
                TriOffset = triOffset,
                TriCount = triCount,
                CollisionTreePointer = collisionTreePointer,
                CollisionTree = ReadXSurfaceCollisionTree(cursor, collisionTreePointer.Untyped, context)
            };
        }

        return lists;
    }

    private XSurfaceCollisionTree? ReadXSurfaceCollisionTree(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            return null;

        if (!context.PointerReader.HasInlinePayload(pointer))
        {
            context.PointerReader.ValidateOffsetPointerRange<XSurfaceCollisionTree>(pointer, XSurfaceCollisionTreeSize, "XSurfaceCollisionTree");
            return null;
        }

        XBlockAddress runtimeAddress =
            context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        byte[] treeBytes = context.Blocks.Load(cursor, XSurfaceCollisionTreeSize, out XBlockAddress treeAddress);
        var treeCursor = new FastFileCursor(treeBytes, treeAddress);
        ModelVec3 trans = ReadVec3(treeCursor);
        ModelVec3 scale = ReadVec3(treeCursor);
        int nodeCount = treeCursor.ReadInt32();
        XPointer<XSurfaceCollisionNode[]> nodesPointer = ReadPointer<XSurfaceCollisionNode[]>(treeCursor, context, XPointerResolutionMode.Direct);
        int leafCount = treeCursor.ReadInt32();
        XPointer<XSurfaceCollisionLeaf[]> leafsPointer = ReadPointer<XSurfaceCollisionLeaf[]>(treeCursor, context, XPointerResolutionMode.Direct);

        IReadOnlyList<XSurfaceCollisionNode> nodes =
            ReadCollisionNodeArray(cursor, nodesPointer.Untyped, nodeCount, context, out XBlockAddress? nodesAddress);
        IReadOnlyList<XSurfaceCollisionLeaf> leafs =
            ReadCollisionLeafArray(cursor, leafsPointer.Untyped, leafCount, context, out XBlockAddress? leafsAddress);

        return new XSurfaceCollisionTree
        {
            RuntimeAddress = runtimeAddress,
            Trans = trans,
            Scale = scale,
            NodeCount = nodeCount,
            NodesPointer = nodesPointer,
            NodesRuntimeAddress = nodesAddress,
            Nodes = nodes,
            LeafCount = leafCount,
            LeafsPointer = leafsPointer,
            LeafsRuntimeAddress = leafsAddress,
            Leafs = leafs
        };
    }

    private IReadOnlyList<MaterialAsset?> ReadMaterialPointers(
        FastFileCursor cursor,
        IReadOnlyList<XPointer<MaterialAsset>> pointers,
        DbLoadExecutionContext context,
        out IReadOnlyList<MaterialAsset?> incomingDefinitions)
    {
        var materials = new MaterialAsset?[pointers.Count];
        var incoming = new MaterialAsset?[pointers.Count];
        for (int i = 0; i < pointers.Count; i++)
        {
            materials[i] = _materialLoader.LoadFromPointer(
                cursor,
                pointers[i].Untyped,
                context,
                out incoming[i]);
        }

        incomingDefinitions = Array.AsReadOnly(incoming);
        return materials;
    }

    private IReadOnlyList<byte> ReadSurfaceStreamBytes(
        FastFileCursor cursor,
        XPointerReference pointer,
        int byteCount,
        int alignment,
        bool pushPhysical,
        DbLoadExecutionContext context,
        out XBlockAddress? runtimeAddress)
    {
        if (!pushPhysical)
        {
            return ReadRawBytes(cursor, pointer, byteCount, alignment, context, out runtimeAddress);
        }

        context.Blocks.Push(XFileBlockType.PHYSICAL);
        try
        {
            return ReadRawBytes(cursor, pointer, byteCount, alignment, context, out runtimeAddress);
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    private IReadOnlyList<ushort> ReadSurfaceStreamUshorts(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        int alignment,
        bool pushPhysical,
        DbLoadExecutionContext context,
        out XBlockAddress? runtimeAddress)
    {
        IReadOnlyList<byte> bytes = ReadSurfaceStreamBytes(
            cursor,
            pointer,
            checked(count * sizeof(ushort)),
            alignment,
            pushPhysical,
            context,
            out runtimeAddress);

        return ReadUInt16Values(bytes);
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

    private static IReadOnlyList<ushort> ReadUInt16Array(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        out XBlockAddress? runtimeAddress)
    {
        return ReadUInt16Values(ReadRawBytes(cursor, pointer, checked(count * sizeof(ushort)), alignment: 2, context, out runtimeAddress));
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
            new DObjQuat(ReadSingle(cursor), ReadSingle(cursor), ReadSingle(cursor), ReadSingle(cursor)),
            ReadVec3(cursor),
            ReadSingle(cursor));
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
        return new XBoneInfo(ReadBounds(cursor), ReadSingle(cursor));
    }

    private static IReadOnlyList<XSurfaceCollisionNode> ReadCollisionNodeArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        out XBlockAddress? runtimeAddress)
    {
        IReadOnlyList<byte> bytes = ReadRawBytes(cursor, pointer, checked(count * XSurfaceCollisionNode.SerializedSize), alignment: 16, context, out runtimeAddress);
        if (bytes.Count == 0)
            return [];

        RequireExactByteCount(bytes, count, XSurfaceCollisionNode.SerializedSize, nameof(XSurfaceCollisionNode));
        var values = new XSurfaceCollisionNode[count];
        for (int i = 0; i < values.Length; i++)
            values[i] = ReadXSurfaceCollisionNode(bytes, i * XSurfaceCollisionNode.SerializedSize);

        return values;
    }

    private static XSurfaceCollisionNode ReadXSurfaceCollisionNode(IReadOnlyList<byte> bytes, int offset)
    {
        var cursor = new FastFileCursor(bytes.Skip(offset).Take(XSurfaceCollisionNode.SerializedSize).ToArray());
        var aabb = new XSurfaceCollisionAabb(
            cursor.ReadUInt16(),
            cursor.ReadUInt16(),
            cursor.ReadUInt16(),
            cursor.ReadUInt16(),
            cursor.ReadUInt16(),
            cursor.ReadUInt16());

        return new XSurfaceCollisionNode(aabb, cursor.ReadUInt16(), cursor.ReadUInt16());
    }

    private static IReadOnlyList<XSurfaceCollisionLeaf> ReadCollisionLeafArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        out XBlockAddress? runtimeAddress)
    {
        return ReadUInt16Array(cursor, pointer, count, context, out runtimeAddress)
            .Select(value => new XSurfaceCollisionLeaf(value))
            .ToArray();
    }

    private static IReadOnlyList<byte> ReadRawBytes(
        FastFileCursor cursor,
        XPointerReference pointer,
        int byteCount,
        int alignment,
        DbLoadExecutionContext context,
        out XBlockAddress? runtimeAddress)
    {
        if (byteCount < 0)
            throw new InvalidDataException($"Invalid negative byte count {byteCount}.");

        if (pointer.Type == PointerType.Null)
        {
            runtimeAddress = null;
            return [];
        }

        if (!context.PointerReader.HasInlinePayload(pointer))
        {
            context.PointerReader.ValidateOffsetPointerRange<byte[]>(pointer, byteCount, "byte[]");
            if (pointer.PackedAddress is { } address)
            {
                runtimeAddress = address;
                return context.Blocks.ReadBytes(address, byteCount);
            }

            runtimeAddress = null;
            return [];
        }

        runtimeAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment);
        return context.Blocks.Load(cursor, byteCount);
    }

    private static void RequireExactByteCount(IReadOnlyList<byte> bytes, int count, int stride, string rowName)
    {
        int expected = checked(count * stride);
        if (bytes.Count != expected)
            throw new InvalidDataException($"{rowName} array expected 0x{expected:X} byte(s), got 0x{bytes.Count:X}.");
    }

    private static IReadOnlyList<ushort> ReadUInt16Values(IReadOnlyList<byte> bytes)
    {
        var cursor = new FastFileCursor(bytes.ToArray());
        var values = new ushort[bytes.Count / sizeof(ushort)];
        for (int i = 0; i < values.Length; i++)
            values[i] = cursor.ReadUInt16();

        return values;
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
            values[i] = BitConverter.Int32BitsToSingle(cursor.ReadInt32());

        return values;
    }

    private static IReadOnlyList<uint> ReadUInt32Values(FastFileCursor cursor, int count)
    {
        var values = new uint[count];
        for (int i = 0; i < values.Length; i++)
            values[i] = cursor.ReadUInt32();

        return values;
    }

    private static float ReadSingle(FastFileCursor cursor)
    {
        return BitConverter.Int32BitsToSingle(cursor.ReadInt32());
    }

    private static ModelBounds ReadBounds(FastFileCursor cursor)
    {
        return new ModelBounds
        {
            MidPoint = ReadVec3(cursor),
            HalfSize = ReadVec3(cursor)
        };
    }

    private static ModelVec3 ReadVec3(FastFileCursor cursor)
    {
        return new ModelVec3
        {
            X = ReadSingle(cursor),
            Y = ReadSingle(cursor),
            Z = ReadSingle(cursor)
        };
    }

    private static void PatchCanonicalPointerCell(
        XPointerReference pointer,
        BaseAsset canonical,
        DbLoadExecutionContext context)
    {
        XBlockAddress destinationCell = pointer.CellAddress
            ?? throw new InvalidDataException(
                $"Packed {canonical.GetType().Name} pointer has no destination cell.");

        int canonicalRaw = canonical.RuntimeAddress?.RawValue
            ?? throw new InvalidDataException($"Canonical {canonical.GetType().Name} has no runtime address.");
        context.Blocks.WriteInt32(destinationCell, canonicalRaw);
    }

    private static string? ReadXString(
        FastFileCursor cursor,
        XString pointer,
        DbLoadExecutionContext context)
    {
        return context.PointerReader.LoadXString(cursor, pointer);
    }

    private static XPointer<T> ReadPointer<T>(
        FastFileCursor cursor,
        DbLoadExecutionContext context,
        XPointerResolutionMode mode) => context.PointerReader.ReadDeferredPointer<T>(cursor, mode);

    private static XString ReadXStringPointer(FastFileCursor cursor, DbLoadExecutionContext context) =>
        ReadPointer<string>(cursor, context, XPointerResolutionMode.Direct);
}
