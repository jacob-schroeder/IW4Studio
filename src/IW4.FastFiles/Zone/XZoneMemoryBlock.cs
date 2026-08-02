namespace IW4.FastFiles.Zone;

// Each PS3 XZoneMemory entry is a 32-bit data address followed by a 32-bit
// size. Data is the managed equivalent of that allocation; this class does
// not model a managed reference as a native address.
public sealed class XZoneMemoryBlock
{
    private byte[] _data;

    public const int Ps3NativeSize = 0x08;

    public XZoneMemoryBlock(
        XFileBlockType type,
        byte[] data,
        XZoneMemoryBlockRsxPlacement? rsxPlacement = null)
    {
        if (type is < XFileBlockType.TEMP or >= XFileBlockType.COUNT)
            throw new ArgumentOutOfRangeException(nameof(type));

        Type = type;
        _data = data ?? throw new ArgumentNullException(nameof(data));
        DeclaredSize = checked((uint)data.Length);
        RsxPlacement = rsxPlacement;
    }

    public XFileBlockType Type { get; }

    public byte[] Data => _data;

    public uint Size => checked((uint)Data.Length);

    public uint DeclaredSize { get; }

    /// <summary>
    /// Optional host logical placement assigned by DB_AllocXZoneMemory. It
    /// supplies the same allocator-relative coordinate consumed by the PS3
    /// renderer without pretending that a managed byte[] has a PS3 CPU
    /// address.
    /// </summary>
    public XZoneMemoryBlockRsxPlacement? RsxPlacement { get; }

    public bool IsReleased { get; private set; }

    internal void Release()
    {
        if (IsReleased)
            return;

        _data = [];
        IsReleased = true;
    }
}
