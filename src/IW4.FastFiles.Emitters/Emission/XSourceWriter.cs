using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace IW4.FastFiles.Emitters.Emission;

/// <summary>Checked big-endian source writer used by the emit pass only.</summary>
public sealed class XSourceWriter
{
    private readonly List<byte> _bytes = [];

    public int Position => _bytes.Count;

    public void WriteByte(byte value) => _bytes.Add(value);

    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        for (int index = 0; index < value.Length; index++)
            _bytes.Add(value[index]);
    }

    public void WriteInt16(short value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(bytes, value);
        WriteBytes(bytes);
    }

    public void WriteUInt16(ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        WriteBytes(bytes);
    }

    public void WriteInt32(int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        WriteBytes(bytes);
    }

    public void WriteUInt32(uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        WriteBytes(bytes);
    }

    /// <summary>Writes the exact IEEE-754 bit pattern in PS3 big-endian
    /// order.  This preserves signed zero and NaN payloads without a numeric
    /// normalization round-trip.</summary>
    public void WriteSingle(float value) => WriteInt32(BitConverter.SingleToInt32Bits(value));

    public void WriteInt64(long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        WriteBytes(bytes);
    }

    public void WriteUInt64(ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        WriteBytes(bytes);
    }

    public int Reserve(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        int offset = Position;
        for (int index = 0; index < count; index++)
            _bytes.Add(0);
        return offset;
    }

    public void PatchInt32(int offset, int value)
    {
        if (offset < 0 || offset > Position - sizeof(int))
            throw new ArgumentOutOfRangeException(nameof(offset));

        BinaryPrimitives.WriteInt32BigEndian(CollectionsMarshal.AsSpan(_bytes).Slice(offset, sizeof(int)), value);
    }

    public void PadToAlignment(int alignment)
    {
        if (alignment <= 0 || (alignment & (alignment - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(alignment), "Alignment must be a positive power of two.");

        int padding = (-Position) & (alignment - 1);
        _bytes.AddRange(Enumerable.Repeat((byte)0, padding));
    }

    public void WriteLatin1CString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.IndexOf('\0') >= 0)
            throw new InvalidDataException("A serialized XString cannot contain an embedded null.");
        if (value.Any(character => character > byte.MaxValue))
            throw new InvalidDataException("A serialized XString contains a character outside Latin-1.");

        WriteBytes(Encoding.Latin1.GetBytes(value));
        WriteByte(0);
    }

    public byte[] ToArray() => _bytes.ToArray();
}
