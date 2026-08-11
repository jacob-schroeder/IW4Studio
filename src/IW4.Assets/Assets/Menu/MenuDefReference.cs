using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

/// <summary>One serialized MenuFile registration and its resolved Menu.</summary>
public sealed record MenuDefReference(
    int Index,
    XPointer<MenuDefAsset> Pointer,
    MenuDefAsset? CanonicalMenu);
