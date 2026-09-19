using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;

namespace MapConverter.Game.IW3.PC.Models;

internal sealed record Iw3XModelCollisionSurfaceSource(
    IReadOnlyList<float> Mins,
    IReadOnlyList<float> Maxs,
    int BoneIndex,
    int Contents,
    int SurfaceFlags);

internal sealed record Iw3XModelCollisionSource(
    string Name,
    int Contents,
    int CollisionLod,
    IReadOnlyList<Iw3XModelCollisionSurfaceSource> Surfaces);

internal sealed record Iw3XModelCollisionSourceData(
    string MapName,
    IReadOnlyDictionary<string, Iw3XModelCollisionSource> ByName);

internal static class Iw3XModelCollisionReader
{
    private static readonly byte[] ExpectedMagic =
        [(byte)'I', (byte)'W', (byte)'3', (byte)'M', (byte)'C', (byte)'O', (byte)'L', 0];

    private const uint ExpectedVersion = 1;
    private const uint ExpectedHeaderSize = 56;
    private const uint ExpectedModelHeaderSize = 16;
    private const uint ExpectedSurfaceSize = 36;
    private const int MaximumFileSize = 64 * 1024 * 1024;
    private const int MaximumStringByteCount = 1024;

    internal static Iw3XModelCollisionSourceData Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
            throw new FileNotFoundException("The IW3 XModel-collision sidecar does not exist.", fullPath);
        if (file.Length < ExpectedHeaderSize || file.Length > MaximumFileSize)
        {
            throw Invalid(
                fullPath,
                $"has invalid length {file.Length}");
        }

        byte[] bytes = File.ReadAllBytes(fullPath);
        var cursor = new BinaryCursor(bytes);
        if (!cursor.ReadBytes(ExpectedMagic.Length).SequenceEqual(ExpectedMagic))
            throw Invalid(fullPath, "has an invalid magic value");

        uint version = cursor.ReadUInt32();
        uint headerSize = cursor.ReadUInt32();
        uint modelHeaderSize = cursor.ReadUInt32();
        uint surfaceSize = cursor.ReadUInt32();
        uint flags = cursor.ReadUInt32();
        uint modelCount = cursor.ReadUInt32();
        uint stringCount = cursor.ReadUInt32();
        uint stringTableByteCount = cursor.ReadUInt32();
        uint entryByteCount = cursor.ReadUInt32();
        uint mapNameStringId = cursor.ReadUInt32();
        ulong payloadByteCount = cursor.ReadUInt64();
        if (version != ExpectedVersion ||
            headerSize != ExpectedHeaderSize ||
            modelHeaderSize != ExpectedModelHeaderSize ||
            surfaceSize != ExpectedSurfaceSize || flags != 0)
        {
            throw Invalid(
                fullPath,
                $"uses unsupported header values version={version}, " +
                $"headerSize={headerSize}, modelHeaderSize={modelHeaderSize}, " +
                $"surfaceSize={surfaceSize}, flags={flags}");
        }
        if (modelCount > ushort.MaxValue)
            throw Invalid(fullPath, "exceeds the supported XModel count range");
        if (mapNameStringId != 0 || stringCount == 0)
            throw Invalid(fullPath, "does not identify its map name as string zero");
        if (stringCount != checked(modelCount + 1))
            throw Invalid(fullPath, "must declare exactly one map string and one name per model");
        if (payloadByteCount != checked((ulong)entryByteCount + stringTableByteCount) ||
            checked((ulong)ExpectedHeaderSize + payloadByteCount) != (ulong)bytes.Length)
        {
            throw Invalid(fullPath, "has inconsistent payload lengths");
        }

        int entryEnd = checked(cursor.Position + (int)entryByteCount);
        var wireModels = new WireModel[checked((int)modelCount)];
        for (int modelIndex = 0; modelIndex < wireModels.Length; modelIndex++)
        {
            uint nameId = cursor.ReadUInt32(entryEnd);
            int contents = cursor.ReadInt32(entryEnd);
            int collisionLod = cursor.ReadInt32(entryEnd);
            uint surfaceCount = cursor.ReadUInt32(entryEnd);
            if (collisionLod is < -1 or > 3)
            {
                throw Invalid(
                    fullPath,
                    $"model {modelIndex} has unsupported collision LOD {collisionLod}");
            }
            ulong requiredSurfaceBytes = checked((ulong)surfaceCount * ExpectedSurfaceSize);
            if (requiredSurfaceBytes > (ulong)(entryEnd - cursor.Position))
                throw Invalid(fullPath, $"model {modelIndex} surface table exceeds the entry payload");

            var surfaces = new Iw3XModelCollisionSurfaceSource[checked((int)surfaceCount)];
            for (int surfaceIndex = 0; surfaceIndex < surfaces.Length; surfaceIndex++)
            {
                float[] mins = ReadFiniteVector(
                    ref cursor, entryEnd, fullPath, modelIndex, surfaceIndex, "mins");
                float[] maxs = ReadFiniteVector(
                    ref cursor, entryEnd, fullPath, modelIndex, surfaceIndex, "maxs");
                if (Enumerable.Range(0, 3).Any(index => mins[index] > maxs[index]))
                {
                    throw Invalid(
                        fullPath,
                        $"model {modelIndex} surface {surfaceIndex} has inverted bounds");
                }
                int boneIndex = cursor.ReadInt32(entryEnd);
                if (boneIndex < 0)
                {
                    throw Invalid(
                        fullPath,
                        $"model {modelIndex} surface {surfaceIndex} has negative bone index");
                }
                surfaces[surfaceIndex] = new Iw3XModelCollisionSurfaceSource(
                    Array.AsReadOnly(mins),
                    Array.AsReadOnly(maxs),
                    boneIndex,
                    cursor.ReadInt32(entryEnd),
                    cursor.ReadInt32(entryEnd));
            }
            wireModels[modelIndex] = new WireModel(
                nameId,
                contents,
                collisionLod,
                Array.AsReadOnly(surfaces));
        }
        if (cursor.Position != entryEnd)
            throw Invalid(fullPath, "model entries do not exactly fill their payload");

        int stringTableStart = cursor.Position;
        var strings = new string[checked((int)stringCount)];
        var uniqueStrings = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < strings.Length; index++)
        {
            uint byteCount = cursor.ReadUInt32();
            if (byteCount is 0 or > MaximumStringByteCount)
                throw Invalid(fullPath, $"string {index} has invalid byte length {byteCount}");
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

        var modelsByName = new Dictionary<string, Iw3XModelCollisionSource>(
            wireModels.Length,
            StringComparer.Ordinal);
        for (int index = 0; index < wireModels.Length; index++)
        {
            WireModel wire = wireModels[index];
            if (wire.NameId == 0 || wire.NameId >= strings.Length)
                throw Invalid(fullPath, $"model {index} name string ID {wire.NameId} is invalid");
            string name = NormalizeModelName(
                strings[wire.NameId],
                fullPath,
                index);
            var model = new Iw3XModelCollisionSource(
                name,
                wire.Contents,
                wire.CollisionLod,
                wire.Surfaces);
            if (!modelsByName.TryAdd(name, model))
                throw Invalid(fullPath, $"model name '{name}' is duplicated");
        }

        return new Iw3XModelCollisionSourceData(
            strings[0],
            new ReadOnlyDictionary<string, Iw3XModelCollisionSource>(modelsByName));
    }

    private static float[] ReadFiniteVector(
        ref BinaryCursor cursor,
        int end,
        string path,
        int modelIndex,
        int surfaceIndex,
        string field)
    {
        var values = new float[3];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = cursor.ReadSingle(end);
            if (!float.IsFinite(values[index]))
            {
                throw Invalid(
                    path,
                    $"model {modelIndex} surface {surfaceIndex} {field}[{index}] is not finite");
            }
        }
        return values;
    }

    private static string NormalizeModelName(
        string name,
        string path,
        int modelIndex)
    {
        string normalized = name.Length > 0 && name[0] == ','
            ? name[1..]
            : name;
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized[0] == '/' ||
            normalized.Contains(',') ||
            normalized.Contains('\\') ||
            normalized.Contains('\0') ||
            !string.Equals(normalized, normalized.Trim(), StringComparison.Ordinal) ||
            normalized.Any(character =>
                char.IsControl(character) || character > byte.MaxValue) ||
            normalized.Split('/').Any(segment =>
                segment.Length == 0 || segment is "." or ".."))
        {
            throw Invalid(
                path,
                $"model {modelIndex} has invalid XModel name '{name}'");
        }
        return normalized;
    }

    private static InvalidDataException Invalid(string path, string message) =>
        new($"IW3 XModel-collision sidecar '{path}' {message}.");

    private sealed record WireModel(
        uint NameId,
        int Contents,
        int CollisionLod,
        IReadOnlyList<Iw3XModelCollisionSurfaceSource> Surfaces);

    private ref struct BinaryCursor(ReadOnlySpan<byte> bytes)
    {
        private readonly ReadOnlySpan<byte> _bytes = bytes;

        internal int Position { get; private set; }

        internal ReadOnlySpan<byte> ReadBytes(int count, int? exclusiveEnd = null)
        {
            int end = exclusiveEnd ?? _bytes.Length;
            if (count < 0 || Position > end || count > end - Position || end > _bytes.Length)
                throw new EndOfStreamException("The XModel-collision sidecar ended unexpectedly.");
            ReadOnlySpan<byte> result = _bytes.Slice(Position, count);
            Position += count;
            return result;
        }

        internal uint ReadUInt32(int? exclusiveEnd = null) =>
            BinaryPrimitives.ReadUInt32LittleEndian(ReadBytes(sizeof(uint), exclusiveEnd));

        internal int ReadInt32(int? exclusiveEnd = null) =>
            unchecked((int)ReadUInt32(exclusiveEnd));

        internal ulong ReadUInt64() =>
            BinaryPrimitives.ReadUInt64LittleEndian(ReadBytes(sizeof(ulong)));

        internal float ReadSingle(int? exclusiveEnd = null) =>
            BitConverter.Int32BitsToSingle(ReadInt32(exclusiveEnd));
    }
}
