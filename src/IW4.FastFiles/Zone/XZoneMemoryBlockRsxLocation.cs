namespace IW4.FastFiles.Zone;

/// <summary>
/// PS3 RSX address domain used by a materialized allocation. The numeric
/// values are the serialized RSX location tokens.
/// </summary>
public enum XZoneMemoryBlockRsxLocation : uint
{
    Local = 0,
    Main = 1
}
