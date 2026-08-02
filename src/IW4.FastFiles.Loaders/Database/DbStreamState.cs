using IW4.Runtime.Database;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Runtime.IO;
using System.Buffers.Binary;
using System.Text;

namespace IW4.FastFiles.Loaders.Database;

public sealed class DbStreamState : IXZoneRuntimeMemory
{
    private readonly Stack<StreamBlockFrame> _stack = new();
    private int[] _blockSizes = [];
    private int[] _positions = [];
    private int[] _materializedLengths = [];
    private MemoryStream[] _streams = [];

    public XZoneMemory? ZoneMemory { get; private set; }
    public bool IsReleased => ZoneMemory?.IsReleased == true;
    public XFileBlockType CurrentBlock { get; private set; } = XFileBlockType.TEMP;
    public IReadOnlyList<int> BlockSizes => _blockSizes;
    public XBlockAddress CurrentAddress => GetAddress(CurrentBlock);

    /// <summary>
    /// Binds the DB stream cursors to one zone's already allocated memory.
    /// XZoneMemory supplies one allocation for each canonical XFile block.
    /// </summary>
    public void DB_InitStreams(XZoneMemory zoneMemory)
    {
        ArgumentNullException.ThrowIfNull(zoneMemory);
        if (zoneMemory.Blocks.Count != XZoneMemory.BlockCount)
        {
            throw new InvalidDataException(
                $"XZoneMemory has {zoneMemory.Blocks.Count} block(s); PS3 DB streams require {XZoneMemory.BlockCount}.");
        }

        ZoneMemory = zoneMemory;
        _blockSizes = new int[zoneMemory.Blocks.Count];
        _positions = new int[_blockSizes.Length];
        _materializedLengths = new int[_blockSizes.Length];
        _streams = new MemoryStream[_blockSizes.Length];
        for (int i = 0; i < _streams.Length; i++)
        {
            XZoneMemoryBlock block = zoneMemory.Blocks[i];
            if ((int)block.Type != i)
            {
                throw new InvalidDataException(
                    $"XZoneMemory block {i} is {block.Type}; DB streams require canonical XFile block order.");
            }

            _blockSizes[i] = checked((int)block.Size);
            _streams[i] = new MemoryStream(
                block.Data,
                index: 0,
                count: block.Data.Length,
                writable: true,
                publiclyVisible: true);
        }

        CurrentBlock = XFileBlockType.TEMP;
        _stack.Clear();
    }

    /// <summary>
    /// Releases stream views before their owning XZoneMemory allocations.
    /// Callers must first retire every provider and runtime side record that
    /// can still address this zone.
    /// </summary>
    public void ReleaseZoneMemory(XZoneMemory zoneMemory)
    {
        ArgumentNullException.ThrowIfNull(zoneMemory);
        if (!ReferenceEquals(ZoneMemory, zoneMemory))
            throw new InvalidOperationException("DB stream state is not bound to the supplied XZoneMemory.");
        if (zoneMemory.IsReleased)
            return;

        foreach (MemoryStream stream in _streams)
            stream?.Dispose();

        _streams = [];
        _blockSizes = [];
        _positions = [];
        _materializedLengths = [];
        _stack.Clear();
        zoneMemory.Release();
    }

    public void Push(XFileBlockType block)
    {
        _stack.Push(new StreamBlockFrame(CurrentBlock, GetPosition(block)));
        CurrentBlock = block;
    }

    public void Pop()
    {
        if (_stack.Count == 0)
            throw new InvalidOperationException("DB stream block stack underflow.");

        StreamBlockFrame frame = _stack.Pop();

        if (CurrentBlock == XFileBlockType.TEMP)
            _positions[(int)XFileBlockType.TEMP] = frame.PushedBlockPosition;

        CurrentBlock = frame.PreviousBlock;
    }

    public XBlockAddress GetAddress(XFileBlockType block)
    {
        int index = (int)block;
        if (index < 0 || index >= _positions.Length)
            throw new ArgumentOutOfRangeException(nameof(block), block, "Invalid XFile block.");

        return new XBlockAddress(block, _positions[index]);
    }

    /// <summary>
    /// Returns the current cursor for a block. TEMP rewind semantics mean this
    /// can be lower than the block's materialized high-water mark.
    /// </summary>
    public int GetPosition(XFileBlockType block) =>
        _positions[GetBlockIndex(block)];

    /// <summary>
    /// Returns the highest byte-exclusive offset materialized in a block.
    /// </summary>
    public int GetMaterializedLength(XFileBlockType block) =>
        _materializedLengths[GetBlockIndex(block)];

    public void Advance(int byteCount)
    {
        if (byteCount < 0)
            throw new ArgumentOutOfRangeException(nameof(byteCount));

        int index = (int)CurrentBlock;
        _positions[index] = checked(_positions[index] + byteCount);
        EnsureLength(index, _positions[index]);
    }

    public XBlockAddress AllocateCurrent(int alignment)
    {
        AlignCurrent(alignment);
        return CurrentAddress;
    }

    public XBlockAddress AllocateInsertPointerCell()
    {
        int index = GetBlockIndex(XFileBlockType.LARGE);
        int position = _positions[index];
        int alignedPosition = checked((position + sizeof(int) - 1) / sizeof(int) * sizeof(int));
        _positions[index] = alignedPosition;
        EnsureLength(index, alignedPosition);

        var address = new XBlockAddress(XFileBlockType.LARGE, alignedPosition);
        WriteInt32(address, 0);
        _positions[index] = checked(alignedPosition + sizeof(int));
        EnsureLength(index, _positions[index]);
        return address;
    }

    public void AlignCurrent(int alignment)
    {
        if (alignment <= 0)
            throw new ArgumentOutOfRangeException(nameof(alignment));

        int index = (int)CurrentBlock;
        int position = _positions[index];
        _positions[index] = checked((position + alignment - 1) / alignment * alignment);
        EnsureLength(index, _positions[index]);
    }

    public byte[] Load(
        FastFileCursor cursor,
        int byteCount)
    {
        if (byteCount < 0)
            throw new ArgumentOutOfRangeException(nameof(byteCount));

        XBlockAddress destinationAddress = CurrentAddress;
        if (CurrentBlock is XFileBlockType.RUNTIME or XFileBlockType.VIRTUAL)
        {
            // PS3 Load_Stream skips source copying for RUNTIME/VIRTUAL blocks.
            // RUNTIME calls the zero-fill helper before advancing; VIRTUAL only advances.
            byte[] zeros = new byte[byteCount];
            Write(zeros);
            return zeros;
        }

        byte[] bytes = cursor.ReadBytes(byteCount);
        Write(bytes);
        return bytes;
    }

    public byte[] Load(
        FastFileCursor cursor,
        int byteCount,
        out XBlockAddress address)
    {
        address = CurrentAddress;
        return Load(cursor, byteCount);
    }

    public ReadOnlyMemory<byte> LoadMemory(
        FastFileCursor cursor,
        int byteCount,
        out XBlockAddress address)
    {
        if (byteCount < 0)
            throw new ArgumentOutOfRangeException(nameof(byteCount));

        address = CurrentAddress;
        if (CurrentBlock is XFileBlockType.RUNTIME or XFileBlockType.VIRTUAL)
        {
            byte[] zeros = new byte[byteCount];
            Write(zeros);
            return zeros;
        }

        ReadOnlyMemory<byte> bytes = cursor.ReadMemory(byteCount);
        Write(bytes.Span);
        return bytes;
    }

    public string LoadCString(FastFileCursor cursor)
    {
        string value = cursor.ReadCString(out ReadOnlyMemory<byte> serializedBytes);
        Write(serializedBytes.Span);
        return value;
    }

    public void Write(ReadOnlySpan<byte> bytes)
    {
        int index = (int)CurrentBlock;
        int end = checked(_positions[index] + bytes.Length);
        ValidateDeclaredLength(index, end);

        MemoryStream stream = _streams[index];
        stream.Position = _positions[index];
        // MemoryStream.Write grows the materialized range itself. Calling
        // EnsureLength first used to allocate and write an equally sized zero
        // buffer immediately before overwriting it with the source bytes.
        stream.Write(bytes);
        _positions[index] = end;
        _materializedLengths[index] = Math.Max(_materializedLengths[index], end);
    }

    public int ReadInt32(XBlockAddress address)
    {
        int index = GetBlockIndex(address.BlockType);
        int offset = address.Offset;
        int writtenLength = _materializedLengths[index];
        if (offset < 0 || offset > writtenLength - sizeof(int))
            throw new InvalidDataException($"Cannot read int32 at {address}; block {address.BlockType} has 0x{writtenLength:X} materialized byte(s).");

        MemoryStream stream = _streams[index];
        if (!stream.TryGetBuffer(out ArraySegment<byte> segment))
            throw new InvalidOperationException($"Unable to inspect block {address.BlockType} bytes.");

        return BinaryPrimitives.ReadInt32BigEndian(segment.AsSpan(offset, sizeof(int)));
    }

    public ushort ReadUInt16(XBlockAddress address)
    {
        int index = GetBlockIndex(address.BlockType);
        int offset = address.Offset;
        int writtenLength = _materializedLengths[index];
        if (offset < 0 || offset > writtenLength - sizeof(ushort))
        {
            throw new InvalidDataException(
                $"Cannot read UInt16 at {address}; block {address.BlockType} has 0x{writtenLength:X} materialized byte(s).");
        }

        MemoryStream stream = _streams[index];
        if (!stream.TryGetBuffer(out ArraySegment<byte> segment))
            throw new InvalidOperationException($"Unable to inspect block {address.BlockType} bytes.");

        return BinaryPrimitives.ReadUInt16BigEndian(segment.AsSpan(offset, sizeof(ushort)));
    }

    public byte ReadByte(XBlockAddress address)
    {
        int index = GetBlockIndex(address.BlockType);
        int offset = address.Offset;
        int writtenLength = _materializedLengths[index];
        if (offset < 0 || offset >= writtenLength)
            throw new InvalidDataException($"Cannot read byte at {address}; block {address.BlockType} has 0x{writtenLength:X} materialized byte(s).");

        MemoryStream stream = _streams[index];
        if (!stream.TryGetBuffer(out ArraySegment<byte> segment))
            throw new InvalidOperationException($"Unable to inspect block {address.BlockType} bytes.");

        return segment[offset];
    }

    public byte[] ReadBytes(XBlockAddress address, int byteCount)
    {
        if (byteCount < 0)
            throw new ArgumentOutOfRangeException(nameof(byteCount));

        int index = GetBlockIndex(address.BlockType);
        int offset = address.Offset;
        int writtenLength = _materializedLengths[index];
        if (offset < 0 || offset > writtenLength - byteCount)
            throw new InvalidDataException($"Cannot read 0x{byteCount:X} byte(s) at {address}; block {address.BlockType} has 0x{writtenLength:X} materialized byte(s).");

        MemoryStream stream = _streams[index];
        if (!stream.TryGetBuffer(out ArraySegment<byte> segment))
            throw new InvalidOperationException($"Unable to inspect block {address.BlockType} bytes.");

        return segment.AsSpan(offset, byteCount).ToArray();
    }

    public string ReadCString(XBlockAddress address)
    {
        int index = GetBlockIndex(address.BlockType);
        int offset = address.Offset;
        int writtenLength = _materializedLengths[index];
        if (offset < 0 || offset >= writtenLength)
            throw new InvalidDataException($"Cannot read CString at {address}; block {address.BlockType} has 0x{writtenLength:X} materialized byte(s).");

        MemoryStream stream = _streams[index];
        if (!stream.TryGetBuffer(out ArraySegment<byte> segment))
            throw new InvalidOperationException($"Unable to inspect block {address.BlockType} bytes.");

        ReadOnlySpan<byte> remaining = segment.AsSpan(offset, writtenLength - offset);
        int terminator = remaining.IndexOf((byte)0);
        if (terminator < 0)
            throw new InvalidDataException($"Cannot read CString at {address}; no null terminator exists before materialized block data ends at 0x{writtenLength:X}.");

        return Encoding.Latin1.GetString(remaining[..terminator]);
    }

    public void WriteInt32(XBlockAddress address, int value)
    {
        int index = GetBlockIndex(address.BlockType);
        int offset = address.Offset;
        int end = checked(offset + sizeof(int));
        ValidateDeclaredLength(index, end);

        MemoryStream stream = _streams[index];
        stream.Position = offset;
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        stream.Write(bytes);
        _materializedLengths[index] = Math.Max(_materializedLengths[index], end);
    }

    public void WriteUInt16(XBlockAddress address, ushort value)
    {
        int index = GetBlockIndex(address.BlockType);
        int offset = address.Offset;
        int end = checked(offset + sizeof(ushort));
        ValidateDeclaredLength(index, end);

        MemoryStream stream = _streams[index];
        stream.Position = offset;
        Span<byte> bytes = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        stream.Write(bytes);
        _materializedLengths[index] = Math.Max(_materializedLengths[index], end);
    }

    public void WriteUInt64(XBlockAddress address, ulong value)
    {
        int index = GetBlockIndex(address.BlockType);
        int offset = address.Offset;
        int end = checked(offset + sizeof(ulong));
        ValidateDeclaredLength(index, end);

        MemoryStream stream = _streams[index];
        stream.Position = offset;
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        stream.Write(bytes);
        _materializedLengths[index] = Math.Max(_materializedLengths[index], end);
    }

    public void WriteBytes(XBlockAddress address, ReadOnlySpan<byte> bytes)
    {
        int index = GetBlockIndex(address.BlockType);
        int offset = address.Offset;
        int end = checked(offset + bytes.Length);
        ValidateDeclaredLength(index, end);

        MemoryStream stream = _streams[index];
        stream.Position = offset;
        stream.Write(bytes);
        _materializedLengths[index] = Math.Max(_materializedLengths[index], end);
    }

    public byte[] GetBytes(XFileBlockType block)
    {
        int index = (int)block;
        if (index < 0 || index >= _streams.Length)
            throw new ArgumentOutOfRangeException(nameof(block), block, "Invalid XFile block.");

        return GetWrittenBytes(index);
    }

    public string DescribePositions()
    {
        if (_positions.Length == 0)
            return "<uninitialized>";

        var parts = new List<string>();
        int count = Math.Min(_positions.Length, (int)XFileBlockType.COUNT);
        for (int i = 0; i < count; i++)
        {
            int declared = i < _blockSizes.Length ? _blockSizes[i] : 0;
            parts.Add($"{(XFileBlockType)i}=0x{_positions[i]:X}/0x{declared:X}");
        }

        return string.Join(", ", parts);
    }

    public void ValidateMaterializedRange(
        XBlockAddress address,
        int byteCount,
        string targetName,
        int rawPointer)
    {
        if (byteCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(byteCount));

        int index = GetBlockIndex(address.BlockType);
        int offset = address.Offset;
        int writtenLength = _materializedLengths[index];
        long requiredEnd = (long)offset + byteCount;

        if (offset < 0 || offset > writtenLength - byteCount)
        {
            throw new InvalidDataException(
                $"Offset pointer 0x{rawPointer:X8} to {targetName} targets {address}, " +
                $"but block {address.BlockType} only has 0x{writtenLength:X} materialized byte(s); " +
                $"required range is 0x{offset:X}..0x{requiredEnd:X}.");
        }
    }

    public void ValidateMaterializedCString(
        XBlockAddress address,
        string targetName,
        int rawPointer)
    {
        int index = GetBlockIndex(address.BlockType);
        int offset = address.Offset;
        int writtenLength = _materializedLengths[index];

        if (offset < 0 || offset >= writtenLength)
        {
            throw new InvalidDataException(
                $"Offset pointer 0x{rawPointer:X8} to {targetName} targets {address}, " +
                $"but block {address.BlockType} only has 0x{writtenLength:X} materialized byte(s).");
        }

        MemoryStream stream = _streams[index];
        if (!stream.TryGetBuffer(out ArraySegment<byte> segment))
            throw new InvalidOperationException($"Unable to inspect block {address.BlockType} bytes for pointer validation.");

        ReadOnlySpan<byte> bytes = segment.AsSpan(0, writtenLength);
        if (bytes[offset..].IndexOf((byte)0) < 0)
        {
            throw new InvalidDataException(
                $"Offset pointer 0x{rawPointer:X8} to {targetName} targets {address}, " +
                $"but no null terminator exists before the end of materialized block {address.BlockType} data at 0x{writtenLength:X}.");
        }
    }

    private void EnsureLength(int index, int length)
    {
        ValidateDeclaredLength(index, length);

        MemoryStream stream = _streams[index];
        if (stream.Length >= length)
        {
            _materializedLengths[index] = Math.Max(_materializedLengths[index], length);
            return;
        }

        // SetLength preserves the runtime-memory zero-fill semantics for
        // alignment/Advance gaps without allocating a temporary byte array.
        stream.SetLength(length);
        _materializedLengths[index] = Math.Max(_materializedLengths[index], length);
    }

    private void ValidateDeclaredLength(int index, int length)
    {
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));

        int declaredLength = _blockSizes[index];
        if (length > declaredLength)
        {
            throw new InvalidOperationException(
                $"Block stream {(XFileBlockType)index} exceeded declared XFile block size: " +
                $"requested length 0x{length:X}, declared length 0x{declaredLength:X}.");
        }
    }

    private int GetBlockIndex(XFileBlockType block)
    {
        int index = (int)block;
        if (index < 0 || index >= _positions.Length)
            throw new ArgumentOutOfRangeException(nameof(block), block, "Invalid XFile block.");

        return index;
    }

    private byte[] GetWrittenBytes(int index)
    {
        int length = _materializedLengths[index];
        MemoryStream stream = _streams[index];
        if (!stream.TryGetBuffer(out ArraySegment<byte> segment))
            return stream.ToArray().AsSpan(0, length).ToArray();

        return segment.AsSpan(0, length).ToArray();
    }
}
