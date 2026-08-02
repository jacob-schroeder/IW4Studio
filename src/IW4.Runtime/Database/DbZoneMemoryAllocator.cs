using IW4.FastFiles.Zone;

namespace IW4.Runtime.Database;

public sealed class DbZoneMemoryAllocator
{
    // Host coordinates deliberately reserve the all-zero stream record and
    // advance monotonically. Literal PS3 offsets are allocator-run-specific;
    // Event20 requires the domain and relative arithmetic, not a captured
    // console's numeric base.
    private const uint LogicalAllocationAlignment = 0x10;
    private const uint FirstUsableEffectiveOffset = 0x10;

    private readonly object _allocationLock = new();
    private uint _nextMainEffectiveOffset = FirstUsableEffectiveOffset;
    private uint _nextLocalEffectiveOffset = FirstUsableEffectiveOffset;

    /// <summary>
    /// Allocates the seven independent block buffers described by an XFile.
    /// This is the managed equivalent of DB_AllocXZoneMemory; the zone name is
    /// retained as allocation context rather than embedded in XZoneMemory.
    /// </summary>
    public XZoneMemory DB_AllocXZoneMemory(XFile xfile, string zoneName)
    {
        ArgumentNullException.ThrowIfNull(xfile);
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneName);

        if (xfile.BlockSizes.Count != XZoneMemory.BlockCount)
        {
            throw new InvalidDataException(
                $"XFile '{zoneName}' declares {xfile.BlockSizes.Count} block(s); " +
                $"PS3 XZoneMemory requires {XZoneMemory.BlockCount}.");
        }

        lock (_allocationLock)
        {
            uint nextMain = _nextMainEffectiveOffset;
            uint nextLocal = _nextLocalEffectiveOffset;
            var blocks = new XZoneMemoryBlock[XZoneMemory.BlockCount];
            for (int i = 0; i < blocks.Length; i++)
            {
                uint declaredSize = xfile.BlockSizes[i];
                if (declaredSize > Array.MaxLength)
                {
                    throw new InvalidDataException(
                        $"XFile '{zoneName}' block {(XFileBlockType)i} declares 0x{declaredSize:X} bytes, " +
                        $"which exceeds the managed array limit 0x{Array.MaxLength:X}.");
                }

                XFileBlockType type = (XFileBlockType)i;
                XZoneMemoryBlockRsxPlacement? placement = type switch
                {
                    // Vertex-layer data occupies renderer-local memory and uses
                    // placement token 0.
                    XFileBlockType.PHYSICAL when declaredSize != 0 =>
                        AllocatePlacement(
                            XZoneMemoryBlockRsxLocation.Local,
                            declaredSize,
                            ref nextLocal),

                    // Vertex data occupies renderer-main memory and uses
                    // placement token 1.
                    XFileBlockType.LARGE when declaredSize != 0 =>
                        AllocatePlacement(
                            XZoneMemoryBlockRsxLocation.Main,
                            declaredSize,
                            ref nextMain),
                    _ => null
                };

                blocks[i] = new XZoneMemoryBlock(
                    type,
                    new byte[checked((int)declaredSize)],
                    placement);
            }

            _nextMainEffectiveOffset = nextMain;
            _nextLocalEffectiveOffset = nextLocal;
            return new XZoneMemory(blocks);
        }
    }

    private static XZoneMemoryBlockRsxPlacement AllocatePlacement(
        XZoneMemoryBlockRsxLocation location,
        uint size,
        ref uint nextEffectiveOffset)
    {
        uint allocationBase = AlignUp(
            nextEffectiveOffset,
            LogicalAllocationAlignment);
        ulong end = (ulong)allocationBase + size;
        if (end > uint.MaxValue)
        {
            throw new InvalidOperationException(
                $"The managed {location} RSX logical address space is exhausted.");
        }

        nextEffectiveOffset = AlignUp(
            checked((uint)end),
            LogicalAllocationAlignment);
        return new XZoneMemoryBlockRsxPlacement(location, allocationBase);
    }

    private static uint AlignUp(uint value, uint alignment)
    {
        ulong aligned = ((ulong)value + alignment - 1) & ~(alignment - 1UL);
        if (aligned > uint.MaxValue)
        {
            throw new InvalidOperationException(
                "The managed RSX logical address space cannot satisfy the requested alignment.");
        }

        return (uint)aligned;
    }
}
