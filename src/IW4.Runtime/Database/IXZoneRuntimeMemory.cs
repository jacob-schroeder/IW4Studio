using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;

namespace IW4.Runtime.Database;

/// <summary>
/// Runtime ownership view of one XZone's materialized block memory. Runtime
/// consumers can inspect snapshots and retire the exact allocation without
/// gaining access to loader cursor, alignment, or block-stack mechanics.
/// </summary>
public interface IXZoneRuntimeMemory : IXAssetSourceMemory
{
    XZoneMemory? ZoneMemory { get; }

    int GetMaterializedLength(XFileBlockType block);

    int GetPosition(XFileBlockType block);

    /// <summary>
    /// Irreversibly releases the exact zone allocation. A successful return
    /// must leave <paramref name="memory"/> with <see cref="XZoneMemory.IsReleased"/>
    /// set; implementations must throw when they cannot complete the release.
    /// </summary>
    void ReleaseZoneMemory(XZoneMemory memory);
}
