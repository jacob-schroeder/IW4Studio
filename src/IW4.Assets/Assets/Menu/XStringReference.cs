using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed record XStringReference(
    int Index,
    XString Pointer,
    string? Value);
