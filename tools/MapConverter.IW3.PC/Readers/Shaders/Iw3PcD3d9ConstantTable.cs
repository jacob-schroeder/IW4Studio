using System.Buffers.Binary;
using System.Text;

namespace MapConverter.IW3.PC.Readers.Shaders;

/// <summary>A reflected Direct3D 9 CTAB parameter in IW3 PC shader bytecode.</summary>
public sealed record Iw3PcD3d9Constant(
    string Name,
    ushort RegisterSet,
    ushort RegisterIndex,
    ushort RegisterCount,
    ushort Type,
    ushort Class,
    ushort Rows,
    ushort Columns,
    ushort Elements);

/// <summary>Reads the optional CTAB comment from an IW3 PC Direct3D 9 shader.</summary>
public static class Iw3PcD3d9ConstantTable
{
    public static IReadOnlyList<Iw3PcD3d9Constant> Read(ReadOnlySpan<byte> data, string path)
    {
        for (int offset = 4; offset + 8 <= data.Length; offset += 4)
        {
            uint token = U32(data, offset);
            if ((token & 0xffff) != 0xfffe)
                continue;
            int byteLength = checked((int)(token >> 16) * 4);
            int payload = offset + 4;
            if (byteLength < 28 || payload + byteLength > data.Length ||
                !data.Slice(payload, 4).SequenceEqual("CTAB"u8))
                continue;
            return Parse(data.Slice(payload, byteLength), path);
        }
        return [];
    }

    private static IReadOnlyList<Iw3PcD3d9Constant> Parse(ReadOnlySpan<byte> table, string path)
    {
        const int signatureSize = 4;
        if (U32(table, 4) != 28)
            throw new InvalidDataException($"Shader '{path}' has an unsupported CTAB header.");
        int count = checked((int)U32(table, 16));
        if (count == 0)
            return [];
        int infoOffset = checked(signatureSize + (int)U32(table, 20));
        if (count > 256 || infoOffset < 28 || infoOffset + checked(count * 20) > table.Length)
            throw new InvalidDataException($"Shader '{path}' has an invalid CTAB constant table.");

        var result = new List<Iw3PcD3d9Constant>(count);
        for (int index = 0; index < count; index++)
        {
            int entry = infoOffset + index * 20;
            string name = ReadCString(table, checked(signatureSize + (int)U32(table, entry)), path);
            ushort registerSet = U16(table, entry + 4);
            ushort registerIndex = U16(table, entry + 6);
            ushort registerCount = U16(table, entry + 8);
            int typeOffset = checked(signatureSize + (int)U32(table, entry + 12));
            if (registerCount == 0 || typeOffset < 0 || typeOffset + 16 > table.Length)
                throw new InvalidDataException($"Shader '{path}' has an invalid CTAB constant '{name}'.");
            result.Add(new Iw3PcD3d9Constant(
                name, registerSet, registerIndex, registerCount,
                U16(table, typeOffset + 2), U16(table, typeOffset), U16(table, typeOffset + 4),
                U16(table, typeOffset + 6), U16(table, typeOffset + 8)));
        }
        return result;
    }

    private static string ReadCString(ReadOnlySpan<byte> source, int offset, string path)
    {
        if (offset < 0 || offset >= source.Length)
            throw new InvalidDataException($"Shader '{path}' contains an out-of-range CTAB string.");
        int length = source[offset..].IndexOf((byte)0);
        if (length <= 0)
            throw new InvalidDataException($"Shader '{path}' contains an invalid CTAB string.");
        return Encoding.ASCII.GetString(source.Slice(offset, length));
    }

    private static uint U32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
    private static ushort U16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
}
