using IW4.Runtime.Assets;
using IW4.Runtime.Strings;

namespace IW4.Runtime.Database;

internal sealed record DbRuntimeState(
    XAssetPoolState AssetPool,
    ScriptStringTableState ScriptStrings,
    MaterialTechniqueStateCacheState MaterialTechniqueStates,
    DbLoadedXZone[] Zones,
    DbLoadedXZone? CurrentZone,
    DbLoadedXZone? StagedZone,
    Exception? PostLoadFailure,
    long NextZoneHandle);
