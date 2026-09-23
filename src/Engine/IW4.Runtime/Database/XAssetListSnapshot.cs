using IW4.Game.ScriptStrings;
using IW4.Game.Zone;
using IW4.Game.Pointers;

namespace IW4.Runtime.Database;

public sealed record XAssetListSnapshot(
    int SerializedOffset,
    int ScriptStringCount,
    XPointer<XPointer<string>[]> ScriptStringsPointer,
    IReadOnlyList<XScriptStringEntry> ScriptStrings,
    int AssetCount,
    XPointer<XAsset[]> AssetsPointer,
    IReadOnlyList<XAssetListEntrySnapshot> Assets);
