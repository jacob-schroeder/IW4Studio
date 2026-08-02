namespace IW4.FastFiles.Zone;

// The PS3 DB_LoadXAssets request row is three 32-bit words in this order
// (0x0c total): name, allocation flags, and free flags.
public readonly record struct XZoneInfo(
    string? Name,
    XZoneFlags AllocFlags,
    XZoneFlags FreeFlags)
{
    public const int Ps3NativeSize = 0x0c;
}
