using IW4.FastFiles.Loaders.Database;
using IW4.FastFiles.Loaders.Assets.Material;
using IW4.FastFiles.Loaders.Assets.Physics;
using IW4.Assets.Assets.FxMap;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.Physics;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;
using IW4.Runtime.IO;

namespace IW4.FastFiles.Loaders.Assets.FxMap;

public sealed class FxWorldLoader
{
    private readonly MaterialLoader _materialLoader = new();
    private readonly PhysPresetLoader _physPresetLoader = new();

    public FxWorldAsset LoadFromAssetPointer(
        FastFileCursor cursor,
        XPointerReference pointer,
        DbLoadExecutionContext context)
    {
        if (pointer.Type == PointerType.Null)
            throw new InvalidDataException("Top-level FxWorld pointer is null.");

        if (pointer.Type == PointerType.Offset)
        {
            FxWorldAsset canonical = context.ResolveCanonicalAsset<FxWorldAsset>(
                    pointer,
                    XAssetType.FxMap)
                ?? throw new InvalidDataException(
                    $"Top-level FxWorld pointer 0x{unchecked((uint)pointer.Raw):X8} " +
                    "does not resolve to a canonical FxMap asset.");
            context.PatchCanonicalAssetPointerCell(
                pointer,
                canonical,
                "Packed FxWorld pointer has no destination cell.",
                "Canonical FxWorld has no runtime address.");
            return canonical;
        }

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
            throw new InvalidDataException($"Top-level FxWorld pointer 0x{pointer.Raw:X8} does not reference inline/insert payload data.");

        ProviderRegistrationOccurrence providerRegistration = context.BeginProviderRegistration(pointer);

        context.Blocks.Push(XFileBlockType.TEMP);
        try
        {
            XBlockAddress rootAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
            FxWorldAsset fxWorld = ReadFxWorld(cursor, rootAddress, context);
            FxWorldAsset canonical = context.DB_AddXAsset(
                XAssetType.FxMap,
                fxWorld.Name,
                fxWorld,
                providerRegistration);

            return canonical;
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    private FxWorldAsset ReadFxWorld(
        FastFileCursor cursor,
        XBlockAddress expectedRootAddress,
        DbLoadExecutionContext context)
    {
        int sourceOffset = cursor.Offset;
        byte[] rootBytes = context.Blocks.Load(cursor, FxWorldAsset.SerializedSize, out XBlockAddress rootAddress);
        if (rootAddress != expectedRootAddress)
            throw new InvalidDataException($"FxWorld pointer patched to {expectedRootAddress}, but root loaded at {rootAddress}.");

        var rootCursor = new FastFileCursor(rootBytes, rootAddress);
        XPointer<string> namePointer = context.PointerReader.ReadPointer<string>(rootCursor, XPointerResolutionMode.Direct);
        FxGlassSystem glassSystem = ReadFxGlassSystemHeader(rootCursor, context);

        if (rootCursor.Offset != FxWorldAsset.SerializedSize)
            throw new InvalidDataException($"FxWorld consumed 0x{rootCursor.Offset:X} bytes instead of 0x{FxWorldAsset.SerializedSize:X}.");

        string? name;
        context.Blocks.Push(XFileBlockType.LARGE);
        try
        {
            name = context.PointerReader.LoadXString(cursor, namePointer);
            glassSystem = ReadFxGlassSystemPayloads(cursor, glassSystem, context);
        }
        finally
        {
            context.Blocks.Pop();
        }


        return new FxWorldAsset
        {
            Offset = sourceOffset,
            RuntimeAddress = rootAddress,
            NamePointer = namePointer,
            Name = name,
            GlassSystem = glassSystem
        };
    }


    private static FxGlassSystem ReadFxGlassSystemHeader(
        FastFileCursor cursor,
        DbLoadExecutionContext context)
    {
        int start = cursor.Offset;
        XBlockAddress address = cursor.AddressAt(start)
            ?? throw new InvalidDataException("FxGlassSystem cursor has no destination block address.");
        int time = cursor.ReadInt32();
        int prevTime = cursor.ReadInt32();
        uint defCount = cursor.ReadUInt32();
        uint pieceLimit = cursor.ReadUInt32();
        uint pieceWordCount = cursor.ReadUInt32();
        uint initPieceCount = cursor.ReadUInt32();
        uint cellCount = cursor.ReadUInt32();
        uint activePieceCount = cursor.ReadUInt32();
        uint firstFreePiece = cursor.ReadUInt32();
        uint geoDataLimit = cursor.ReadUInt32();
        uint geoDataCount = cursor.ReadUInt32();
        uint initGeoDataCount = cursor.ReadUInt32();
        XPointer<FxGlassDef[]> defsPointer = context.PointerReader.ReadPointer<FxGlassDef[]>(cursor, XPointerResolutionMode.Direct);
        XPointer<FxGlassPiecePlace[]> piecePlacesPointer = context.PointerReader.ReadPointer<FxGlassPiecePlace[]>(cursor, XPointerResolutionMode.Direct);
        XPointer<FxGlassPieceState[]> pieceStatesPointer = context.PointerReader.ReadPointer<FxGlassPieceState[]>(cursor, XPointerResolutionMode.Direct);
        XPointer<FxGlassPieceDynamics[]> pieceDynamicsPointer = context.PointerReader.ReadPointer<FxGlassPieceDynamics[]>(cursor, XPointerResolutionMode.Direct);
        XPointer<FxGlassGeometryData[]> geoDataPointer = context.PointerReader.ReadPointer<FxGlassGeometryData[]>(cursor, XPointerResolutionMode.Direct);
        XPointer<uint[]> isInUsePointer = context.PointerReader.ReadPointer<uint[]>(cursor, XPointerResolutionMode.Direct);
        XPointer<uint[]> cellBitsPointer = context.PointerReader.ReadPointer<uint[]>(cursor, XPointerResolutionMode.Direct);
        XPointer<byte[]> visDataPointer = context.PointerReader.ReadPointer<byte[]>(cursor, XPointerResolutionMode.Direct);
        XPointer<FxVec3[]> linkOrgPointer = context.PointerReader.ReadPointer<FxVec3[]>(cursor, XPointerResolutionMode.Direct);
        XPointer<float[]> halfThicknessPointer = context.PointerReader.ReadPointer<float[]>(cursor, XPointerResolutionMode.Direct);
        XPointer<ushort[]> lightingHandlesPointer = context.PointerReader.ReadPointer<ushort[]>(cursor, XPointerResolutionMode.Direct);
        XPointer<FxGlassInitPieceState[]> initPieceStatesPointer = context.PointerReader.ReadPointer<FxGlassInitPieceState[]>(cursor, XPointerResolutionMode.Direct);
        XPointer<FxGlassGeometryData[]> initGeoDataPointer = context.PointerReader.ReadPointer<FxGlassGeometryData[]>(cursor, XPointerResolutionMode.Direct);
        byte needToCompactData = cursor.ReadByte();
        byte initCount = cursor.ReadByte();
        ushort pad66 = cursor.ReadUInt16();
        float effectChanceAccum = cursor.ReadSingle();
        int lastPieceDeletionTime = cursor.ReadInt32();

        if (cursor.Offset - start != FxGlassSystem.SerializedSize)
            throw new InvalidDataException($"FxGlassSystem consumed 0x{cursor.Offset - start:X} bytes instead of 0x{FxGlassSystem.SerializedSize:X}.");

        return new FxGlassSystem
        {
            Offset = address.Offset,
            Time = time,
            PrevTime = prevTime,
            DefCount = defCount,
            PieceLimit = pieceLimit,
            PieceWordCount = pieceWordCount,
            InitPieceCount = initPieceCount,
            CellCount = cellCount,
            ActivePieceCount = activePieceCount,
            FirstFreePiece = firstFreePiece,
            GeoDataLimit = geoDataLimit,
            GeoDataCount = geoDataCount,
            InitGeoDataCount = initGeoDataCount,
            DefsPointer = defsPointer,
            PiecePlacesPointer = piecePlacesPointer,
            PieceStatesPointer = pieceStatesPointer,
            PieceDynamicsPointer = pieceDynamicsPointer,
            GeoDataPointer = geoDataPointer,
            IsInUsePointer = isInUsePointer,
            CellBitsPointer = cellBitsPointer,
            VisDataPointer = visDataPointer,
            LinkOrgPointer = linkOrgPointer,
            HalfThicknessPointer = halfThicknessPointer,
            LightingHandlesPointer = lightingHandlesPointer,
            InitPieceStatesPointer = initPieceStatesPointer,
            InitGeoDataPointer = initGeoDataPointer,
            NeedToCompactData = needToCompactData,
            InitCount = initCount,
            Pad66 = pad66,
            EffectChanceAccum = effectChanceAccum,
            LastPieceDeletionTime = lastPieceDeletionTime
        };
    }

    private FxGlassSystem ReadFxGlassSystemPayloads(
        FastFileCursor cursor,
        FxGlassSystem header,
        DbLoadExecutionContext context)
    {

        IReadOnlyList<FxGlassDef> defs = ReadFxGlassDefs(cursor, header.DefsPointer.Untyped, Count(header.DefCount, "defCount"), context);

        IReadOnlyList<FxGlassPiecePlace> piecePlaces;
        IReadOnlyList<FxGlassPieceState> pieceStates;
        IReadOnlyList<FxGlassPieceDynamics> pieceDynamics;
        IReadOnlyList<FxGlassGeometryData> geoData;
        IReadOnlyList<uint> isInUse;
        IReadOnlyList<uint> cellBits;
        IReadOnlyList<byte> visData;
        IReadOnlyList<FxVec3> linkOrg;
        IReadOnlyList<float> halfThickness;

        int pieceLimit = Count(header.PieceLimit, "pieceLimit");
        int pieceWordCount = Count(header.PieceWordCount, "pieceWordCount");
        int cellCount = Count(header.CellCount, "cellCount");
        piecePlaces = ReadPushedRuntime(context, () => ReadPiecePlaces(cursor, header.PiecePlacesPointer.Untyped, pieceLimit, context));
        pieceStates = ReadPushedRuntime(context, () => ReadPieceStates(cursor, header.PieceStatesPointer.Untyped, pieceLimit, context));
        pieceDynamics = ReadPushedRuntime(context, () => ReadPieceDynamics(cursor, header.PieceDynamicsPointer.Untyped, pieceLimit, context));
        geoData = ReadPushedRuntime(context, () => ReadGeometryData(cursor, header.GeoDataPointer.Untyped, Count(header.GeoDataLimit, "geoDataLimit"), context, "FxGlassSystem.geoData"));
        isInUse = ReadPushedRuntime(context, () => ReadUInt32Array(cursor, header.IsInUsePointer.Untyped, pieceWordCount, 4, context, "FxGlassSystem.isInUse"));
        cellBits = ReadPushedRuntime(context, () => ReadUInt32Array(cursor, header.CellBitsPointer.Untyped, checked(cellCount * pieceWordCount), 4, context, "FxGlassSystem.cellBits"));
        visData = ReadPushedRuntime(context, () => ReadByteArray(cursor, header.VisDataPointer.Untyped, Align(pieceLimit, 16), 16, context, "FxGlassSystem.visData"));
        linkOrg = ReadPushedRuntime(context, () => ReadVec3Array(cursor, header.LinkOrgPointer.Untyped, pieceLimit, context));
        halfThickness = ReadPushedRuntime(context, () => ReadFloatArray(cursor, header.HalfThicknessPointer.Untyped, Align(pieceLimit, 4), 16, context, "FxGlassSystem.halfThickness"));

        IReadOnlyList<ushort> lightingHandles = ReadUInt16Array(
            cursor,
            header.LightingHandlesPointer.Untyped,
            Count(header.InitPieceCount, "initPieceCount"),
            2,
            context,
            "FxGlassSystem.lightingHandles");
        IReadOnlyList<FxGlassInitPieceState> initPieceStates = ReadInitPieceStates(
            cursor,
            header.InitPieceStatesPointer.Untyped,
            Count(header.InitPieceCount, "initPieceCount"),
            context);
        IReadOnlyList<FxGlassGeometryData> initGeoData = ReadGeometryData(
            cursor,
            header.InitGeoDataPointer.Untyped,
            Count(header.InitGeoDataCount, "initGeoDataCount"),
            context,
            "FxGlassSystem.initGeoData");


        return new FxGlassSystem
        {
            Offset = header.Offset,
            Time = header.Time,
            PrevTime = header.PrevTime,
            DefCount = header.DefCount,
            PieceLimit = header.PieceLimit,
            PieceWordCount = header.PieceWordCount,
            InitPieceCount = header.InitPieceCount,
            CellCount = header.CellCount,
            ActivePieceCount = header.ActivePieceCount,
            FirstFreePiece = header.FirstFreePiece,
            GeoDataLimit = header.GeoDataLimit,
            GeoDataCount = header.GeoDataCount,
            InitGeoDataCount = header.InitGeoDataCount,
            DefsPointer = header.DefsPointer,
            Defs = defs,
            PiecePlacesPointer = header.PiecePlacesPointer,
            PiecePlaces = piecePlaces,
            PieceStatesPointer = header.PieceStatesPointer,
            PieceStates = pieceStates,
            PieceDynamicsPointer = header.PieceDynamicsPointer,
            PieceDynamics = pieceDynamics,
            GeoDataPointer = header.GeoDataPointer,
            GeoData = geoData,
            IsInUsePointer = header.IsInUsePointer,
            IsInUse = isInUse,
            CellBitsPointer = header.CellBitsPointer,
            CellBits = cellBits,
            VisDataPointer = header.VisDataPointer,
            VisData = visData,
            LinkOrgPointer = header.LinkOrgPointer,
            LinkOrg = linkOrg,
            HalfThicknessPointer = header.HalfThicknessPointer,
            HalfThickness = halfThickness,
            LightingHandlesPointer = header.LightingHandlesPointer,
            LightingHandles = lightingHandles,
            InitPieceStatesPointer = header.InitPieceStatesPointer,
            InitPieceStates = initPieceStates,
            InitGeoDataPointer = header.InitGeoDataPointer,
            InitGeoData = initGeoData,
            NeedToCompactData = header.NeedToCompactData,
            InitCount = header.InitCount,
            Pad66 = header.Pad66,
            EffectChanceAccum = header.EffectChanceAccum,
            LastPieceDeletionTime = header.LastPieceDeletionTime
        };
    }

    private IReadOnlyList<FxGlassDef> ReadFxGlassDefs(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        XBlockAddress defsAddress = PatchRequiredInlineArray(pointer, count, context, "FxGlassSystem.defs");
        int byteCount = checked(count * FxGlassDef.SerializedSize);
        byte[] bytes = context.Blocks.Load(cursor, byteCount, out XBlockAddress loadedAddress);
        if (loadedAddress != defsAddress)
            throw new InvalidDataException($"FxGlassSystem.defs pointer patched to {defsAddress}, but array loaded at {loadedAddress}.");

        var rowCursor = new FastFileCursor(bytes, defsAddress);
        var defs = new FxGlassDef[count];
        for (int i = 0; i < defs.Length; i++)
            defs[i] = ReadFxGlassDef(cursor, rowCursor, context, i);

        return defs;
    }

    private static T ReadPushedRuntime<T>(
        DbLoadExecutionContext context,
        Func<T> read)
    {
        context.Blocks.Push(XFileBlockType.RUNTIME);
        try
        {
            return read();
        }
        finally
        {
            context.Blocks.Pop();
        }
    }

    private FxGlassDef ReadFxGlassDef(
        FastFileCursor cursor,
        FastFileCursor rowCursor,
        DbLoadExecutionContext context,
        int index)
    {
        int start = rowCursor.Offset;
        XBlockAddress rowAddress = rowCursor.AddressAt(start)
            ?? throw new InvalidDataException("FxGlassDef cursor has no destination block address.");
        float halfThickness = rowCursor.ReadSingle();
        FxVec2 texVec0 = ReadVec2(rowCursor);
        FxVec2 texVec1 = ReadVec2(rowCursor);
        uint color = rowCursor.ReadUInt32();
        XPointer<MaterialAsset> materialPointer = context.PointerReader.ReadPointer<MaterialAsset>(rowCursor, XPointerResolutionMode.AliasCell);
        XPointer<MaterialAsset> materialShatteredPointer = context.PointerReader.ReadPointer<MaterialAsset>(rowCursor, XPointerResolutionMode.AliasCell);
        XPointer<PhysPresetAsset> physPresetPointer = context.PointerReader.ReadPointer<PhysPresetAsset>(rowCursor, XPointerResolutionMode.AliasCell);
        float invHighMipRadius = rowCursor.ReadSingle();
        float shatteredInvHighMipRadius = rowCursor.ReadSingle();

        if (rowCursor.Offset - start != FxGlassDef.SerializedSize)
            throw new InvalidDataException($"FxGlassDef consumed 0x{rowCursor.Offset - start:X} bytes instead of 0x{FxGlassDef.SerializedSize:X}.");

        int childSourceStart = cursor.Offset;

        PhysPresetAsset? physPreset = _physPresetLoader.LoadFromPointer(
            cursor,
            physPresetPointer.Untyped,
            context);
        MaterialAsset? material = _materialLoader.LoadFromPointer(
            cursor,
            materialPointer.Untyped,
            context);
        MaterialAsset? materialShattered = _materialLoader.LoadFromPointer(
            cursor,
            materialShatteredPointer.Untyped,
            context);

        return new FxGlassDef
        {
            Offset = rowAddress.Offset,
            HalfThickness = halfThickness,
            TexVecs = [texVec0, texVec1],
            Color = color,
            MaterialPointer = materialPointer,
            Material = material,
            MaterialShatteredPointer = materialShatteredPointer,
            MaterialShattered = materialShattered,
            PhysPresetPointer = physPresetPointer,
            PhysPreset = physPreset,
            InvHighMipRadius = invHighMipRadius,
            ShatteredInvHighMipRadius = shatteredInvHighMipRadius
        };
    }

    private static IReadOnlyList<FxGlassPiecePlace> ReadPiecePlaces(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, FxGlassPiecePlace.SerializedSize, 4, context, "FxGlassSystem.piecePlaces");
        var c = new FastFileCursor(bytes);
        var rows = new FxGlassPiecePlace[count];
        for (int i = 0; i < rows.Length; i++)
        {
            FxSpatialFrame frame = ReadSpatialFrame(c);
            float radius = c.ReadSingle();
            uint nextFree = BitConverter.SingleToUInt32Bits(frame.Quat.X);
            rows[i] = new FxGlassPiecePlace(frame, radius, nextFree);
        }

        return rows;
    }

    private static IReadOnlyList<FxGlassPieceState> ReadPieceStates(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, FxGlassPieceState.SerializedSize, 4, context, "FxGlassSystem.pieceStates");
        var c = new FastFileCursor(bytes);
        var rows = new FxGlassPieceState[count];
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = new FxGlassPieceState
            {
                TexCoordOrigin = ReadVec2(c),
                SupportMask = c.ReadUInt32(),
                InitIndex = c.ReadUInt16(),
                GeoDataStart = c.ReadUInt16(),
                DefIndex = c.ReadByte(),
                Pad11 = c.ReadBytes(5),
                VertCount = c.ReadByte(),
                HoleDataCount = c.ReadByte(),
                CrackDataCount = c.ReadByte(),
                FanDataCount = c.ReadByte(),
                Flags = c.ReadUInt16(),
                AreaX2 = c.ReadSingle()
            };
        }

        return rows;
    }

    private static IReadOnlyList<FxGlassPieceDynamics> ReadPieceDynamics(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, FxGlassPieceDynamics.SerializedSize, 4, context, "FxGlassSystem.pieceDynamics");
        var c = new FastFileCursor(bytes);
        var rows = new FxGlassPieceDynamics[count];
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = new FxGlassPieceDynamics(
                c.ReadInt32(),
                c.ReadInt32(),
                c.ReadInt32(),
                ReadVec3(c),
                ReadVec3(c));
        }

        return rows;
    }

    private static IReadOnlyList<FxGlassInitPieceState> ReadInitPieceStates(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, FxGlassInitPieceState.SerializedSize, 4, context, "FxGlassSystem.initPieceStates");
        var c = new FastFileCursor(bytes);
        var rows = new FxGlassInitPieceState[count];
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = new FxGlassInitPieceState
            {
                Frame = ReadSpatialFrame(c),
                Radius = c.ReadSingle(),
                TexCoordOrigin = ReadVec2(c),
                SupportMask = c.ReadUInt32(),
                AreaX2 = c.ReadSingle(),
                DefIndex = c.ReadByte(),
                VertCount = c.ReadByte(),
                FanDataCount = c.ReadByte(),
                Pad33 = c.ReadByte()
            };
        }

        return rows;
    }

    private static IReadOnlyList<FxGlassGeometryData> ReadGeometryData(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        string memberName)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, FxGlassGeometryData.SerializedSize, 4, context, memberName);
        var c = new FastFileCursor(bytes);
        var rows = new FxGlassGeometryData[count];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = new FxGlassGeometryData(c.ReadUInt32());

        return rows;
    }

    private static IReadOnlyList<uint> ReadUInt32Array(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        int alignment,
        DbLoadExecutionContext context,
        string memberName)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, sizeof(uint), alignment, context, memberName);
        var c = new FastFileCursor(bytes);
        var values = new uint[count];
        for (int i = 0; i < values.Length; i++)
            values[i] = c.ReadUInt32();

        return values;
    }

    private static IReadOnlyList<ushort> ReadUInt16Array(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        int alignment,
        DbLoadExecutionContext context,
        string memberName)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, sizeof(ushort), alignment, context, memberName);
        var c = new FastFileCursor(bytes);
        var values = new ushort[count];
        for (int i = 0; i < values.Length; i++)
            values[i] = c.ReadUInt16();

        return values;
    }

    private static IReadOnlyList<float> ReadFloatArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        int alignment,
        DbLoadExecutionContext context,
        string memberName)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, sizeof(float), alignment, context, memberName);
        var c = new FastFileCursor(bytes);
        var values = new float[count];
        for (int i = 0; i < values.Length; i++)
            values[i] = c.ReadSingle();

        return values;
    }

    private static IReadOnlyList<byte> ReadByteArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        int alignment,
        DbLoadExecutionContext context,
        string memberName)
    {
        return LoadInlineArray(cursor, pointer, count, 1, alignment, context, memberName);
    }

    private static IReadOnlyList<FxVec3> ReadVec3Array(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context)
    {
        byte[] bytes = LoadInlineArray(cursor, pointer, count, 0x0C, 4, context, "FxGlassSystem.linkOrg");
        var c = new FastFileCursor(bytes);
        var values = new FxVec3[count];
        for (int i = 0; i < values.Length; i++)
            values[i] = ReadVec3(c);

        return values;
    }

    private static XBlockAddress PatchRequiredInlineArray(
        XPointerReference pointer,
        int count,
        DbLoadExecutionContext context,
        string memberName)
    {
        if (pointer.Type == PointerType.Null && count == 0)
            return context.Blocks.CurrentAddress;

        if (pointer.Type == PointerType.Null)
            throw new InvalidDataException($"{memberName} is null with non-zero count {count}.");

        if (pointer.Type == PointerType.Offset)
            throw new InvalidDataException($"{memberName} pointer 0x{pointer.Raw:X8} is packed, but PS3 Load_FxGlassSystem only proves null/non-null inline array loading.");

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
            throw new InvalidDataException($"{memberName} pointer 0x{pointer.Raw:X8} is not inline/insert/null.");

        return context.PointerReader.PatchInlinePointerCell(pointer, alignment: 4);
    }

    private static byte[] LoadInlineArray(
        FastFileCursor cursor,
        XPointerReference pointer,
        int count,
        int stride,
        int alignment,
        DbLoadExecutionContext context,
        string memberName)
    {
        if (pointer.Type == PointerType.Null)
        {
            if (count != 0)
                throw new InvalidDataException($"{memberName} is null with non-zero count {count}.");

            return [];
        }

        if (pointer.Type == PointerType.Offset)
            throw new InvalidDataException($"{memberName} pointer 0x{pointer.Raw:X8} is packed, but PS3 Load_FxGlassSystem only proves null/non-null inline array loading.");

        if (pointer.Type is not (PointerType.Inline or PointerType.Insert))
            throw new InvalidDataException($"{memberName} pointer 0x{pointer.Raw:X8} is not inline/insert/null.");

        int byteCount = checked(count * stride);
        XBlockAddress targetAddress = context.PointerReader.PatchInlinePointerCell(pointer, alignment);
        byte[] bytes = context.Blocks.Load(cursor, byteCount, out XBlockAddress loadedAddress);
        if (loadedAddress != targetAddress)
            throw new InvalidDataException($"{memberName} pointer patched to {targetAddress}, but array loaded at {loadedAddress}.");

        return bytes;
    }

    private static FxSpatialFrame ReadSpatialFrame(FastFileCursor cursor)
    {
        return new FxSpatialFrame(
            new FxQuat(cursor.ReadSingle(), cursor.ReadSingle(), cursor.ReadSingle(), cursor.ReadSingle()),
            ReadVec3(cursor));
    }

    private static FxVec3 ReadVec3(FastFileCursor cursor)
    {
        return new FxVec3(cursor.ReadSingle(), cursor.ReadSingle(), cursor.ReadSingle());
    }

    private static FxVec2 ReadVec2(FastFileCursor cursor)
    {
        return new FxVec2(cursor.ReadSingle(), cursor.ReadSingle());
    }


    private static int Count(uint value, string name)
    {
        if (value > int.MaxValue)
            throw new InvalidDataException($"{name} {value} exceeds supported managed count range.");

        return (int)value;
    }

    private static int Align(int value, int alignment)
    {
        return checked((value + alignment - 1) / alignment * alignment);
    }
}
