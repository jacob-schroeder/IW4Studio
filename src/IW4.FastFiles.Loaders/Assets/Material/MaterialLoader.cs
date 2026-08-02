using IW4.FastFiles.Loaders.Database;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Loaders.Assets.Image;
using IW4.FastFiles.Loaders.Assets.TechniqueSet;
using IW4.Runtime.Database;
using IW4.Runtime.IO;
using MaterialAssetModel = IW4.Assets.Assets.Material.MaterialAsset;

namespace IW4.FastFiles.Loaders.Assets.Material;

public sealed class MaterialLoader
{
    private const int MaterialSize = 0xa8;
    private const int TechniqueSlotCount = 37;
    private const int TechniqueSetSize = 0x9c;
    private const int TextureDefSize = 0x0c;
    private const int ConstantDefSize = 0x20;
    private const int GfxStateBitsSize = 0x08;
    private const int WaterSize = 0x48;
    private static readonly GfxImageLoader ImageLoader = new();

    public MaterialAssetModel LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Offset)
        {
            ResolveAliasCellOffset(pointer, context, MaterialSize, "Material");
            context.PointerReader.ValidateOffsetPointerRange<MaterialAssetModel>(
                pointer,
                MaterialSize,
                XPointerNullability.Required,
                "Material");
            return context.ResolveMaterial(pointer)
                ?? throw new InvalidDataException(
                    $"Top-level Material pointer 0x{unchecked((uint)pointer.Raw):X8} does not resolve to a canonical Material asset.");
        }

        if (!context.PointerReader.HasInlinePayload(pointer))
            throw new InvalidDataException($"Top-level Material pointer 0x{pointer.Raw:X8} does not reference inline payload data.");

        MaterialAssetModel material = LoadInlineMaterial(cursor, pointer, context, out _);
        XBlockAddress pointerCellAddress = pointer.CellAddress
            ?? throw new InvalidDataException("Inline Material pointer has no destination cell.");
        return context.DB_AddXAsset(material, pointerCellAddress);
    }

    public MaterialAssetModel? LoadFromPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        return LoadFromPointer(cursor, pointer, context, out _);
    }

    public MaterialAssetModel? LoadFromPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context,
        out MaterialAssetModel? incomingDefinition)
    {
        incomingDefinition = null;

        if (pointer.Type == PointerType.Null)
            return null;

        if (pointer.Type == PointerType.Offset)
        {
            int? aliasedRaw = ResolveAliasCellOffset(pointer, context, MaterialSize, "Material");
            context.PointerReader.ValidateOffsetPointerRange<MaterialAssetModel>(
                pointer,
                MaterialSize,
                XPointerNullability.Nullable,
                "Material");
            if (aliasedRaw == 0)
                return null;

            return context.ResolveMaterial(pointer)
                ?? throw new InvalidDataException(
                    $"Material pointer 0x{unchecked((uint)pointer.Raw):X8} does not resolve to a canonical Material asset.");
        }

        XBlockAddress? insertCell = pointer.Type == PointerType.Insert
            ? context.Blocks.AllocateInsertPointerCell()
            : null;

        MaterialAssetModel material = LoadInlineMaterial(cursor, pointer, context, out _);
        incomingDefinition = material;
        XBlockAddress pointerCellAddress = pointer.CellAddress
            ?? throw new InvalidDataException("Inline Material pointer has no destination cell.");
        MaterialAssetModel canonical = context.DB_AddXAsset(material, pointerCellAddress);
        if (insertCell is { } cell)
        {
            int canonicalRaw = canonical.RuntimeAddress?.RawValue
                ?? throw new InvalidDataException("Canonical Material has no runtime address.");
            context.Blocks.WriteInt32(cell, canonicalRaw);
        }

        return canonical;
    }

    private static MaterialAssetModel LoadInlineMaterial(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context,
        out XBlockAddress rootAddress)
    {
        int offset = cursor.Offset;
        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            XBlockAddress targetAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            byte[] rootBytes = context.Blocks.Load(cursor, MaterialSize, out rootAddress);
            if (rootAddress != targetAddress)
                throw new InvalidDataException($"Material pointer patched to {targetAddress}, but root loaded at {rootAddress}.");

            var rootCursor = new FastFileCursor(rootBytes, rootAddress);

            XPointer<string> namePointer = ReadXStringPointer(rootCursor, context);
            byte gameFlags = rootCursor.ReadByte();
            byte sortKey = rootCursor.ReadByte();
            byte textureAtlasRowCount = rootCursor.ReadByte();
            byte textureAtlasColumnCount = rootCursor.ReadByte();
            ulong drawSurfPacked = rootCursor.ReadUInt64();
            uint surfaceTypeBits = rootCursor.ReadUInt32();
            ushort hashIndex = rootCursor.ReadUInt16();
            ushort materialInfoPad16 = rootCursor.ReadUInt16();
            MaterialStateBitsEntry[] stateBitsEntries = ReadStateBitsEntries(rootCursor, TechniqueSlotCount);
            byte textureCount = rootCursor.ReadByte();
            byte constantCount = rootCursor.ReadByte();
            byte stateBitsCount = rootCursor.ReadByte();
            byte stateFlags = rootCursor.ReadByte();
            byte cameraRegion = rootCursor.ReadByte();
            byte xstringCount = rootCursor.ReadByte();
            byte pad43 = rootCursor.ReadByte();
            ushort[] inlineTechniqueSlotStateBits = ReadUshorts(rootCursor, TechniqueSlotCount);
            ushort pad8E = rootCursor.ReadUInt16();
            XPointerReference runtimeUshortPayload = ReadRawCell(rootCursor, XPointerOffsetMode.Direct);
            XPointerReference techniqueSetPointer = context.PointerReader.ReadCell(rootCursor, XPointerOffsetMode.AliasCell);
            XPointerReference textureTablePointer = context.PointerReader.ReadCell(rootCursor, XPointerOffsetMode.Direct);
            XPointerReference constantTablePointer = context.PointerReader.ReadCell(rootCursor, XPointerOffsetMode.Direct);
            XPointerReference stateBitsPointer = context.PointerReader.ReadCell(rootCursor, XPointerOffsetMode.Direct);
            XPointerReference xstringTablePointer = ReadRawCell(rootCursor, XPointerOffsetMode.Direct);

            if (rootCursor.Offset != MaterialSize)
                throw new InvalidDataException($"Material consumed 0x{rootCursor.Offset:X} bytes instead of 0x{MaterialSize:X}.");


            context.Blocks.Push(XFileBlockType.LARGE);
            try
            {
                string? name = ReadXString(cursor, namePointer, context);
                IReadOnlyList<ushort> runtimeTechniqueSlotStateBits = ReadRuntimeUshortPayload(cursor, runtimeUshortPayload, context);
                MaterialTechniqueSetAsset? techniqueSet = ReadTechniqueSetPointer(
                    cursor,
                    techniqueSetPointer,
                    context,
                    out MaterialTechniqueSetAsset? incomingTechniqueSet);
                IReadOnlyList<MaterialTextureDef> textures = ReadTextureDefArray(cursor, textureTablePointer, textureCount, context);
                IReadOnlyList<MaterialConstantDef> constants = ReadMaterialConstantArray(cursor, constantTablePointer, constantCount, context);
                IReadOnlyList<GfxStateBits> stateBits = ReadGfxStateBitsArray(cursor, stateBitsPointer, stateBitsCount, context);
                IReadOnlyList<MaterialXStringEntry> xstrings = ReadXStringPointerArray(cursor, xstringTablePointer, xstringCount, context);


                return new MaterialAssetModel
                {
                    Offset = offset,
                    RuntimeAddress = rootAddress,
                    Info = new MaterialInfo
                    {
                        NamePointer = namePointer,
                        Name = name,
                        GameFlags = gameFlags,
                        SortKey = sortKey,
                        TextureAtlasRowCount = textureAtlasRowCount,
                        TextureAtlasColumnCount = textureAtlasColumnCount,
                        DrawSurf = new GfxDrawSurf(drawSurfPacked),
                        SurfaceTypeBits = surfaceTypeBits,
                        HashIndex = hashIndex,
                        Pad16 = materialInfoPad16
                    },
                    StateBitsEntries = stateBitsEntries,
                    TextureCount = textureCount,
                    ConstantCount = constantCount,
                    StateBitsCount = stateBitsCount,
                    StateFlags = stateFlags,
                    CameraRegion = cameraRegion,
                    XStringCount = xstringCount,
                    Pad43 = pad43,
                    InlineTechniqueSlotStateBits = inlineTechniqueSlotStateBits,
                    Pad8E = pad8E,
                    RuntimeTechniqueSlotStateBitsPointer = runtimeUshortPayload,
                    RuntimeTechniqueSlotStateBits = runtimeTechniqueSlotStateBits,
                    TechniqueSetPointer = techniqueSetPointer.AsPointer<MaterialTechniqueSetAsset>(),
                    TechniqueSet = techniqueSet,
                    IncomingTechniqueSet = incomingTechniqueSet,
                    TextureTablePointer = textureTablePointer,
                    Textures = textures,
                    ConstantTablePointer = constantTablePointer,
                    Constants = constants,
                    StateBitsPointer = stateBitsPointer,
                    StateBits = stateBits,
                    XStringTablePointer = xstringTablePointer,
                    XStrings = xstrings
                };
            }
            finally
            {
                context.Blocks.Pop();
            }
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    private static MaterialStateBitsEntry[] ReadStateBitsEntries(FastFileCursor cursor, int count)
    {
        var entries = new MaterialStateBitsEntry[count];
        for (int i = 0; i < entries.Length; i++)
            entries[i] = new MaterialStateBitsEntry(i, cursor.ReadByte());

        return entries;
    }

    private static IReadOnlyList<ushort> ReadRuntimeUshortPayload(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        int byteCount = TechniqueSlotCount * sizeof(ushort);
        if (pointer.Type == PointerType.Null)
            return [];

        if (pointer.CellAddress is not { } cellAddress)
            throw new InvalidDataException($"Material runtime ushort payload cell 0x{pointer.Raw:X8} has no destination cell address.");

        context.Blocks.Push(XFileBlockType.RUNTIME);
        try
        {
            context.Blocks.AlignCurrent(2);
            XBlockAddress payloadAddress = context.Blocks.CurrentAddress;
            context.Blocks.WriteInt32(cellAddress, XPointerCodec.Encode(payloadAddress));
            byte[] payloadBytes = context.Blocks.Load(cursor, byteCount, out XBlockAddress loadedAddress);
            var payloadCursor = new FastFileCursor(payloadBytes, loadedAddress);
            return ReadUshorts(payloadCursor, TechniqueSlotCount);
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    private static int? ResolveAliasCellOffset(
        XPointerReference pointer,
        DbLoadExecutionContext context,
        int targetByteCount,
        string targetName)
    {
        if (pointer.Type != PointerType.Offset || pointer.ResolutionMode != XPointerResolutionMode.AliasCell)
            return null;

        if (pointer.CellAddress is not { } destinationCell)
            throw new InvalidDataException($"Alias-cell pointer 0x{pointer.Raw:X8} has no destination cell to patch.");

        int aliasedRaw = context.PointerReader.ReadAliasCellRaw(pointer);
        if (aliasedRaw != 0)
        {
            if (XPointerCodec.GetType(aliasedRaw) != PointerType.Offset)
                throw new InvalidDataException(
                    $"Alias-cell pointer 0x{pointer.Raw:X8} resolved to unresolved sentinel 0x{aliasedRaw:X8} for {targetName}.");

            context.PointerReader.ValidateOffsetPointerRange<MaterialAssetModel>(
                XPointerReference.FromRaw(aliasedRaw, XPointerResolutionMode.Direct, pointer.PackedAddress),
                targetByteCount,
                targetName);
        }

        context.Blocks.WriteInt32(destinationCell, aliasedRaw);
        return aliasedRaw;
    }

    private static MaterialTechniqueSetAsset? ReadTechniqueSetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context,
        out MaterialTechniqueSetAsset? incomingDefinition)
    {
        incomingDefinition = null;
        if (pointer.Type is not
                (PointerType.Inline or PointerType.Insert))
        {
            context.PointerReader.ValidateOffsetPointerRange<MaterialTechniqueSetAsset>(pointer, TechniqueSetSize, "MaterialTechniqueSet");
            return context.ResolveTechniqueSet(pointer);
        }

        return new MaterialTechniqueSetLoader().LoadFromAssetPointer(
            cursor,
            pointer,
            context,
            out incomingDefinition);
    }

    private static IReadOnlyList<MaterialTextureDef> ReadTextureDefArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative MaterialTextureDef count {count}.");

        if (!context.PointerReader.HasInlinePayload(pointer))
        {
            context.PointerReader.ValidateOffsetPointerRange<MaterialTextureDef[]>(pointer, checked(count * TextureDefSize), "MaterialTextureDef[]");
            return [];
        }

        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        byte[] textureBytes = context.Blocks.Load(cursor, checked(count * TextureDefSize), out XBlockAddress textureAddress);
        var textureCursor = new FastFileCursor(textureBytes, textureAddress);
        var textures = new MaterialTextureDef[count];

        for (int i = 0; i < textures.Length; i++)
        {
            uint nameHash = textureCursor.ReadUInt32();
            byte nameStart = textureCursor.ReadByte();
            byte nameEnd = textureCursor.ReadByte();
            byte samplerState = textureCursor.ReadByte();
            byte semantic = textureCursor.ReadByte();
            XPointerReference dataPointer = context.PointerReader.ReadCell(textureCursor, XPointerOffsetMode.AliasCell);
            textures[i] = new MaterialTextureDef
            {
                NameHash = nameHash,
                NameStart = nameStart,
                NameEnd = nameEnd,
                SamplerState = samplerState,
                Semantic = semantic,
                DataPointer = dataPointer
            };
        }

        for (int i = 0; i < textures.Length; i++)
        {
            MaterialTextureDef texture = textures[i];

            if (texture.Semantic == 0x0b)
                textures[i] = CopyTexture(texture, water: ReadWaterPointer(cursor, texture.DataPointer, context));
            else
            {
                GfxImageAsset? image = ReadGfxImagePointer(
                    cursor,
                    texture.DataPointer,
                    context,
                    out GfxImageAsset? incomingImage);
                textures[i] = CopyTexture(
                    texture,
                    image: image,
                    incomingImage: incomingImage);
            }
        }

        return textures;
    }

    private static MaterialTextureDef CopyTexture(
        MaterialTextureDef texture,
        GfxImageAsset? image = null,
        GfxImageAsset? incomingImage = null,
        MaterialWater? water = null)
    {
        return new MaterialTextureDef
        {
            NameHash = texture.NameHash,
            NameStart = texture.NameStart,
            NameEnd = texture.NameEnd,
            SamplerState = texture.SamplerState,
            Semantic = texture.Semantic,
            DataPointer = texture.DataPointer,
            Image = image,
            IncomingImage = incomingImage,
            Water = water
        };
    }

    private static GfxImageAsset? ReadGfxImagePointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context,
        out GfxImageAsset? incomingDefinition)
    {
        return ImageLoader.LoadFromPointer(
            cursor,
            pointer,
            context,
            out incomingDefinition);
    }

    private static MaterialWater? ReadWaterPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (!context.PointerReader.HasInlinePayload(pointer))
        {
            context.PointerReader.ValidateOffsetPointerRange(pointer, WaterSize, "water_t");
            return null;
        }

        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        byte[] rootBytes = context.Blocks.Load(cursor, WaterSize, out XBlockAddress rootAddress);
        var rootCursor = new FastFileCursor(rootBytes, rootAddress);

        var writable = new MaterialWaterWritable(rootCursor.ReadUInt32());
        XPointerReference h0xPointer = context.PointerReader.ReadCell(rootCursor, XPointerOffsetMode.Direct);
        XPointerReference h0yPointer = context.PointerReader.ReadCell(rootCursor, XPointerOffsetMode.Direct);
        XPointerReference wTermPointer = context.PointerReader.ReadCell(rootCursor, XPointerOffsetMode.Direct);
        int m = rootCursor.ReadInt32();
        int n = rootCursor.ReadInt32();
        float lx = ReadSingle(rootCursor);
        float lz = ReadSingle(rootCursor);
        float gravity = ReadSingle(rootCursor);
        float windVelocity = ReadSingle(rootCursor);
        var windDirection = new MaterialVec2(ReadSingle(rootCursor), ReadSingle(rootCursor));
        float amplitude = ReadSingle(rootCursor);
        var codeConstant = new MaterialVec4(
            ReadSingle(rootCursor),
            ReadSingle(rootCursor),
            ReadSingle(rootCursor),
            ReadSingle(rootCursor));
        XPointerReference imagePointer = context.PointerReader.ReadCell(rootCursor, XPointerOffsetMode.AliasCell);

        int elementCount = checked(m * n);
        IReadOnlyList<float> h0x = ReadWaterSpectrum(cursor, h0xPointer, elementCount, context);
        IReadOnlyList<float> h0y = ReadWaterSpectrum(cursor, h0yPointer, elementCount, context);
        IReadOnlyList<float> wTerm = ReadWaterSpectrum(cursor, wTermPointer, elementCount, context);
        GfxImageAsset? image = ReadGfxImagePointer(
            cursor,
            imagePointer,
            context,
            out GfxImageAsset? incomingImage);

        return new MaterialWater
        {
            Writable = writable,
            H0XPointer = h0xPointer,
            H0YPointer = h0yPointer,
            WTermPointer = wTermPointer,
            M = m,
            N = n,
            Lx = lx,
            Lz = lz,
            Gravity = gravity,
            WindVelocity = windVelocity,
            WindDirection = windDirection,
            Amplitude = amplitude,
            CodeConstant = codeConstant,
            ImagePointer = imagePointer.AsPointer<GfxImageAsset>(),
            H0X = h0x,
            H0Y = h0y,
            WTerm = wTerm,
            Image = image,
            IncomingImage = incomingImage
        };
    }

    private static IReadOnlyList<float> ReadWaterSpectrum(
        FastFileCursor cursor,
        XPointerReference pointer,
        int elementCount,
        DbLoadExecutionContext context)
    {
        int byteCount = checked(elementCount * sizeof(float));
        if (!context.PointerReader.HasInlinePayload(pointer))
        {
            context.PointerReader.ValidateOffsetPointerRange<float[]>(pointer, byteCount, "water spectrum float[]");
            return [];
        }

        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        byte[] bytes = context.Blocks.Load(cursor, byteCount, out XBlockAddress address);
        var spectrumCursor = new FastFileCursor(bytes, address);
        var values = new float[elementCount];
        for (int i = 0; i < values.Length; i++)
            values[i] = ReadSingle(spectrumCursor);

        return values;
    }

    private static IReadOnlyList<GfxStateBits> ReadGfxStateBitsArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative GfxStateBits count {count}.");

        if (!context.PointerReader.HasInlinePayload(pointer))
        {
            context.PointerReader.ValidateOffsetPointerRange<GfxStateBits[]>(pointer, checked(count * GfxStateBitsSize), "GfxStateBits[]");
            return [];
        }

        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
        byte[] stateBytes = context.Blocks.Load(cursor, checked(count * GfxStateBitsSize), out XBlockAddress stateAddress);
        var stateCursor = new FastFileCursor(stateBytes, stateAddress);
        var stateBits = new GfxStateBits[count];

        for (int i = 0; i < stateBits.Length; i++)
        {
            int loadBitsCellOffset = stateCursor.Offset;
            XPointerReference loadBits = XPointerReference.FromRaw(
                stateCursor.ReadInt32(),
                XPointerResolutionMode.AliasCell,
                stateCursor.AddressAt(loadBitsCellOffset));
            uint tail = stateCursor.ReadUInt32();
            stateBits[i] = new GfxStateBits
            {
                LoadBitsPointer = loadBits,
                Tail = tail
            };
        }

        for (int i = 0; i < stateBits.Length; i++)
        {
            GfxStateBits state = stateBits[i];
            stateBits[i] = new GfxStateBits
            {
                LoadBitsPointer = state.LoadBitsPointer,
                LoadBits = ReadGfxStateBitsLoadBits(cursor, state.LoadBitsPointer, context),
                Tail = state.Tail
            };
        }

        return stateBits;
    }

    private static IReadOnlyList<MaterialConstantDef> ReadMaterialConstantArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative MaterialConstantDef count {count}.");

        int byteCount = checked(count * ConstantDefSize);
        if (!context.PointerReader.HasInlinePayload(pointer))
        {
            context.PointerReader.ValidateOffsetPointerRange(pointer, byteCount, "MaterialConstantDef[]");
            return [];
        }

        context.PointerReader.PatchInlinePointerCell(pointer, alignment: 16);
        byte[] bytes = context.Blocks.Load(cursor, byteCount, out XBlockAddress address);
        var constantCursor = new FastFileCursor(bytes, address);
        var constants = new MaterialConstantDef[count];

        for (int i = 0; i < constants.Length; i++)
        {
            uint nameHash = constantCursor.ReadUInt32();
            byte[] nameBytes = constantCursor.ReadBytes(0x0c);
            constants[i] = new MaterialConstantDef
            {
                NameHash = nameHash,
                NameBytes = nameBytes,
                Literal = new MaterialVec4(
                    ReadSingle(constantCursor),
                    ReadSingle(constantCursor),
                    ReadSingle(constantCursor),
                    ReadSingle(constantCursor))
            };
        }

        return constants;
    }

    private static IReadOnlyList<uint> ReadGfxStateBitsLoadBits(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        const int byteCount = 2 * sizeof(int);


        if (pointer.Type == PointerType.Null)
            return [];

        if (pointer.Type == PointerType.Offset)
        {
            context.PointerReader.ValidateOffsetPointerRange<byte[]>(
                pointer,
                byteCount,
                "GfxStateBits.LoadBits");
            IReadOnlyList<uint> aliased = context.ResolveGfxStateLoadBits(pointer);
            XBlockAddress destinationCell = pointer.CellAddress
                ?? throw new InvalidDataException(
                    $"GfxStateBits loadBits alias 0x{unchecked((uint)pointer.Raw):X8} has no destination cell.");
            int aliasedRaw = context.PointerReader.ReadAliasCellRaw(pointer);
            context.Blocks.WriteInt32(destinationCell, aliasedRaw);
            return RegisterGfxStateLoadBits(pointer, aliased, context);
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
            return [];

        XBlockAddress? insertCell = pointer.Type == PointerType.Insert
            ? context.Blocks.AllocateInsertPointerCell()
            : null;

        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            XBlockAddress loadBitsAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            byte[] bytes = context.Blocks.Load(cursor, byteCount);
            if (insertCell is { } cell)
                context.Blocks.WriteInt32(cell, XPointerCodec.Encode(loadBitsAddress));

            var loadBitsCursor = new FastFileCursor(bytes, loadBitsAddress);
            IReadOnlyList<uint> loadBits =
            [
                loadBitsCursor.ReadUInt32(),
                loadBitsCursor.ReadUInt32()
            ];
            return RegisterGfxStateLoadBits(pointer, loadBits, context);
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    private static IReadOnlyList<uint> RegisterGfxStateLoadBits(
        XPointerReference pointer,
        IReadOnlyList<uint> loadBits,
        DbLoadExecutionContext context)
    {
        XBlockAddress aliasCell = pointer.CellAddress
            ?? throw new InvalidDataException(
                $"GfxStateBits loadBits pointer 0x{unchecked((uint)pointer.Raw):X8} has no destination cell.");
        return context.RegisterGfxStateLoadBits(aliasCell, loadBits);
    }

    private static IReadOnlyList<MaterialXStringEntry> ReadXStringPointerArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        if (count < 0)
            throw new InvalidDataException($"Invalid negative Material XString count {count}.");

        if (pointer.Type == PointerType.Null)
            return [];

        if (pointer.CellAddress is not { } cellAddress)
            throw new InvalidDataException($"Material XString[] cell 0x{pointer.Raw:X8} has no destination cell address.");

        context.Blocks.AlignCurrent(4);
        XBlockAddress tableAddress = context.Blocks.CurrentAddress;
        context.Blocks.WriteInt32(cellAddress, XPointerCodec.Encode(tableAddress));
        byte[] pointerBytes = context.Blocks.Load(cursor, checked(count * sizeof(int)), out XBlockAddress pointerTableAddress);
        if (pointerTableAddress != tableAddress)
            throw new InvalidDataException($"Material XString[] pointer patched to {tableAddress}, but table loaded at {pointerTableAddress}.");

        var pointerCursor = new FastFileCursor(pointerBytes, pointerTableAddress);
        var pointers = new XPointer<string>[count];

        for (int i = 0; i < pointers.Length; i++)
            pointers[i] = context.PointerReader.ReadPointer<string>(pointerCursor, XPointerResolutionMode.Direct);

        var entries = new MaterialXStringEntry[count];
        for (int i = 0; i < pointers.Length; i++)
            entries[i] = new MaterialXStringEntry(i, pointers[i], ReadXString(cursor, pointers[i], context));

        return entries;
    }

    private static ushort[] ReadUshorts(FastFileCursor cursor, int count)
    {
        var values = new ushort[count];
        for (int i = 0; i < values.Length; i++)
            values[i] = cursor.ReadUInt16();

        return values;
    }

    private static float ReadSingle(FastFileCursor cursor)
    {
        return BitConverter.Int32BitsToSingle(cursor.ReadInt32());
    }

    private static XPointer<string> ReadXStringPointer(
        FastFileCursor cursor,
        DbLoadExecutionContext context)
    {
        return context.PointerReader.ReadPointer<string>(cursor, XPointerResolutionMode.Direct);
    }

    private static string? ReadXString(
        FastFileCursor cursor,
        XPointer<string> pointer,
        DbLoadExecutionContext context)
    {
        return context.PointerReader.LoadXString(cursor, pointer);
    }

    private static XPointerReference ReadRawCell(
        FastFileCursor cursor,
        XPointerOffsetMode offsetMode)
    {
        int cellOffset = cursor.Offset;
        return XPointerReference.FromRaw(
            cursor.ReadInt32(),
            offsetMode,
            cursor.AddressAt(cellOffset));
    }

}
