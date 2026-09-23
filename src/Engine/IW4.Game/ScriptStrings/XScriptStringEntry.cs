using IW4.Game.Pointers;
using IW4.Game.Zone;

namespace IW4.Game.ScriptStrings;

public sealed record XScriptStringEntry(
    int Index,
    int PointerSerializedOffset,
    XBlockAddress PointerCellAddress,
    XString Pointer,
    string? Value,
    ScriptStringHandle RuntimeHandle);
