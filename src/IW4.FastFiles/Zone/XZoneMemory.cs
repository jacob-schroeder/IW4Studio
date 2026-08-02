using System.Collections.ObjectModel;

namespace IW4.FastFiles.Zone;

// PS3 XZoneMemory contains seven ordered XZoneMemoryBlock entries and occupies
// 0x38 bytes in the native process.
public sealed class XZoneMemory
{
    public const int BlockCount = (int)XFileBlockType.COUNT;
    public const int Ps3NativeSize = BlockCount * XZoneMemoryBlock.Ps3NativeSize;

    private readonly ReadOnlyCollection<XZoneMemoryBlock> _blocks;

    public XZoneMemory(IEnumerable<XZoneMemoryBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        XZoneMemoryBlock[] orderedBlocks = blocks.ToArray();
        if (orderedBlocks.Length != BlockCount)
        {
            throw new ArgumentException(
                $"PS3 XZoneMemory requires exactly {BlockCount} blocks; got {orderedBlocks.Length}.",
                nameof(blocks));
        }

        for (int index = 0; index < orderedBlocks.Length; index++)
        {
            XZoneMemoryBlock block = orderedBlocks[index]
                ?? throw new ArgumentException($"XZoneMemory block {index} is null.", nameof(blocks));

            if ((int)block.Type != index)
            {
                throw new ArgumentException(
                    $"XZoneMemory block {index} must be {(XFileBlockType)index}, got {block.Type}.",
                    nameof(blocks));
            }
        }

        _blocks = Array.AsReadOnly(orderedBlocks);
    }

    public IReadOnlyList<XZoneMemoryBlock> Blocks => _blocks;

    public bool IsReleased { get; private set; }

    public XZoneMemoryBlock this[XFileBlockType blockType]
    {
        get
        {
            int index = (int)blockType;
            if ((uint)index >= BlockCount)
                throw new ArgumentOutOfRangeException(nameof(blockType));

            return _blocks[index];
        }
    }

    /// <summary>
    /// Releases the seven managed block allocations after every registry,
    /// runtime-side-state, and script-string owner has been retired.
    /// </summary>
    public void Release()
    {
        if (IsReleased)
            return;

        foreach (XZoneMemoryBlock block in _blocks)
            block.Release();

        IsReleased = true;
    }
}
