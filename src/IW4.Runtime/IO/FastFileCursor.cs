using System.Buffers.Binary;
using System.Text;
using IW4.FastFiles.Zone;

namespace IW4.Runtime.IO;

public sealed class FastFileCursor
{
    private readonly ReadOnlyMemory<byte> _memory;

    public FastFileCursor(ReadOnlyMemory<byte> memory, XBlockAddress? baseAddress = null, int? decodedTapeBaseOffset = null)
    {
        _memory = memory;
        BaseAddress = baseAddress;
        DecodedTapeBaseOffset = decodedTapeBaseOffset;
    }

    public int Offset { get; private set; }
    public int Length => _memory.Length;
    public int Remaining => Length - Offset;
    public XBlockAddress? BaseAddress { get; }
    /// <summary>Optional coordinate of this cursor's first byte in the decoded zone tape.</summary>
    public int? DecodedTapeBaseOffset { get; }

    private ReadOnlySpan<byte> Span => _memory.Span;

    public byte ReadByte()
    {
        EnsureAvailable(sizeof(byte));
        return Span[Offset++];
    }

    public ushort ReadUInt16()
    {
        EnsureAvailable(sizeof(ushort));
        ushort value = BinaryPrimitives.ReadUInt16BigEndian(Span.Slice(Offset, sizeof(ushort)));
        Offset += sizeof(ushort);
        return value;
    }

    public ushort PeekUInt16()
    {
        EnsureAvailable(sizeof(ushort));
        return BinaryPrimitives.ReadUInt16BigEndian(Span.Slice(Offset, sizeof(ushort)));
    }

    public int ReadInt32()
    {
        EnsureAvailable(sizeof(int));
        int value = BinaryPrimitives.ReadInt32BigEndian(Span.Slice(Offset, sizeof(int)));
        Offset += sizeof(int);
        return value;
    }

    internal float ReadSingle()
    {
        return BitConverter.Int32BitsToSingle(ReadInt32());
    }

    public uint ReadUInt32()
    {
        EnsureAvailable(sizeof(uint));
        uint value = BinaryPrimitives.ReadUInt32BigEndian(Span.Slice(Offset, sizeof(uint)));
        Offset += sizeof(uint);
        return value;
    }

    public ulong ReadUInt64()
    {
        EnsureAvailable(sizeof(ulong));
        ulong value = BinaryPrimitives.ReadUInt64BigEndian(Span.Slice(Offset, sizeof(ulong)));
        Offset += sizeof(ulong);
        return value;
    }

    public string ReadFixedString(int length)
    {
        EnsureAvailable(length);
        string value = Encoding.Latin1.GetString(Span.Slice(Offset, length));
        Offset += length;
        return value;
    }

    public string ReadCString()
    {
        return ReadCString(out _);
    }

    public string ReadCString(out ReadOnlyMemory<byte> serializedBytes)
    {
        int start = Offset;
        int terminator = Span[start..].IndexOf((byte)0);
        if (terminator < 0)
            throw new EndOfStreamException($"CString at 0x{start:X} has no terminator before buffer length 0x{Length:X}.");

        serializedBytes = _memory.Slice(start, terminator + sizeof(byte));
        string value = Encoding.Latin1.GetString(serializedBytes.Span[..terminator]);
        Offset = checked(start + terminator + sizeof(byte));
        return value;
    }

    public byte[] ReadBytes(int length)
    {
        return ReadMemory(length).ToArray();
    }

    public ReadOnlyMemory<byte> ReadMemory(int length)
    {
        EnsureAvailable(length);
        ReadOnlyMemory<byte> value = _memory.Slice(Offset, length);
        Offset += length;
        return value;
    }

    /// <summary>
    /// Returns a bounded, zero-copy view with coordinates relative to this cursor.
    /// The source bytes remain read-only and the returned cursor starts at offset zero.
    /// </summary>
    internal FastFileCursor Slice(int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > Length - length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        return new FastFileCursor(
            _memory.Slice(offset, length),
            BaseAddress?.Add(offset),
            DecodedTapeOffsetAt(offset));
    }

    /// <summary>
    /// Returns an owned copy of a byte range without advancing the cursor.
    /// Parsers use this when a source envelope must outlive the input cursor.
    /// </summary>
    public byte[] CopyBytes(int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > Length - length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        return _memory.Slice(offset, length).ToArray();
    }

    public void Skip(int length)
    {
        EnsureAvailable(length);
        Offset += length;
    }

    public void Align(int alignment)
    {
        if (alignment <= 0)
            throw new ArgumentOutOfRangeException(nameof(alignment));

        int aligned = (Offset + alignment - 1) / alignment * alignment;
        Skip(aligned - Offset);
    }

    public XBlockAddress? AddressAt(int offset)
    {
        if (offset < 0 || offset > Length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        return BaseAddress?.Add(offset);
    }

    public int? DecodedTapeOffsetAt(int offset)
    {
        if (offset < 0 || offset > Length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        return DecodedTapeBaseOffset is { } baseOffset ? checked(baseOffset + offset) : null;
    }

    private void EnsureAvailable(int byteCount)
    {
        if (byteCount < 0)
            throw new ArgumentOutOfRangeException(nameof(byteCount));

        if (Offset + byteCount > Length)
            throw new EndOfStreamException($"Tried to read 0x{byteCount:X} byte(s) at 0x{Offset:X}, beyond buffer length 0x{Length:X}.");
    }
}
