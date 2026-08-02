namespace IW4.FastFiles.Zone;

/// <summary>
/// Host-runtime sidecar for one managed XZoneMemory allocation in a
/// PS3-shaped RSX effective-address space. This is not another member of the
/// native 0x08-byte XZoneMemoryBlock; native pointers are execution-specific.
/// </summary>
public sealed record XZoneMemoryBlockRsxPlacement(
    XZoneMemoryBlockRsxLocation Location,
    uint BaseEffectiveOffset);
