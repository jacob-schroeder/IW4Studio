using IW4.FastFiles.Zone;
using IW4.Assets.Zone;
using IW4.FastFiles.Pointers;

namespace IW4.Runtime.Database;

public sealed record XAssetListSnapshot(
    int SerializedOffset,
    int ScriptStringCount,
    XPointer<XPointer<string>[]> ScriptStringsPointer,
    IReadOnlyList<XScriptStringEntry> ScriptStrings,
    int AssetCount,
    XPointer<XAsset[]> AssetsPointer,
    IReadOnlyList<XAssetListEntrySnapshot> Assets);
