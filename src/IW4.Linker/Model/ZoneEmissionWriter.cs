using System.Buffers.Binary;
using IW4.FastFiles.Zone;

namespace IW4.Linker.Model;

/// <summary>
/// Sequential source writer paired with the seven independent native
/// destination cursors used while rebuilding a decoded zone.
/// </summary>
internal sealed class ZoneEmissionWriter
{
    private const int MaximumBlockExtent = 0x0fffffff;

    private readonly MemoryStream _source = new();
    private readonly int[] _cursors = new int[XFile.BlockCount];
    private readonly int[] _highWater = new int[XFile.BlockCount];
    private readonly Stack<int> _tempScopeBases = new();

    public int SourceLength => checked((int)_source.Length);

    public int ReserveSource(int byteCount)
    {
        if (byteCount < 0)
            throw new ArgumentOutOfRangeException(nameof(byteCount));

        int offset = SourceLength;
        int end = checked(offset + byteCount);
        _source.SetLength(end);
        _source.Position = end;
        return offset;
    }

    public void WriteInt32(int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        _source.Write(bytes);
    }

    public void WriteUInt32(uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        _source.Write(bytes);
    }

    public void WriteUInt16(ushort value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        _source.Write(bytes);
    }

    public void WriteBytes(ReadOnlySpan<byte> bytes) => _source.Write(bytes);

    public void PatchInt32(int sourceOffset, int value) =>
        PatchUInt32(sourceOffset, unchecked((uint)value));

    public void PatchUInt16(int sourceOffset, ushort value)
    {
        ValidatePatchRange(sourceOffset, sizeof(ushort));
        BinaryPrimitives.WriteUInt16BigEndian(
            _source.GetBuffer().AsSpan(sourceOffset, sizeof(ushort)),
            value);
    }

    public void PatchUInt32(int sourceOffset, uint value)
    {
        ValidatePatchRange(sourceOffset, sizeof(uint));
        BinaryPrimitives.WriteUInt32BigEndian(
            _source.GetBuffer().AsSpan(sourceOffset, sizeof(uint)),
            value);
    }

    public XBlockAddress Allocate(
        XFileBlockType block,
        int byteCount,
        int alignment)
    {
        int blockIndex = (int)block;
        if ((uint)blockIndex >= XFile.BlockCount)
            throw new ArgumentOutOfRangeException(nameof(block));
        if (byteCount < 0)
            throw new ArgumentOutOfRangeException(nameof(byteCount));
        if (alignment <= 0 || (alignment & (alignment - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(alignment));
        if (block == XFileBlockType.TEMP && _tempScopeBases.Count == 0)
        {
            throw new InvalidOperationException(
                "TEMP allocations require an active lifetime scope.");
        }

        int alignedOffset = AlignUp(_cursors[blockIndex], alignment);
        int end = checked(alignedOffset + byteCount);
        if (alignedOffset >= MaximumBlockExtent || end > MaximumBlockExtent)
        {
            throw new OverflowException(
                $"{block} allocation exceeds the packed-pointer block range.");
        }

        _cursors[blockIndex] = end;
        _highWater[blockIndex] = Math.Max(_highWater[blockIndex], end);
        return new XBlockAddress(block, alignedOffset);
    }

    public void PushTempScope()
    {
        int tempIndex = (int)XFileBlockType.TEMP;
        _tempScopeBases.Push(_cursors[tempIndex]);
    }

    public void PopTempScope()
    {
        if (_tempScopeBases.Count == 0)
            throw new InvalidOperationException("TEMP scope stack is empty.");

        _cursors[(int)XFileBlockType.TEMP] = _tempScopeBases.Pop();
    }

    public uint[] GetBlockSizes()
    {
        EnsureBalanced();
        return _highWater.Select(value => checked((uint)value)).ToArray();
    }

    public byte[] CompletePadded(int alignment)
    {
        EnsureBalanced();
        if (alignment <= 0 || (alignment & (alignment - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(alignment));

        byte[] meaningful = _source.ToArray();
        int paddedLength = AlignUp(meaningful.Length, alignment);
        if (paddedLength == meaningful.Length)
            return meaningful;

        Array.Resize(ref meaningful, paddedLength);
        return meaningful;
    }

    private void EnsureBalanced()
    {
        if (_tempScopeBases.Count != 0)
            throw new InvalidOperationException("TEMP scope stack is not balanced.");
    }

    private void ValidatePatchRange(int offset, int byteCount)
    {
        if (offset < 0 || byteCount < 0 || offset > SourceLength - byteCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset),
                "Source patch lies outside the emitted tape.");
        }
    }

    private static int AlignUp(int value, int alignment)
    {
        long aligned = ((long)value + alignment - 1) & ~(long)(alignment - 1);
        return checked((int)aligned);
    }
}
