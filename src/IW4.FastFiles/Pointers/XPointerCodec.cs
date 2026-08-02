using IW4.FastFiles.Zone;

namespace IW4.FastFiles.Pointers;

public static class XPointerCodec
{
    public static PointerType GetType(int value)
    {
        if (value == 0)  return PointerType.Null;
        if (value == -1) return PointerType.Inline;
        if (value == -2) return PointerType.Insert;
        return PointerType.Offset;
    }

    public static int Offset(int value) => (value & 0x0FFFFFFF) - 1;
    public static int BlockIndex(int value) => (int)((uint)value >> 28);
    public static int Encode(XBlockAddress address)
    {
        int blockIndex = (int)address.BlockType;
        if (blockIndex < 0 || blockIndex >= (int)XFileBlockType.COUNT)
            throw new ArgumentOutOfRangeException(nameof(address), address, "Invalid XFile block address.");
        if (address.Offset < 0 || address.Offset >= 0x0fffffff)
            throw new ArgumentOutOfRangeException(nameof(address), address, "XFile block offset cannot be encoded in 28 bits.");

        return (blockIndex << 28) | (address.Offset + 1);
    }

    public static bool TryDecodeBlockAddress(int value, out XBlockAddress address)
    {
        int blockIndex = BlockIndex(value);
        if (GetType(value) != PointerType.Offset ||
            blockIndex < 0 ||
            blockIndex >= (int)XFileBlockType.COUNT)
        {
            address = default;
            return false;
        }

        address = new XBlockAddress((XFileBlockType)blockIndex, Offset(value));
        return true;
    }

    public static XBlockAddress Decode(int value)
    {
        if (!TryDecodeBlockAddress(value, out XBlockAddress address))
        {
            throw new InvalidDataException(
                $"Runtime pointer 0x{unchecked((uint)value):X8} is not an XBlockAddress encoding.");
        }

        return address;
    }
}
