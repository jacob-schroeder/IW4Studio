using System.Buffers.Binary;
using System.Text;

namespace MapConverter.Game.IW3.PC.DynamicEntities;

internal sealed record Iw3DynamicEntitySource(
    int Type,
    IReadOnlyList<float> Quat,
    IReadOnlyList<float> Origin,
    string? XModelName,
    ushort BrushModel,
    ushort PhysicsBrushModel,
    string? DestroyFxName,
    string? DestroyPiecesName,
    string? PhysPresetName,
    int Health,
    IReadOnlyList<float> CenterOfMass,
    IReadOnlyList<float> MomentsOfInertia,
    IReadOnlyList<float> ProductsOfInertia,
    int Contents);

internal sealed record Iw3DynamicEntitySourceData(
    string MapName,
    IReadOnlyList<IReadOnlyList<Iw3DynamicEntitySource>> Definitions)
{
    internal int Count => Definitions.Sum(list => list.Count);
}

internal static class Iw3DynamicEntityReader
{
    private static readonly byte[] ExpectedMagic =
        [(byte)'I', (byte)'W', (byte)'3', (byte)'D', (byte)'Y', (byte)'N', 0, 0];

    private const uint ExpectedVersion = 1;
    private const uint ExpectedHeaderSize = 56;
    private const uint ExpectedRecordSize = 96;
    private const int MaximumFileSize = 64 * 1024 * 1024;
    private const int MaximumStringByteCount = 1024;
    private const uint NullStringId = uint.MaxValue;

    internal static Iw3DynamicEntitySourceData Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
            throw new FileNotFoundException("The IW3 dynamic-entity sidecar does not exist.", fullPath);
        if (file.Length < ExpectedHeaderSize || file.Length > MaximumFileSize)
        {
            throw new InvalidDataException(
                $"IW3 dynamic-entity sidecar '{fullPath}' has invalid length {file.Length}.");
        }

        byte[] bytes = File.ReadAllBytes(fullPath);
        var cursor = new BinaryCursor(bytes);
        if (!cursor.ReadBytes(ExpectedMagic.Length).SequenceEqual(ExpectedMagic))
            throw Invalid(fullPath, "has an invalid magic value");

        uint version = cursor.ReadUInt32();
        uint headerSize = cursor.ReadUInt32();
        uint recordSize = cursor.ReadUInt32();
        uint flags = cursor.ReadUInt32();
        uint modelCount = cursor.ReadUInt32();
        uint brushCount = cursor.ReadUInt32();
        _ = cursor.ReadUInt32();
        uint mapNameStringId = cursor.ReadUInt32();
        uint stringCount = cursor.ReadUInt32();
        uint stringTableByteCount = cursor.ReadUInt32();
        ulong payloadByteCount = cursor.ReadUInt64();

        if (version != ExpectedVersion ||
            headerSize != ExpectedHeaderSize ||
            recordSize != ExpectedRecordSize || flags != 0)
        {
            throw Invalid(
                fullPath,
                $"uses unsupported header values version={version}, " +
                $"headerSize={headerSize}, recordSize={recordSize}, flags={flags}");
        }
        if (modelCount > ushort.MaxValue || brushCount > ushort.MaxValue)
            throw Invalid(fullPath, "exceeds the IW4 dynamic-entity count range");
        if (mapNameStringId != 0 || stringCount == 0)
            throw Invalid(fullPath, "does not identify its map name as string zero");

        ulong recordCount = checked((ulong)modelCount + brushCount);
        ulong recordBytes = checked(recordCount * ExpectedRecordSize);
        ulong expectedPayloadBytes = checked(recordBytes + stringTableByteCount);
        if (payloadByteCount != expectedPayloadBytes ||
            checked((ulong)ExpectedHeaderSize + payloadByteCount) != (ulong)bytes.Length)
        {
            throw Invalid(fullPath, "has inconsistent payload lengths");
        }
        if (stringCount > checked(1 + recordCount * 4))
            throw Invalid(fullPath, "declares more strings than its records can reference");

        var wireRows = new WireRow[checked((int)recordCount)];
        for (int index = 0; index < wireRows.Length; index++)
            wireRows[index] = ReadWireRow(ref cursor, fullPath, index);

        int stringTableStart = cursor.Position;
        var strings = new string[checked((int)stringCount)];
        var uniqueStrings = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < strings.Length; index++)
        {
            uint byteCount = cursor.ReadUInt32();
            if (byteCount is 0 or > MaximumStringByteCount)
            {
                throw Invalid(
                    fullPath,
                    $"string {index} has invalid byte length {byteCount}");
            }

            ReadOnlySpan<byte> encoded = cursor.ReadBytes(checked((int)byteCount));
            if (encoded.Contains((byte)0))
                throw Invalid(fullPath, $"string {index} contains an embedded NUL");
            string value = Encoding.Latin1.GetString(encoded);
            if (!uniqueStrings.Add(value))
                throw Invalid(fullPath, $"string {index} duplicates an earlier value");
            strings[index] = value;

            int padding = checked((4 - ((int)byteCount & 3)) & 3);
            if (cursor.ReadBytes(padding).IndexOfAnyExcept((byte)0) >= 0)
                throw Invalid(fullPath, $"string {index} has nonzero alignment padding");
        }
        if (cursor.Position - stringTableStart != stringTableByteCount ||
            cursor.Position != bytes.Length)
        {
            throw Invalid(fullPath, "does not end exactly after its string table");
        }

        var modelDefinitions = new Iw3DynamicEntitySource[checked((int)modelCount)];
        var brushDefinitions = new Iw3DynamicEntitySource[checked((int)brushCount)];
        for (int index = 0; index < wireRows.Length; index++)
        {
            Iw3DynamicEntitySource definition = Materialize(
                wireRows[index],
                strings,
                fullPath,
                index);
            if (index < modelDefinitions.Length)
            {
                if (definition.XModelName is null)
                    throw Invalid(fullPath, $"model definition {index} has no XModel");
                modelDefinitions[index] = definition;
            }
            else
            {
                if (definition.XModelName is not null)
                    throw Invalid(fullPath, $"brush definition {index - modelDefinitions.Length} has an XModel");
                brushDefinitions[index - modelDefinitions.Length] = definition;
            }
        }

        return new Iw3DynamicEntitySourceData(
            strings[0],
            Array.AsReadOnly<IReadOnlyList<Iw3DynamicEntitySource>>(
            [
                Array.AsReadOnly(modelDefinitions),
                Array.AsReadOnly(brushDefinitions)
            ]));
    }

    private static WireRow ReadWireRow(
        ref BinaryCursor cursor,
        string path,
        int index)
    {
        int start = cursor.Position;
        int type = cursor.ReadInt32();
        float[] quat = ReadFiniteFloats(ref cursor, 4, path, index, "quaternion");
        float[] origin = ReadFiniteFloats(ref cursor, 3, path, index, "origin");
        uint xmodel = cursor.ReadUInt32();
        ushort brushModel = cursor.ReadUInt16();
        ushort physicsBrushModel = cursor.ReadUInt16();
        uint destroyFx = cursor.ReadUInt32();
        uint destroyPieces = cursor.ReadUInt32();
        uint physPreset = cursor.ReadUInt32();
        int health = cursor.ReadInt32();
        float[] centerOfMass = ReadFiniteFloats(
            ref cursor, 3, path, index, "center of mass");
        float[] momentsOfInertia = ReadFiniteFloats(
            ref cursor, 3, path, index, "moments of inertia");
        float[] productsOfInertia = ReadFiniteFloats(
            ref cursor, 3, path, index, "products of inertia");
        int contents = cursor.ReadInt32();
        if (cursor.Position - start != ExpectedRecordSize)
            throw Invalid(path, $"record {index} does not match the v1 record size");
        if (type is not 1 and not 2)
            throw Invalid(path, $"record {index} has unsupported type {type}");
        double quatMagnitudeSquared = quat.Sum(value => (double)value * value);
        if (quatMagnitudeSquared <= double.Epsilon)
            throw Invalid(path, $"record {index} has a zero quaternion");

        return new WireRow(
            type,
            quat,
            origin,
            xmodel,
            brushModel,
            physicsBrushModel,
            destroyFx,
            destroyPieces,
            physPreset,
            health,
            centerOfMass,
            momentsOfInertia,
            productsOfInertia,
            contents);
    }

    private static float[] ReadFiniteFloats(
        ref BinaryCursor cursor,
        int count,
        string path,
        int row,
        string field)
    {
        var result = new float[count];
        for (int index = 0; index < result.Length; index++)
        {
            result[index] = cursor.ReadSingle();
            if (!float.IsFinite(result[index]))
                throw Invalid(path, $"record {row} {field}[{index}] is not finite");
        }
        return result;
    }

    private static Iw3DynamicEntitySource Materialize(
        WireRow row,
        IReadOnlyList<string> strings,
        string path,
        int index) => new(
        row.Type,
        Array.AsReadOnly(row.Quat),
        Array.AsReadOnly(row.Origin),
        ResolveName(row.XModelNameId, strings, path, index, "XModel"),
        row.BrushModel,
        row.PhysicsBrushModel,
        ResolveName(row.DestroyFxNameId, strings, path, index, "destroy FX"),
        ResolveName(row.DestroyPiecesNameId, strings, path, index, "destroy pieces"),
        ResolveName(row.PhysPresetNameId, strings, path, index, "physics preset"),
        row.Health,
        Array.AsReadOnly(row.CenterOfMass),
        Array.AsReadOnly(row.MomentsOfInertia),
        Array.AsReadOnly(row.ProductsOfInertia),
        row.Contents);

    private static string? ResolveName(
        uint id,
        IReadOnlyList<string> strings,
        string path,
        int row,
        string field)
    {
        if (id == NullStringId)
            return null;
        if (id >= strings.Count)
            throw Invalid(path, $"record {row} {field} string ID {id} is out of range");
        return strings[checked((int)id)];
    }

    private static InvalidDataException Invalid(string path, string message) =>
        new($"IW3 dynamic-entity sidecar '{path}' {message}.");

    private sealed record WireRow(
        int Type,
        float[] Quat,
        float[] Origin,
        uint XModelNameId,
        ushort BrushModel,
        ushort PhysicsBrushModel,
        uint DestroyFxNameId,
        uint DestroyPiecesNameId,
        uint PhysPresetNameId,
        int Health,
        float[] CenterOfMass,
        float[] MomentsOfInertia,
        float[] ProductsOfInertia,
        int Contents);

    private ref struct BinaryCursor(ReadOnlySpan<byte> bytes)
    {
        private readonly ReadOnlySpan<byte> _bytes = bytes;

        internal int Position { get; private set; }

        internal ReadOnlySpan<byte> ReadBytes(int count)
        {
            if (count < 0 || count > _bytes.Length - Position)
                throw new EndOfStreamException("The dynamic-entity sidecar ended unexpectedly.");
            ReadOnlySpan<byte> result = _bytes.Slice(Position, count);
            Position += count;
            return result;
        }

        internal ushort ReadUInt16()
        {
            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(ReadBytes(sizeof(ushort)));
            return value;
        }

        internal uint ReadUInt32()
        {
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(ReadBytes(sizeof(uint)));
            return value;
        }

        internal int ReadInt32() => unchecked((int)ReadUInt32());

        internal ulong ReadUInt64()
        {
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(ReadBytes(sizeof(ulong)));
            return value;
        }

        internal float ReadSingle() => BitConverter.Int32BitsToSingle(ReadInt32());
    }
}
