using System.Text;
using IW4.Game.Assets.FxMap;
using IW4.Game.Assets.GameMap;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.Physics;
using IW4.Game.ScriptStrings;
using IW4.Game.Zone;

namespace IW4.Formats.Codecs.D3dbsp;

/// <summary>Unversioned IW4Studio glass extension for a v22 d3dbsp.</summary>
public static class D3dbspGlassCodec
{
    // Limits allocation from an untrusted extension header; this is not an engine layout constant.
    private const int MaximumRuntimeCapacity = 1_000_000;

    public static byte[] Encode(FxGlassSystem fx, GGlassData game, IReadOnlyList<ushort> brushIndices)
    {
        ArgumentNullException.ThrowIfNull(fx);
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(brushIndices);
        ValidateFx(fx);
        ValidateRelationships(fx, game, brushIndices);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(fx.DefCount);
        writer.Write(fx.PieceLimit);
        writer.Write(fx.PieceWordCount);
        writer.Write(fx.InitPieceCount);
        writer.Write(fx.CellCount);
        writer.Write(fx.FirstFreePiece);
        writer.Write(fx.GeoDataLimit);
        writer.Write(fx.InitGeoDataCount);
        writer.Write(fx.InitCount);
        writer.Write(fx.Pad66);
        foreach (FxGlassDef definition in fx.Defs)
        {
            if (definition is null || definition.TexVecs is null || definition.TexVecs.Count != 2)
                throw new InvalidDataException("A glass definition is null or has an invalid texture-vector table.");
            ValidateProvider(definition.MaterialPointer.Raw, definition.Material?.SerializedAssetName, "material");
            ValidateProvider(definition.MaterialShatteredPointer.Raw, definition.MaterialShattered?.SerializedAssetName, "shattered material");
            ValidateProvider(definition.PhysPresetPointer.Raw, definition.PhysPreset?.SerializedAssetName, "physics preset");
            writer.Write(definition.HalfThickness);
            WriteVec2(writer, definition.TexVecs[0]);
            WriteVec2(writer, definition.TexVecs[1]);
            writer.Write(definition.Color);
            WriteName(writer, definition.Material?.SerializedAssetName);
            WriteName(writer, definition.MaterialShattered?.SerializedAssetName);
            WriteName(writer, definition.PhysPreset?.SerializedAssetName);
            writer.Write(definition.InvHighMipRadius);
            writer.Write(definition.ShatteredInvHighMipRadius);
        }
        foreach (FxGlassInitPieceState piece in fx.InitPieceStates)
        {
            if (piece is null)
                throw new InvalidDataException("A glass initial piece is null.");
            WriteFrame(writer, piece.Frame);
            writer.Write(piece.Radius);
            WriteVec2(writer, piece.TexCoordOrigin);
            writer.Write(piece.SupportMask);
            writer.Write(piece.AreaX2);
            writer.Write(piece.DefIndex);
            writer.Write(piece.VertCount);
            writer.Write(piece.FanDataCount);
            writer.Write(piece.Pad33);
        }
        foreach (FxGlassGeometryData value in fx.InitGeoData)
            writer.Write(value.PackedValue);
        foreach (ushort value in fx.LightingHandles)
            writer.Write(value);

        writer.Write(game.PieceCount);
        writer.Write(game.DamageToWeaken);
        writer.Write(game.DamageToDestroy);
        writer.Write(game.GlassNameCount);
        writer.Write(game.Pad14To7F.Count);
        foreach (byte value in game.Pad14To7F)
            writer.Write(value);
        foreach (GGlassPiece piece in game.GlassPieces)
        {
            if (piece is null)
                throw new InvalidDataException("A GameWorld glass piece is null.");
            writer.Write(piece.DamageTaken);
            writer.Write(piece.CollapseTime);
            writer.Write(piece.LastStateChangeTime);
            writer.Write(piece.PackedImpactDir);
            writer.Write(piece.PackedImpactPos);
        }
        foreach (GGlassName name in game.GlassNames)
        {
            if (name is null || name.Name is null || name.PieceIndices is null ||
                name.PieceCount != name.PieceIndices.Count ||
                name.PieceIndices.Any(index => index >= game.PieceCount))
                throw new InvalidDataException("A GameWorld glass name has invalid piece indices.");
            if (name.NameStrPointer.Raw != 0 && name.NameStr is null)
                throw new InvalidDataException("A GameWorld glass name has an unresolved name string.");
            WriteName(writer, name.NameStr);
            writer.Write(name.Name.RawLocalIndex);
            WriteName(writer, name.Name.Text);
            writer.Write(name.PieceCount);
            foreach (ushort index in name.PieceIndices)
                writer.Write(index);
        }
        writer.Write(brushIndices.Count);
        foreach (ushort index in brushIndices)
            writer.Write(index);
        return stream.ToArray();
    }

    public static (FxGlassSystem Fx, GGlassData Game, ushort[] BrushIndices) Decode(
        ReadOnlySpan<byte> data, int expectedBrushCount, int expectedCellCount)
    {
        if (expectedBrushCount < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedBrushCount));
        if (expectedCellCount < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedCellCount));
        if (data.IsEmpty)
            return (new FxGlassSystem(), new GGlassData(), new ushort[expectedBrushCount]);
        using var stream = new MemoryStream(data.ToArray(), writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        try
        {
            uint defCount = reader.ReadUInt32();
            uint pieceLimit = reader.ReadUInt32();
            uint pieceWordCount = reader.ReadUInt32();
            uint initPieceCount = reader.ReadUInt32();
            uint cellCount = reader.ReadUInt32();
            uint firstFreePiece = reader.ReadUInt32();
            uint geoDataLimit = reader.ReadUInt32();
            uint initGeoDataCount = reader.ReadUInt32();
            byte initCount = reader.ReadByte();
            ushort pad66 = reader.ReadUInt16();
            if (defCount > byte.MaxValue + 1 || initPieceCount > ushort.MaxValue ||
                pieceLimit > ushort.MaxValue ||
                pieceLimit > MaximumRuntimeCapacity || pieceWordCount > MaximumRuntimeCapacity ||
                cellCount > MaximumRuntimeCapacity || geoDataLimit > MaximumRuntimeCapacity ||
                (ulong)cellCount * pieceWordCount > MaximumRuntimeCapacity)
                throw new InvalidDataException("The glass runtime capacity exceeds native indices or the codec allocation limit.");
            if (initPieceCount > pieceLimit || initGeoDataCount > geoDataLimit ||
                (pieceLimit != 0 && (ulong)pieceWordCount * 32 < pieceLimit) ||
                (cellCount != 0 && cellCount != expectedCellCount) ||
                ((defCount != 0 || pieceLimit != 0 || initPieceCount != 0) &&
                    cellCount != expectedCellCount))
                throw new InvalidDataException("The glass capacities are inconsistent with the source world.");
            int defsLength = Count(defCount, 24, stream, "glass definitions");
            var defs = new FxGlassDef[defsLength];
            for (int index = 0; index < defs.Length; index++)
            {
                float thickness = reader.ReadSingle();
                FxVec2 first = ReadVec2(reader);
                FxVec2 second = ReadVec2(reader);
                uint color = reader.ReadUInt32();
                string? material = ReadName(reader);
                string? shattered = ReadName(reader);
                string? physics = ReadName(reader);
                defs[index] = new FxGlassDef
                {
                    HalfThickness = thickness,
                    TexVecs = [first, second],
                    Color = color,
                    Material = material is null ? null : new MaterialAsset { Info = new MaterialInfo { Name = ReferenceName(material) } },
                    MaterialShattered = shattered is null ? null : new MaterialAsset { Info = new MaterialInfo { Name = ReferenceName(shattered) } },
                    PhysPreset = physics is null ? null : new PhysPresetAsset { Name = ReferenceName(physics) },
                    InvHighMipRadius = reader.ReadSingle(),
                    ShatteredInvHighMipRadius = reader.ReadSingle()
                };
            }
            int initialLength = Count(initPieceCount, FxGlassInitPieceState.SerializedSize, stream, "initial glass pieces");
            var initial = new FxGlassInitPieceState[initialLength];
            for (int index = 0; index < initial.Length; index++)
                initial[index] = new FxGlassInitPieceState
                {
                    Frame = ReadFrame(reader), Radius = reader.ReadSingle(),
                    TexCoordOrigin = ReadVec2(reader), SupportMask = reader.ReadUInt32(),
                    AreaX2 = reader.ReadSingle(), DefIndex = reader.ReadByte(),
                    VertCount = reader.ReadByte(), FanDataCount = reader.ReadByte(),
                    Pad33 = reader.ReadByte()
                };
            int geoLength = Count(initGeoDataCount, 4, stream, "initial glass geometry");
            var geometry = new FxGlassGeometryData[geoLength];
            for (int index = 0; index < geometry.Length; index++)
                geometry[index] = new FxGlassGeometryData(reader.ReadUInt32());
            Count(initPieceCount, 2, stream, "glass lighting handles");
            var lighting = new ushort[initialLength];
            for (int index = 0; index < lighting.Length; index++)
                lighting[index] = reader.ReadUInt16();
            int gamePieceCount = BoundedCount(reader.ReadInt32(), GGlassPiece.SerializedSize, stream, "GameWorld glass pieces");
            ushort weaken = reader.ReadUInt16();
            ushort destroy = reader.ReadUInt16();
            int gameNameCount = BoundedCount(reader.ReadInt32(), 10, stream, "GameWorld glass names");
            int padLength = reader.ReadInt32();
            if (padLength is not (0 or 0x6c))
                throw new InvalidDataException("Invalid GameWorld glass padding length.");
            byte[] padding = reader.ReadBytes(padLength);
            if (padding.Length != padLength)
                throw new EndOfStreamException();
            var gamePieces = new GGlassPiece[gamePieceCount];
            for (int index = 0; index < gamePieces.Length; index++)
                gamePieces[index] = new GGlassPiece
                {
                    DamageTaken = reader.ReadUInt16(), CollapseTime = reader.ReadUInt16(),
                    LastStateChangeTime = reader.ReadInt32(), PackedImpactDir = reader.ReadUInt16(),
                    PackedImpactPos = reader.ReadUInt16()
                };
            var gameNames = new GGlassName[gameNameCount];
            for (int index = 0; index < gameNames.Length; index++)
            {
                string? nameString = ReadName(reader);
                ushort localIndex = reader.ReadUInt16();
                string? text = ReadName(reader);
                ushort pieceCount = reader.ReadUInt16();
                Count(pieceCount, 2, stream, "glass name indices");
                var indices = new ushort[pieceCount];
                for (int piece = 0; piece < indices.Length; piece++)
                {
                    indices[piece] = reader.ReadUInt16();
                    if (indices[piece] >= gamePieceCount)
                        throw new InvalidDataException("A glass name references a missing GameWorld piece.");
                }
                gameNames[index] = new GGlassName
                {
                    NameStr = nameString,
                    Name = new ScriptStringReference(localIndex, text, ScriptStringHandle.Null, default),
                    PieceCount = pieceCount,
                    PieceIndices = indices
                };
            }
            int brushCount = BoundedCount(reader.ReadInt32(), 2, stream, "glass brush indices");
            if (brushCount != expectedBrushCount)
                throw new InvalidDataException($"Glass brush index count {brushCount} differs from brush count {expectedBrushCount}.");
            var brushIndices = new ushort[brushCount];
            for (int index = 0; index < brushIndices.Length; index++)
                brushIndices[index] = reader.ReadUInt16();
            if (stream.Position != stream.Length)
                throw new InvalidDataException("The glass lump has trailing data.");
            var fx = new FxGlassSystem
            {
                DefCount = defCount, PieceLimit = pieceLimit, PieceWordCount = pieceWordCount,
                InitPieceCount = initPieceCount, CellCount = cellCount,
                FirstFreePiece = firstFreePiece, GeoDataLimit = geoDataLimit,
                InitGeoDataCount = initGeoDataCount, InitCount = initCount, Pad66 = pad66,
                Defs = defs, InitPieceStates = initial, InitGeoData = geometry,
                LightingHandles = lighting,
                PiecePlaces = Enumerable.Range(0, CheckedCount(pieceLimit)).Select(_ => new FxGlassPiecePlace(default, 0, 0)).ToArray(),
                PieceStates = Enumerable.Range(0, CheckedCount(pieceLimit)).Select(_ => new FxGlassPieceState { Pad11 = new byte[5] }).ToArray(),
                PieceDynamics = Enumerable.Range(0, CheckedCount(pieceLimit)).Select(_ => new FxGlassPieceDynamics(0, 0, 0, default, default)).ToArray(),
                GeoData = new FxGlassGeometryData[CheckedCount(geoDataLimit)],
                IsInUse = new uint[CheckedCount(pieceWordCount)],
                CellBits = new uint[checked(CheckedCount(cellCount) * CheckedCount(pieceWordCount))],
                VisData = new byte[Align(CheckedCount(pieceLimit), 16)],
                LinkOrg = new FxVec3[CheckedCount(pieceLimit)],
                HalfThickness = new float[Align(CheckedCount(pieceLimit), 4)]
            };
            var game = new GGlassData
            {
                PieceCount = gamePieceCount, DamageToWeaken = weaken, DamageToDestroy = destroy,
                GlassNameCount = gameNameCount, Pad14To7F = padding,
                GlassPieces = gamePieces, GlassNames = gameNames
            };
            ValidateFx(fx);
            ValidateRelationships(fx, game, brushIndices);
            return (fx, game, brushIndices);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("The glass lump is truncated.", exception);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("The glass lump declares an overflowing capacity.", exception);
        }
    }

    public static bool IsCanonicalEmpty(FxGlassSystem fx, GGlassData game, IReadOnlyList<ushort> brushIndices)
    {
        ArgumentNullException.ThrowIfNull(fx);
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(brushIndices);
        ValidateFx(fx);
        ValidateRelationships(fx, game, brushIndices);
        return fx.Time == 0 && fx.PrevTime == 0 && fx.DefCount == 0 &&
            fx.PieceLimit == 0 && fx.PieceWordCount == 0 && fx.InitPieceCount == 0 &&
            fx.CellCount == 0 && fx.ActivePieceCount == 0 && fx.FirstFreePiece == 0 &&
            fx.GeoDataLimit == 0 && fx.GeoDataCount == 0 && fx.InitGeoDataCount == 0 &&
            fx.NeedToCompactData == 0 && fx.InitCount == 0 && fx.Pad66 == 0 &&
            BitConverter.SingleToInt32Bits(fx.EffectChanceAccum) == 0 &&
            fx.LastPieceDeletionTime == 0 && fx.Defs.Count == 0 &&
            fx.PiecePlaces.Count == 0 && fx.PieceStates.Count == 0 &&
            fx.PieceDynamics.Count == 0 && fx.GeoData.Count == 0 &&
            fx.IsInUse.Count == 0 && fx.CellBits.Count == 0 &&
            fx.VisData.Count == 0 && fx.LinkOrg.Count == 0 &&
            fx.HalfThickness.Count == 0 && fx.LightingHandles.Count == 0 &&
            fx.InitPieceStates.Count == 0 && fx.InitGeoData.Count == 0 &&
            game.PieceCount == 0 && game.GlassPieces.Count == 0 &&
            game.GlassNameCount == 0 && game.GlassNames.Count == 0 &&
            game.DamageToWeaken == 0 && game.DamageToDestroy == 0 &&
            game.Pad14To7F.All(value => value == 0) &&
            brushIndices.All(index => index == 0);
    }

    private static void ValidateRelationships(
        FxGlassSystem fx, GGlassData game, IReadOnlyList<ushort> brushIndices)
    {
        if (game.GlassPieces is null || game.GlassNames is null || game.Pad14To7F is null ||
            game.PieceCount < 0 || game.PieceCount > ushort.MaxValue ||
            game.PieceCount != game.GlassPieces.Count ||
            game.GlassNameCount < 0 || game.GlassNameCount != game.GlassNames.Count ||
            game.Pad14To7F.Count is not (0 or 0x6c))
            throw new InvalidDataException("GameWorld glass counts or padding are inconsistent.");
        if (fx.InitPieceCount != game.PieceCount ||
            fx.InitPieceStates.Any(piece => piece is null || piece.DefIndex >= fx.DefCount) ||
            fx.InitPieceStates.Sum(piece => (long)piece.VertCount + piece.FanDataCount) != fx.InitGeoDataCount ||
            brushIndices.Any(index => index != 0 && (index > fx.InitPieceCount || index > game.PieceCount)))
            throw new InvalidDataException("Glass definitions, initial geometry, GameWorld pieces, or brush indices disagree.");
    }

    private static void ValidateFx(FxGlassSystem fx)
    {
        if (fx.Defs is null || fx.InitPieceStates is null || fx.InitGeoData is null ||
            fx.LightingHandles is null || fx.PiecePlaces is null || fx.PieceStates is null ||
            fx.PieceDynamics is null || fx.GeoData is null || fx.IsInUse is null ||
            fx.CellBits is null || fx.VisData is null || fx.LinkOrg is null ||
            fx.HalfThickness is null)
            throw new InvalidDataException("FxWorld glass contains a null table.");
        if (fx.DefCount > byte.MaxValue + 1 || fx.InitPieceCount > ushort.MaxValue ||
            fx.PieceLimit > ushort.MaxValue || fx.InitPieceCount > fx.PieceLimit ||
            fx.InitGeoDataCount > fx.GeoDataLimit ||
            (fx.PieceLimit != 0 && (ulong)fx.PieceWordCount * 32 < fx.PieceLimit) ||
            fx.PieceWordCount > MaximumRuntimeCapacity || fx.CellCount > MaximumRuntimeCapacity ||
            fx.GeoDataLimit > MaximumRuntimeCapacity ||
            (ulong)fx.CellCount * fx.PieceWordCount > MaximumRuntimeCapacity)
            throw new InvalidDataException("FxWorld glass capacities exceed native indices or the codec allocation limit.");
        if (fx.DefCount != fx.Defs.Count || fx.InitPieceCount != fx.InitPieceStates.Count ||
            fx.InitGeoDataCount != fx.InitGeoData.Count || fx.InitPieceCount != fx.LightingHandles.Count ||
            fx.PieceLimit != fx.PiecePlaces.Count || fx.PieceLimit != fx.PieceStates.Count ||
            fx.PieceLimit != fx.PieceDynamics.Count || fx.GeoDataLimit != fx.GeoData.Count ||
            fx.PieceWordCount != fx.IsInUse.Count ||
            checked((long)fx.CellCount * fx.PieceWordCount) != fx.CellBits.Count ||
            Align(CheckedCount(fx.PieceLimit), 16) != fx.VisData.Count ||
            fx.PieceLimit != fx.LinkOrg.Count ||
            Align(CheckedCount(fx.PieceLimit), 4) != fx.HalfThickness.Count)
            throw new InvalidDataException("FxWorld glass counts or capacities are inconsistent.");
        if (fx.Time != 0 || fx.PrevTime != 0 || fx.ActivePieceCount != 0 ||
            fx.GeoDataCount != 0 || fx.NeedToCompactData != 0 ||
            BitConverter.SingleToInt32Bits(fx.EffectChanceAccum) != 0 ||
            fx.LastPieceDeletionTime != 0 ||
            fx.PiecePlaces.Any(piece => piece is null || !IsZero(piece.Frame) ||
                BitConverter.SingleToInt32Bits(piece.Radius) != 0 || piece.NextFree != 0) ||
            fx.PieceStates.Any(piece => piece is null || !IsZero(piece.TexCoordOrigin) ||
                piece.SupportMask != 0 || piece.InitIndex != 0 || piece.GeoDataStart != 0 ||
                piece.DefIndex != 0 || piece.Pad11 is null || piece.Pad11.Count != 5 ||
                piece.Pad11.Any(value => value != 0) ||
                piece.VertCount != 0 || piece.HoleDataCount != 0 || piece.CrackDataCount != 0 ||
                piece.FanDataCount != 0 || piece.Flags != 0 ||
                BitConverter.SingleToInt32Bits(piece.AreaX2) != 0) ||
            fx.PieceDynamics.Any(piece => piece is null || piece.FallTime != 0 ||
                piece.PhysObjId != 0 || piece.PhysJointId != 0 || !IsZero(piece.Vel) || !IsZero(piece.AVel)) ||
            fx.GeoData.Any(value => value.PackedValue != 0) || fx.IsInUse.Any(value => value != 0) ||
            fx.CellBits.Any(value => value != 0) || fx.VisData.Any(value => value != 0) ||
            fx.LinkOrg.Any(value => !IsZero(value)) ||
            fx.HalfThickness.Any(value => BitConverter.SingleToInt32Bits(value) != 0))
            throw new NotSupportedException("Live FxWorld glass runtime state cannot be exported to a d3dbsp.");
    }

    private static void ValidateProvider(int pointer, string? name, string description)
    {
        if ((pointer != 0 && name is null) || (name is not null &&
            (name.Length == 0 || name == "," || name.Contains('\0'))))
            throw new InvalidDataException($"A glass definition has an unresolved or invalid {description} reference.");
    }

    private static bool IsZero(FxVec2 value) =>
        BitConverter.SingleToInt32Bits(value.X) == 0 && BitConverter.SingleToInt32Bits(value.Y) == 0;
    private static bool IsZero(FxVec3 value) =>
        BitConverter.SingleToInt32Bits(value.X) == 0 && BitConverter.SingleToInt32Bits(value.Y) == 0 &&
        BitConverter.SingleToInt32Bits(value.Z) == 0;
    private static bool IsZero(FxSpatialFrame value) =>
        BitConverter.SingleToInt32Bits(value.Quat.X) == 0 &&
        BitConverter.SingleToInt32Bits(value.Quat.Y) == 0 &&
        BitConverter.SingleToInt32Bits(value.Quat.Z) == 0 &&
        BitConverter.SingleToInt32Bits(value.Quat.W) == 0 && IsZero(value.Origin);

    private static string ReferenceName(string name)
    {
        ValidateProvider(0, name, "asset");
        return name.StartsWith(',') ? name : "," + name;
    }

    private static void WriteName(BinaryWriter writer, string? name)
    {
        if (name is null) { writer.Write(-1); return; }
        if (name.Contains('\0')) throw new InvalidDataException("Glass asset names cannot contain NUL.");
        byte[] bytes = Encoding.UTF8.GetBytes(name);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static string? ReadName(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length == -1) return null;
        if (length < 0 || length > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Invalid glass string length.");
        byte[] bytes = reader.ReadBytes(length);
        string name;
        try
        {
            name = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("A glass string is not valid UTF-8.", exception);
        }
        if (name.Contains('\0')) throw new InvalidDataException("Glass strings cannot contain NUL.");
        return name;
    }

    private static int CheckedCount(uint count) => checked((int)count);
    private static int Align(int count, int alignment) => checked((count + alignment - 1) / alignment * alignment);
    private static int Count(uint count, int minimumBytes, Stream stream, string description) =>
        BoundedCount(CheckedCount(count), minimumBytes, stream, description);
    private static int BoundedCount(int count, int minimumBytes, Stream stream, string description)
    {
        if (count < 0 || count > (stream.Length - stream.Position) / minimumBytes)
            throw new InvalidDataException($"The {description} count exceeds the glass lump.");
        return count;
    }

    private static void WriteVec2(BinaryWriter writer, FxVec2 value) { writer.Write(value.X); writer.Write(value.Y); }
    private static FxVec2 ReadVec2(BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle());
    private static void WriteFrame(BinaryWriter writer, FxSpatialFrame frame)
    {
        writer.Write(frame.Quat.X); writer.Write(frame.Quat.Y); writer.Write(frame.Quat.Z); writer.Write(frame.Quat.W);
        writer.Write(frame.Origin.X); writer.Write(frame.Origin.Y); writer.Write(frame.Origin.Z);
    }
    private static FxSpatialFrame ReadFrame(BinaryReader reader) => new(
        new FxQuat(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
        new FxVec3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));
}
