using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Immutable;

namespace IW4.Render.Shaders;

/// <summary>
/// Canonical little-endian writer for RSX shader content encodings. Strings
/// retain their exact UTF-16 code-unit representation.
/// </summary>
internal sealed class RsxCanonicalContentWriter
{
    private readonly ArrayBufferWriter<byte> _buffer = new();

    internal void WriteByte(byte value)
    {
        Span<byte> destination = _buffer.GetSpan(1);
        destination[0] = value;
        _buffer.Advance(1);
    }

    internal void WriteBoolean(bool value) =>
        WriteByte(value ? (byte)1 : (byte)0);

    internal void WriteUInt16(ushort value)
    {
        Span<byte> destination = _buffer.GetSpan(sizeof(ushort));
        BinaryPrimitives.WriteUInt16LittleEndian(destination, value);
        _buffer.Advance(sizeof(ushort));
    }

    internal void WriteInt32(int value)
    {
        Span<byte> destination = _buffer.GetSpan(sizeof(int));
        BinaryPrimitives.WriteInt32LittleEndian(destination, value);
        _buffer.Advance(sizeof(int));
    }

    internal void WriteUInt32(uint value)
    {
        Span<byte> destination = _buffer.GetSpan(sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(destination, value);
        _buffer.Advance(sizeof(uint));
    }

    internal void WriteSingle(float value) =>
        WriteUInt32(BitConverter.SingleToUInt32Bits(value));

    internal void WriteNullableInt32(int? value)
    {
        WriteBoolean(value.HasValue);
        if (value.HasValue)
            WriteInt32(value.Value);
    }

    internal void WriteNullableUInt16(ushort? value)
    {
        WriteBoolean(value.HasValue);
        if (value.HasValue)
            WriteUInt16(value.Value);
    }

    internal void WriteBytes(ReadOnlySpan<byte> value)
    {
        WriteInt32(value.Length);
        value.CopyTo(_buffer.GetSpan(value.Length));
        _buffer.Advance(value.Length);
    }

    internal void WriteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        WriteInt32(value.Length);
        int byteCount = checked(value.Length * sizeof(char));
        Span<byte> destination = _buffer.GetSpan(byteCount);
        for (var index = 0; index < value.Length; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                destination[(index * sizeof(char))..],
                value[index]);
        }
        _buffer.Advance(byteCount);
    }

    internal byte[] ToArray() => _buffer.WrittenSpan.ToArray();

    internal ImmutableArray<byte> ToImmutable() =>
        ImmutableArray.CreateRange(_buffer.WrittenSpan.ToArray());
}
