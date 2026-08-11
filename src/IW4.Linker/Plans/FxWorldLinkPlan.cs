using IW4.Assets.Assets;
using IW4.Assets.Assets.FxMap;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.Linker.Contracts;

namespace IW4.Linker.Plans;

/// <summary>
/// Frozen FxMap glass graph. Definition and initial-state rows consume LARGE
/// source bytes; mutable glass working arrays are source-free RUNTIME
/// allocations exactly as the PS3 loader proves.
/// </summary>
internal sealed class FxWorldLinkPlan : AssetLinkPlan
{
    private FxWorldLinkPlan(
        AssetKey key,
        string originalSerializedName,
        FxGlassSystem glass,
        LinkAssetFreezeScope freeze,
        LinkStorageTarget? definitions,
        LinkStorageSymbol? piecePlaces,
        LinkStorageSymbol? pieceStates,
        LinkStorageSymbol? pieceDynamics,
        LinkStorageSymbol? geoData,
        LinkStorageSymbol? isInUse,
        LinkStorageSymbol? cellBits,
        LinkStorageSymbol? visData,
        LinkStorageSymbol? linkOrg,
        LinkStorageSymbol? halfThickness,
        LinkStorageTarget? lightingHandles,
        LinkStorageTarget? initialPieceStates,
        LinkStorageTarget? initialGeoData)
        : base(
            key,
            originalSerializedName,
            freeze.FreezeProviderName(originalSerializedName, 0, "Asset.Name"))
    {
        var writer = new LinkTemplateWriter(FxWorldAsset.SerializedSize);
        writer.Skip(sizeof(int));
        writer.WriteInt32(glass.Time);
        writer.WriteInt32(glass.PrevTime);
        writer.WriteUInt32(glass.DefCount);
        writer.WriteUInt32(glass.PieceLimit);
        writer.WriteUInt32(glass.PieceWordCount);
        writer.WriteUInt32(glass.InitPieceCount);
        writer.WriteUInt32(glass.CellCount);
        writer.WriteUInt32(glass.ActivePieceCount);
        writer.WriteUInt32(glass.FirstFreePiece);
        writer.WriteUInt32(glass.GeoDataLimit);
        writer.WriteUInt32(glass.GeoDataCount);
        writer.WriteUInt32(glass.InitGeoDataCount);
        writer.Skip(13 * sizeof(int));
        writer.WriteByte(glass.NeedToCompactData);
        writer.WriteByte(glass.InitCount);
        writer.WriteUInt16(glass.Pad66);
        WriteSingle(writer, glass.EffectChanceAccum);
        writer.WriteInt32(glass.LastPieceDeletionTime);
        Root = LinkStorageSymbol.SourceBytes(
            XFileBlockType.TEMP,
            writer.Complete(),
            alignment: 4,
            root => CreateRootOperations(
                root,
                definitions,
                piecePlaces,
                pieceStates,
                pieceDynamics,
                geoData,
                isInUse,
                cellBits,
                visData,
                linkOrg,
                halfThickness,
                lightingHandles,
                initialPieceStates,
                initialGeoData));
    }

    internal override LinkStorageSymbol Root { get; }

    public static AssetLinkPlan Freeze(
        AssetKey key,
        string originalSerializedName,
        FxWorldAsset definition,
        LinkAssetFreezeScope freeze)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(freeze);
        FxGlassSystem glass = definition.GlassSystem ??
            throw new InvalidDataException("FxMap.GlassSystem cannot be null.");
        if (originalSerializedName.StartsWith(','))
        {
            ValidateReferenceShape(glass);
            return ExternalAssetLinkPlan.Create(
                key,
                XAssetType.FxMap,
                originalSerializedName,
                freeze);
        }
        FrozenGlass frozen = FrozenGlass.Freeze(glass, freeze);
        return new FxWorldLinkPlan(
            key,
            originalSerializedName,
            glass,
            freeze,
            frozen.Definitions,
            frozen.PiecePlaces,
            frozen.PieceStates,
            frozen.PieceDynamics,
            frozen.GeoData,
            frozen.IsInUse,
            frozen.CellBits,
            frozen.VisData,
            frozen.LinkOrg,
            frozen.HalfThickness,
            frozen.LightingHandles,
            frozen.InitialPieceStates,
            frozen.InitialGeoData);
    }

    private IEnumerable<LinkOperation> CreateRootOperations(
        LinkStorageSymbol root,
        LinkStorageTarget? definitions,
        LinkStorageSymbol? piecePlaces,
        LinkStorageSymbol? pieceStates,
        LinkStorageSymbol? pieceDynamics,
        LinkStorageSymbol? geoData,
        LinkStorageSymbol? isInUse,
        LinkStorageSymbol? cellBits,
        LinkStorageSymbol? visData,
        LinkStorageSymbol? linkOrg,
        LinkStorageSymbol? halfThickness,
        LinkStorageTarget? lightingHandles,
        LinkStorageTarget? initialPieceStates,
        LinkStorageTarget? initialGeoData)
    {
        yield return NameOperation(root, 0);
        if (definitions is { } definitionStorage)
            yield return Direct(root, 0x34, definitionStorage, "FxMap.GlassSystem.Defs");
        if (piecePlaces is not null)
            yield return PresenceOperation(root, 0x38, piecePlaces, "FxMap.GlassSystem.PiecePlaces");
        if (pieceStates is not null)
            yield return PresenceOperation(root, 0x3c, pieceStates, "FxMap.GlassSystem.PieceStates");
        if (pieceDynamics is not null)
            yield return PresenceOperation(root, 0x40, pieceDynamics, "FxMap.GlassSystem.PieceDynamics");
        if (geoData is not null)
            yield return PresenceOperation(root, 0x44, geoData, "FxMap.GlassSystem.GeoData");
        if (isInUse is not null)
            yield return PresenceOperation(root, 0x48, isInUse, "FxMap.GlassSystem.IsInUse");
        if (cellBits is not null)
            yield return PresenceOperation(root, 0x4c, cellBits, "FxMap.GlassSystem.CellBits");
        if (visData is not null)
            yield return PresenceOperation(root, 0x50, visData, "FxMap.GlassSystem.VisData");
        if (linkOrg is not null)
            yield return PresenceOperation(root, 0x54, linkOrg, "FxMap.GlassSystem.LinkOrg");
        if (halfThickness is not null)
            yield return PresenceOperation(root, 0x58, halfThickness, "FxMap.GlassSystem.HalfThickness");
        if (lightingHandles is { } lightingStorage)
            yield return Direct(root, 0x5c, lightingStorage, "FxMap.GlassSystem.LightingHandles");
        if (initialPieceStates is { } initialPieceStorage)
            yield return Direct(root, 0x60, initialPieceStorage, "FxMap.GlassSystem.InitPieceStates");
        if (initialGeoData is { } initialGeoStorage)
            yield return Direct(root, 0x64, initialGeoStorage, "FxMap.GlassSystem.InitGeoData");
    }

    private static LinkStorageTarget? FreezeDefinitions(
        IReadOnlyList<FxGlassDef> definitions,
        XPointerReference pointer,
        LinkAssetFreezeScope freeze)
    {
        if (definitions.Count == 0 && pointer.Type == PointerType.Null)
            return null;
        FrozenDefinition[] frozen = definitions
            .Select((definition, index) => FrozenDefinition.Freeze(
                definition ?? throw new InvalidDataException(
                    $"FxMap.GlassSystem.Defs[{index}] cannot be null."),
                index))
            .ToArray();
        var writer = new LinkTemplateWriter(
            checked(frozen.Length * FxGlassDef.SerializedSize));
        foreach (FrozenDefinition definition in frozen)
            writer.WriteBytes(definition.Template);
        return freeze.FreezeStorage(
            pointer,
            writer.Complete(),
            XFileBlockType.LARGE,
            alignment: 4,
            (table, addend) =>
            {
                var operations = new List<LinkOperation>();
                for (int index = 0; index < frozen.Length; index++)
                {
                    frozen[index].AppendOperations(
                        table,
                        checked(addend + index * FxGlassDef.SerializedSize),
                        operations);
                }
                return operations;
            },
            "FxMap.GlassSystem.Defs");
    }

    private static LinkStorageSymbol? FreezeRuntime(
        int count,
        int stride,
        int alignment,
        XPointerReference pointer,
        string fieldPath)
    {
        if (count == 0 && pointer.Type == PointerType.Null)
            return null;
        if (count != 0 &&
            pointer.CellAddress is not null &&
            pointer.Type == PointerType.Null)
        {
            throw new InvalidDataException(
                $"{fieldPath} retains a captured null pointer with non-empty semantic storage.");
        }
        if (pointer.CellAddress is not null &&
            pointer.Type is not (PointerType.Inline or PointerType.Insert))
        {
            throw new InvalidDataException(
                $"{fieldPath} retains an unsupported captured {pointer.Type} pointer.");
        }
        return LinkStorageSymbol.SourceFree(
            XFileBlockType.RUNTIME,
            checked(count * stride),
            alignment,
            LinkMaterializationKind.RuntimeZeroFill);
    }

    private static LinkStorageTarget? FreezeUInt16s(
        IReadOnlyList<ushort> values,
        XPointerReference pointer,
        LinkAssetFreezeScope freeze)
    {
        if (values.Count == 0 && pointer.Type == PointerType.Null)
            return null;
        var writer = new LinkTemplateWriter(checked(values.Count * sizeof(ushort)));
        foreach (ushort value in values)
            writer.WriteUInt16(value);
        return freeze.FreezeStorage(
            pointer,
            writer.Complete(),
            XFileBlockType.LARGE,
            alignment: 2,
            operations: null,
            "FxMap.GlassSystem.LightingHandles");
    }

    private static LinkStorageTarget? FreezeInitialPieceStates(
        IReadOnlyList<FxGlassInitPieceState> values,
        XPointerReference pointer,
        LinkAssetFreezeScope freeze)
    {
        if (values.Count == 0 && pointer.Type == PointerType.Null)
            return null;
        var writer = new LinkTemplateWriter(
            checked(values.Count * FxGlassInitPieceState.SerializedSize));
        for (int index = 0; index < values.Count; index++)
        {
            FxGlassInitPieceState value = values[index] ??
                throw new InvalidDataException(
                    $"FxMap.GlassSystem.InitPieceStates[{index}] cannot be null.");
            WriteFrame(writer, value.Frame);
            WriteSingle(writer, value.Radius);
            WriteVec2(writer, value.TexCoordOrigin);
            writer.WriteUInt32(value.SupportMask);
            WriteSingle(writer, value.AreaX2);
            writer.WriteByte(value.DefIndex);
            writer.WriteByte(value.VertCount);
            writer.WriteByte(value.FanDataCount);
            writer.WriteByte(value.Pad33);
        }
        return freeze.FreezeStorage(
            pointer,
            writer.Complete(),
            XFileBlockType.LARGE,
            alignment: 4,
            operations: null,
            "FxMap.GlassSystem.InitPieceStates");
    }

    private static LinkStorageTarget? FreezeInitialGeoData(
        IReadOnlyList<FxGlassGeometryData> values,
        XPointerReference pointer,
        LinkAssetFreezeScope freeze)
    {
        if (values.Count == 0 && pointer.Type == PointerType.Null)
            return null;
        var writer = new LinkTemplateWriter(
            checked(values.Count * FxGlassGeometryData.SerializedSize));
        foreach (FxGlassGeometryData value in values)
            writer.WriteUInt32(value.PackedValue);
        return freeze.FreezeStorage(
            pointer,
            writer.Complete(),
            XFileBlockType.LARGE,
            alignment: 4,
            operations: null,
            "FxMap.GlassSystem.InitGeoData");
    }

    private static void ValidateReferenceShape(FxGlassSystem glass)
    {
        bool nonzero =
            glass.Time != 0 ||
            glass.PrevTime != 0 ||
            glass.DefCount != 0 ||
            glass.PieceLimit != 0 ||
            glass.PieceWordCount != 0 ||
            glass.InitPieceCount != 0 ||
            glass.CellCount != 0 ||
            glass.ActivePieceCount != 0 ||
            glass.FirstFreePiece != 0 ||
            glass.GeoDataLimit != 0 ||
            glass.GeoDataCount != 0 ||
            glass.InitGeoDataCount != 0 ||
            glass.NeedToCompactData != 0 ||
            glass.InitCount != 0 ||
            glass.Pad66 != 0 ||
            BitConverter.SingleToInt32Bits(glass.EffectChanceAccum) != 0 ||
            glass.LastPieceDeletionTime != 0;
        if (nonzero ||
            glass.DefsPointer.Raw != 0 ||
            glass.PiecePlacesPointer.Raw != 0 ||
            glass.PieceStatesPointer.Raw != 0 ||
            glass.PieceDynamicsPointer.Raw != 0 ||
            glass.GeoDataPointer.Raw != 0 ||
            glass.IsInUsePointer.Raw != 0 ||
            glass.CellBitsPointer.Raw != 0 ||
            glass.VisDataPointer.Raw != 0 ||
            glass.LinkOrgPointer.Raw != 0 ||
            glass.HalfThicknessPointer.Raw != 0 ||
            glass.LightingHandlesPointer.Raw != 0 ||
            glass.InitPieceStatesPointer.Raw != 0 ||
            glass.InitGeoDataPointer.Raw != 0 ||
            glass.Defs.Count != 0 ||
            glass.PiecePlaces.Count != 0 ||
            glass.PieceStates.Count != 0 ||
            glass.PieceDynamics.Count != 0 ||
            glass.GeoData.Count != 0 ||
            glass.IsInUse.Count != 0 ||
            glass.CellBits.Count != 0 ||
            glass.VisData.Count != 0 ||
            glass.LinkOrg.Count != 0 ||
            glass.HalfThickness.Count != 0 ||
            glass.LightingHandles.Count != 0 ||
            glass.InitPieceStates.Count != 0 ||
            glass.InitGeoData.Count != 0)
        {
            throw new InvalidDataException(
                "A comma-prefixed FxMap provider must have a zeroed reference body.");
        }
    }

    private static void WriteFrame(LinkTemplateWriter writer, FxSpatialFrame frame)
    {
        WriteSingle(writer, frame.Quat.X);
        WriteSingle(writer, frame.Quat.Y);
        WriteSingle(writer, frame.Quat.Z);
        WriteSingle(writer, frame.Quat.W);
        WriteVec3(writer, frame.Origin);
    }

    private static void WriteVec2(LinkTemplateWriter writer, FxVec2 value)
    {
        WriteSingle(writer, value.X);
        WriteSingle(writer, value.Y);
    }

    private static void WriteVec3(LinkTemplateWriter writer, FxVec3 value)
    {
        WriteSingle(writer, value.X);
        WriteSingle(writer, value.Y);
        WriteSingle(writer, value.Z);
    }

    private static void WriteSingle(LinkTemplateWriter writer, float value) =>
        writer.WriteInt32(BitConverter.SingleToInt32Bits(value));

    private static DirectStorageLinkOperation Direct(
        LinkStorageSymbol owner,
        int pointerOffset,
        LinkStorageTarget target,
        string fieldPath) =>
        new(
            new LinkStorageCell(owner, pointerOffset),
            target.View,
            target.CanMaterializeRoot,
            fieldPath);

    private static bool IsZero(float value) =>
        BitConverter.SingleToInt32Bits(value) == 0;

    private static bool IsZero(FxVec2 value) =>
        IsZero(value.X) && IsZero(value.Y);

    private static bool IsZero(FxVec3 value) =>
        IsZero(value.X) && IsZero(value.Y) && IsZero(value.Z);

    private static bool IsZero(FxSpatialFrame value) =>
        IsZero(value.Quat.X) &&
        IsZero(value.Quat.Y) &&
        IsZero(value.Quat.Z) &&
        IsZero(value.Quat.W) &&
        IsZero(value.Origin);

    private sealed class FrozenDefinition
    {
        private FrozenDefinition(
            byte[] template,
            AssetDependency? physPreset,
            AssetDependency? material,
            AssetDependency? shatteredMaterial,
            int index)
        {
            Template = template;
            PhysPreset = physPreset;
            Material = material;
            ShatteredMaterial = shatteredMaterial;
            Index = index;
        }

        public byte[] Template { get; }
        private AssetDependency? PhysPreset { get; }
        private AssetDependency? Material { get; }
        private AssetDependency? ShatteredMaterial { get; }
        private int Index { get; }

        public static FrozenDefinition Freeze(FxGlassDef definition, int index)
        {
            string path = $"FxMap.GlassSystem.Defs[{index}]";
            IReadOnlyList<FxVec2> texVecs = definition.TexVecs ??
                throw new InvalidDataException($"{path}.TexVecs cannot be null.");
            if (texVecs.Count != 2)
                throw new InvalidDataException($"{path}.TexVecs requires exactly two rows.");
            AssetDependency? physPreset = FreezeProviderDependency(
                definition.PhysPresetPointer.Untyped,
                definition.PhysPreset,
                XAssetType.PhysPreset,
                $"{path}.PhysPreset");
            AssetDependency? material = FreezeProviderDependency(
                definition.MaterialPointer.Untyped,
                definition.Material,
                XAssetType.Material,
                $"{path}.Material");
            AssetDependency? shattered = FreezeProviderDependency(
                definition.MaterialShatteredPointer.Untyped,
                definition.MaterialShattered,
                XAssetType.Material,
                $"{path}.MaterialShattered");
            var writer = new LinkTemplateWriter(FxGlassDef.SerializedSize);
            WriteSingle(writer, definition.HalfThickness);
            WriteVec2(writer, texVecs[0]);
            WriteVec2(writer, texVecs[1]);
            writer.WriteUInt32(definition.Color);
            writer.Skip(3 * sizeof(int));
            WriteSingle(writer, definition.InvHighMipRadius);
            WriteSingle(writer, definition.ShatteredInvHighMipRadius);
            return new FrozenDefinition(
                writer.Complete(),
                physPreset,
                material,
                shattered,
                index);
        }

        public void AppendOperations(
            LinkStorageSymbol table,
            int baseOffset,
            ICollection<LinkOperation> operations)
        {
            if (PhysPreset is { } physPreset)
            {
                operations.Add(ProviderOperation(
                    table,
                    checked(baseOffset + 0x20),
                    physPreset));
            }
            if (Material is { } material)
            {
                operations.Add(ProviderOperation(
                    table,
                    checked(baseOffset + 0x18),
                    material));
            }
            if (ShatteredMaterial is { } shattered)
            {
                operations.Add(ProviderOperation(
                    table,
                    checked(baseOffset + 0x1c),
                    shattered));
            }
        }
    }

    private sealed class FrozenGlass
    {
        private FrozenGlass(
            LinkStorageTarget? definitions,
            LinkStorageSymbol? piecePlaces,
            LinkStorageSymbol? pieceStates,
            LinkStorageSymbol? pieceDynamics,
            LinkStorageSymbol? geoData,
            LinkStorageSymbol? isInUse,
            LinkStorageSymbol? cellBits,
            LinkStorageSymbol? visData,
            LinkStorageSymbol? linkOrg,
            LinkStorageSymbol? halfThickness,
            LinkStorageTarget? lightingHandles,
            LinkStorageTarget? initialPieceStates,
            LinkStorageTarget? initialGeoData)
        {
            Definitions = definitions;
            PiecePlaces = piecePlaces;
            PieceStates = pieceStates;
            PieceDynamics = pieceDynamics;
            GeoData = geoData;
            IsInUse = isInUse;
            CellBits = cellBits;
            VisData = visData;
            LinkOrg = linkOrg;
            HalfThickness = halfThickness;
            LightingHandles = lightingHandles;
            InitialPieceStates = initialPieceStates;
            InitialGeoData = initialGeoData;
        }

        public LinkStorageTarget? Definitions { get; }
        public LinkStorageSymbol? PiecePlaces { get; }
        public LinkStorageSymbol? PieceStates { get; }
        public LinkStorageSymbol? PieceDynamics { get; }
        public LinkStorageSymbol? GeoData { get; }
        public LinkStorageSymbol? IsInUse { get; }
        public LinkStorageSymbol? CellBits { get; }
        public LinkStorageSymbol? VisData { get; }
        public LinkStorageSymbol? LinkOrg { get; }
        public LinkStorageSymbol? HalfThickness { get; }
        public LinkStorageTarget? LightingHandles { get; }
        public LinkStorageTarget? InitialPieceStates { get; }
        public LinkStorageTarget? InitialGeoData { get; }

        public static FrozenGlass Freeze(
            FxGlassSystem glass,
            LinkAssetFreezeScope freeze)
        {
            IReadOnlyList<FxGlassDef> definitions = Require(
                glass.Defs,
                "FxMap.GlassSystem.Defs");
            IReadOnlyList<FxGlassPiecePlace> piecePlaces = Require(
                glass.PiecePlaces,
                "FxMap.GlassSystem.PiecePlaces");
            IReadOnlyList<FxGlassPieceState> pieceStates = Require(
                glass.PieceStates,
                "FxMap.GlassSystem.PieceStates");
            IReadOnlyList<FxGlassPieceDynamics> pieceDynamics = Require(
                glass.PieceDynamics,
                "FxMap.GlassSystem.PieceDynamics");
            IReadOnlyList<FxGlassGeometryData> geoData = Require(
                glass.GeoData,
                "FxMap.GlassSystem.GeoData");
            IReadOnlyList<uint> isInUse = Require(
                glass.IsInUse,
                "FxMap.GlassSystem.IsInUse");
            IReadOnlyList<uint> cellBits = Require(
                glass.CellBits,
                "FxMap.GlassSystem.CellBits");
            IReadOnlyList<byte> visData = Require(
                glass.VisData,
                "FxMap.GlassSystem.VisData");
            IReadOnlyList<FxVec3> linkOrg = Require(
                glass.LinkOrg,
                "FxMap.GlassSystem.LinkOrg");
            IReadOnlyList<float> halfThickness = Require(
                glass.HalfThickness,
                "FxMap.GlassSystem.HalfThickness");
            IReadOnlyList<ushort> lighting = Require(
                glass.LightingHandles,
                "FxMap.GlassSystem.LightingHandles");
            IReadOnlyList<FxGlassInitPieceState> initialPieces = Require(
                glass.InitPieceStates,
                "FxMap.GlassSystem.InitPieceStates");
            IReadOnlyList<FxGlassGeometryData> initialGeo = Require(
                glass.InitGeoData,
                "FxMap.GlassSystem.InitGeoData");

            int defCount = Count(glass.DefCount, "FxMap.GlassSystem.DefCount");
            int pieceLimit = Count(glass.PieceLimit, "FxMap.GlassSystem.PieceLimit");
            int pieceWordCount = Count(
                glass.PieceWordCount,
                "FxMap.GlassSystem.PieceWordCount");
            int initPieceCount = Count(
                glass.InitPieceCount,
                "FxMap.GlassSystem.InitPieceCount");
            int cellCount = Count(glass.CellCount, "FxMap.GlassSystem.CellCount");
            int geoDataLimit = Count(
                glass.GeoDataLimit,
                "FxMap.GlassSystem.GeoDataLimit");
            int initGeoCount = Count(
                glass.InitGeoDataCount,
                "FxMap.GlassSystem.InitGeoDataCount");
            int cellBitCount = Product(
                cellCount,
                pieceWordCount,
                "FxMap.GlassSystem.CellBits");
            int visCount = Align(
                pieceLimit,
                16,
                "FxMap.GlassSystem.VisData");
            int halfThicknessCount = Align(
                pieceLimit,
                4,
                "FxMap.GlassSystem.HalfThickness");

            RequireCount(defCount, definitions.Count, "FxMap.GlassSystem.Defs");
            RequireCount(pieceLimit, piecePlaces.Count, "FxMap.GlassSystem.PiecePlaces");
            RequireCount(pieceLimit, pieceStates.Count, "FxMap.GlassSystem.PieceStates");
            RequireCount(pieceLimit, pieceDynamics.Count, "FxMap.GlassSystem.PieceDynamics");
            RequireCount(geoDataLimit, geoData.Count, "FxMap.GlassSystem.GeoData");
            RequireCount(pieceWordCount, isInUse.Count, "FxMap.GlassSystem.IsInUse");
            RequireCount(cellBitCount, cellBits.Count, "FxMap.GlassSystem.CellBits");
            RequireCount(visCount, visData.Count, "FxMap.GlassSystem.VisData");
            RequireCount(pieceLimit, linkOrg.Count, "FxMap.GlassSystem.LinkOrg");
            RequireCount(
                halfThicknessCount,
                halfThickness.Count,
                "FxMap.GlassSystem.HalfThickness");
            RequireCount(initPieceCount, lighting.Count, "FxMap.GlassSystem.LightingHandles");
            RequireCount(initPieceCount, initialPieces.Count, "FxMap.GlassSystem.InitPieceStates");
            RequireCount(initGeoCount, initialGeo.Count, "FxMap.GlassSystem.InitGeoData");
            if (glass.GeoDataCount > glass.GeoDataLimit)
            {
                throw new InvalidDataException(
                    "FxMap.GlassSystem.GeoDataCount cannot exceed GeoDataLimit.");
            }
            if (glass.ActivePieceCount > glass.PieceLimit)
            {
                throw new InvalidDataException(
                    "FxMap.GlassSystem.ActivePieceCount cannot exceed PieceLimit.");
            }
            ValidateRuntime(piecePlaces, pieceStates, pieceDynamics, geoData,
                isInUse, cellBits, visData, linkOrg, halfThickness);

            return new FrozenGlass(
                FreezeDefinitions(definitions, glass.DefsPointer.Untyped, freeze),
                FreezeRuntime(
                    piecePlaces.Count,
                    FxGlassPiecePlace.SerializedSize,
                    4,
                    glass.PiecePlacesPointer.Untyped,
                    "FxMap.GlassSystem.PiecePlaces"),
                FreezeRuntime(
                    pieceStates.Count,
                    FxGlassPieceState.SerializedSize,
                    4,
                    glass.PieceStatesPointer.Untyped,
                    "FxMap.GlassSystem.PieceStates"),
                FreezeRuntime(
                    pieceDynamics.Count,
                    FxGlassPieceDynamics.SerializedSize,
                    4,
                    glass.PieceDynamicsPointer.Untyped,
                    "FxMap.GlassSystem.PieceDynamics"),
                FreezeRuntime(
                    geoData.Count,
                    FxGlassGeometryData.SerializedSize,
                    4,
                    glass.GeoDataPointer.Untyped,
                    "FxMap.GlassSystem.GeoData"),
                FreezeRuntime(
                    isInUse.Count,
                    sizeof(uint),
                    4,
                    glass.IsInUsePointer.Untyped,
                    "FxMap.GlassSystem.IsInUse"),
                FreezeRuntime(
                    cellBits.Count,
                    sizeof(uint),
                    4,
                    glass.CellBitsPointer.Untyped,
                    "FxMap.GlassSystem.CellBits"),
                FreezeRuntime(
                    visData.Count,
                    sizeof(byte),
                    16,
                    glass.VisDataPointer.Untyped,
                    "FxMap.GlassSystem.VisData"),
                FreezeRuntime(
                    linkOrg.Count,
                    0x0c,
                    4,
                    glass.LinkOrgPointer.Untyped,
                    "FxMap.GlassSystem.LinkOrg"),
                FreezeRuntime(
                    halfThickness.Count,
                    sizeof(float),
                    16,
                    glass.HalfThicknessPointer.Untyped,
                    "FxMap.GlassSystem.HalfThickness"),
                FreezeUInt16s(lighting, glass.LightingHandlesPointer.Untyped, freeze),
                FreezeInitialPieceStates(
                    initialPieces,
                    glass.InitPieceStatesPointer.Untyped,
                    freeze),
                FreezeInitialGeoData(
                    initialGeo,
                    glass.InitGeoDataPointer.Untyped,
                    freeze));
        }

        private static void ValidateRuntime(
            IReadOnlyList<FxGlassPiecePlace> piecePlaces,
            IReadOnlyList<FxGlassPieceState> pieceStates,
            IReadOnlyList<FxGlassPieceDynamics> pieceDynamics,
            IReadOnlyList<FxGlassGeometryData> geoData,
            IReadOnlyList<uint> isInUse,
            IReadOnlyList<uint> cellBits,
            IReadOnlyList<byte> visData,
            IReadOnlyList<FxVec3> linkOrg,
            IReadOnlyList<float> halfThickness)
        {
            for (int index = 0; index < piecePlaces.Count; index++)
            {
                FxGlassPiecePlace value = piecePlaces[index] ??
                    throw new InvalidDataException(
                        $"FxMap.GlassSystem.PiecePlaces[{index}] cannot be null.");
                if (!IsZero(value.Frame) || !IsZero(value.Radius) || value.NextFree != 0)
                    throw RuntimeError("PiecePlaces");
            }
            for (int index = 0; index < pieceStates.Count; index++)
            {
                FxGlassPieceState value = pieceStates[index] ??
                    throw new InvalidDataException(
                        $"FxMap.GlassSystem.PieceStates[{index}] cannot be null.");
                IReadOnlyList<byte> pad = value.Pad11 ??
                    throw new InvalidDataException(
                        $"FxMap.GlassSystem.PieceStates[{index}].Pad11 cannot be null.");
                if (pad.Count != 5 || pad.Any(item => item != 0) ||
                    !IsZero(value.TexCoordOrigin) || value.SupportMask != 0 ||
                    value.InitIndex != 0 || value.GeoDataStart != 0 ||
                    value.DefIndex != 0 || value.VertCount != 0 ||
                    value.HoleDataCount != 0 || value.CrackDataCount != 0 ||
                    value.FanDataCount != 0 || value.Flags != 0 ||
                    !IsZero(value.AreaX2))
                {
                    throw RuntimeError("PieceStates");
                }
            }
            for (int index = 0; index < pieceDynamics.Count; index++)
            {
                FxGlassPieceDynamics value = pieceDynamics[index] ??
                    throw new InvalidDataException(
                        $"FxMap.GlassSystem.PieceDynamics[{index}] cannot be null.");
                if (value.FallTime != 0 || value.PhysObjId != 0 ||
                    value.PhysJointId != 0 || !IsZero(value.Vel) ||
                    !IsZero(value.AVel))
                {
                    throw RuntimeError("PieceDynamics");
                }
            }
            if (geoData.Any(value => value.PackedValue != 0))
                throw RuntimeError("GeoData");
            if (isInUse.Any(value => value != 0))
                throw RuntimeError("IsInUse");
            if (cellBits.Any(value => value != 0))
                throw RuntimeError("CellBits");
            if (visData.Any(value => value != 0))
                throw RuntimeError("VisData");
            if (linkOrg.Any(value => !IsZero(value)))
                throw RuntimeError("LinkOrg");
            if (halfThickness.Any(value => !IsZero(value)))
                throw RuntimeError("HalfThickness");
        }

        private static InvalidDataException RuntimeError(string field) => new(
            $"FxMap.GlassSystem.{field} is RUNTIME storage and must be bitwise zero-filled.");

        private static IReadOnlyList<T> Require<T>(
            IReadOnlyList<T>? value,
            string fieldPath) => value ?? throw new InvalidDataException(
                $"{fieldPath} cannot be null.");

        private static int Count(uint value, string fieldPath)
        {
            if (value > int.MaxValue)
                throw new InvalidDataException($"{fieldPath} exceeds Int32.");
            return (int)value;
        }

        private static int Product(int left, int right, string fieldPath)
        {
            try
            {
                return checked(left * right);
            }
            catch (OverflowException exception)
            {
                throw new InvalidDataException($"{fieldPath} count exceeds Int32.", exception);
            }
        }

        private static int Align(int value, int alignment, string fieldPath)
        {
            try
            {
                return checked((value + alignment - 1) / alignment * alignment);
            }
            catch (OverflowException exception)
            {
                throw new InvalidDataException($"{fieldPath} aligned count exceeds Int32.", exception);
            }
        }

        private static void RequireCount(
            int expected,
            int actual,
            string fieldPath)
        {
            if (expected != actual)
            {
                throw new InvalidDataException(
                    $"{fieldPath} contains {actual} row(s), but its native count requires {expected}.");
            }
        }
    }
}
