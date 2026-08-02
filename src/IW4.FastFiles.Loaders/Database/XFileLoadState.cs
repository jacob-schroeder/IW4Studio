using IW4.FastFiles.Database;
using IW4.FastFiles.Zone;

namespace IW4.FastFiles.Loaders.Database;

/// <summary>
/// Managed state produced by DB_InitLoadXFile and consumed by DB_LoadXFile.
/// The original engine keeps equivalent state in DB globals and the DBFile
/// pipeline; keeping it explicit prevents it from being confused with
/// XZoneMemory.
/// </summary>
public sealed record XFileLoadState(
    DbHeader Header,
    XFile XFile,
    byte[] ZoneBytes,
    int XFileDataOffset);
