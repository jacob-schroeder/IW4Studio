using IW4.FastFiles.Database;

namespace IW4.FastFiles.Zone;

// PS3 XZone uses a 0x8c stride, with name bytes at +0x08, zone flags at +0x4c,
// allocator type at +0x50, and XZoneMemory at +0x54. The +0x00 region is
// represented as DBFile; the PS3-only +0x48 word has no known semantic name.
public sealed class XZone
{
    public const int Ps3NativeSize = 0x8c;
    public const int MemoryOffset = 0x54;

    public XZone(
        DbFile file,
        uint unknown48,
        XZoneFlags flags,
        int allocType,
        XZoneMemory memory)
    {
        File = file ?? throw new ArgumentNullException(nameof(file));
        Unknown48 = unknown48;
        Flags = flags;
        AllocType = allocType;
        Memory = memory ?? throw new ArgumentNullException(nameof(memory));
    }

    public DbFile File { get; }

    public string Name => File.Name;

    public uint Unknown48 { get; }

    public XZoneFlags Flags { get; }

    public int AllocType { get; }

    public XZoneMemory Memory { get; }
}
