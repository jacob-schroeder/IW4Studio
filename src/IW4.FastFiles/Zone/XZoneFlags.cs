namespace IW4.FastFiles.Zone;

// PS3 DB_LoadXAssets uses these five numeric bits as zone allocation/free
// categories.
[Flags]
public enum XZoneFlags : int
{
    None = 0,
    DB_ZONE_COMMON = 0x01,
    DB_ZONE_UI = 0x02,
    DB_ZONE_GAME = 0x04,
    DB_ZONE_LOAD = 0x08,
    DB_ZONE_DEV = 0x10
}
