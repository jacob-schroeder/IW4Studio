using IW4.FastFiles.Pointers;
using IW4.FastFiles.Strings;
using IW4.FastFiles.Zone;

namespace IW4.Assets.Zone;

public sealed record XScriptStringEntry(
    int Index,
    int PointerSerializedOffset,
    XBlockAddress PointerCellAddress,
    XString Pointer,
    string? Value,
    ScriptStringHandle RuntimeHandle);
