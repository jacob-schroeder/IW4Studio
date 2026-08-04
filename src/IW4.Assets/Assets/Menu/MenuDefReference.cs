using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

/// <summary>
/// One serialized MenuFile registration. Inline and insert entries retain
/// the exact incoming body as well as the Menu selected by DB_AddXAsset;
/// packed entries have no incoming body and resolve only to their canonical
/// Menu. Keeping both prevents a same-name earlier provider from hiding a
/// divergent body that was actually present in the source stream.
/// </summary>
public sealed record MenuDefReference(
    int Index,
    XPointer<MenuDefAsset> Pointer,
    MenuDefAsset? IncomingDefinition,
    MenuDefAsset? CanonicalMenu);
