using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Menu;

public sealed class ItemKeyHandler
{
    public const int SerializedSize = 0x0c;

    public int Key { get; init; }
    public XPointer<MenuEventHandlerSet> Action { get; init; }
    public MenuEventHandlerSet? ActionSet { get; set; }
    public XPointer<ItemKeyHandler> Next { get; init; }
    public ItemKeyHandler? NextHandler { get; set; }
}
