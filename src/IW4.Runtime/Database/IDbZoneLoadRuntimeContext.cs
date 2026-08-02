using IW4.Runtime.Assets;
using IW4.Runtime.Assets.Lifecycle;
using IW4.Runtime.Diagnostics;
using IW4.Runtime.Strings;

namespace IW4.Runtime.Database;

/// <summary>
/// Runtime-owned view of one loader context. It exposes the registry identity,
/// retained zone memory, and warnings needed after loading without exposing
/// source cursors or pointer-resolution operations.
/// </summary>
public interface IDbZoneLoadRuntimeContext
{
    IXZoneRuntimeMemory Blocks { get; }

    XAssetLoadSession AssetLoadSession { get; }

    MaterialTechniqueStateCache MaterialTechniqueStateCache { get; }

    IGfxImageRuntimeRegistrationHooks? GfxImageRuntimeRegistrationHooks { get; }

    ManagedXAssetRuntimeLifecycle AssetRuntimeLifecycle { get; }

    LoadDiagnostics Diagnostics { get; }

    XAssetPool AssetPool => AssetLoadSession.AssetPool;

    ScriptStringTable ScriptStrings => AssetLoadSession.ScriptStrings;

    ZoneScriptStringTable ZoneScriptStrings => AssetLoadSession.ZoneScriptStrings;

    DbZoneHandle ZoneOwner => AssetLoadSession.ZoneOwner;
}
