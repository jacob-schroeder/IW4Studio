using System.Collections.ObjectModel;

namespace IW4.FastFiles.Zone;

// The PS3 decoded-zone header is two uint32 values followed by seven uint32
// block sizes (0x24 bytes total).
public sealed class XFile
{
    public const int SerializedSize = 0x24;
    public const int BlockCount = (int)XFileBlockType.COUNT;

    private readonly ReadOnlyCollection<uint> _blockSizes;

    public XFile(uint size, uint externalSize, IEnumerable<uint> blockSizes)
    {
        ArgumentNullException.ThrowIfNull(blockSizes);

        uint[] sizes = blockSizes.ToArray();
        if (sizes.Length != BlockCount)
        {
            throw new ArgumentException(
                $"PS3 XFile requires exactly {BlockCount} block sizes; got {sizes.Length}.",
                nameof(blockSizes));
        }

        Size = size;
        ExternalSize = externalSize;
        _blockSizes = Array.AsReadOnly(sizes);
    }

    public uint Size { get; }

    public uint ExternalSize { get; }

    public IReadOnlyList<uint> BlockSizes => _blockSizes;

    public uint this[XFileBlockType blockType]
    {
        get
        {
            int index = (int)blockType;
            if ((uint)index >= BlockCount)
                throw new ArgumentOutOfRangeException(nameof(blockType));

            return _blockSizes[index];
        }
    }
}
