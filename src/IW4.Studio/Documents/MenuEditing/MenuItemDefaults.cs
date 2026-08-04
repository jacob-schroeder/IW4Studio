using IW4.Assets.Assets.Menu;

namespace IW4.Studio.Documents.MenuEditing;

/// <summary>
/// Single source of defaults for new items and item-type changes. The shape
/// always matches the engine union arm and fixed-array requirements.
/// </summary>
public static class MenuItemDefaults
{
    public static MenuItemValue CreateValue(
        ItemDefType type,
        string? name = null)
    {
        var emptyRect = new MenuRectangleValue(
            0,
            0,
            0,
            0,
            HorizontalAlign.HORIZONTAL_ALIGN_SUBLEFT,
            VerticalAlign.VERTICAL_ALIGN_SUBTOP);
        var windowRect = emptyRect with { Width = 100, Height = 30 };
        var window = new MenuWindowValue(
            name,
            windowRect,
            windowRect,
            null,
            WindowStyle.WINDOW_STYLE_EMPTY,
            WindowBorder.WINDOW_BORDER_NONE,
            default,
            0,
            0,
            WindowStaticFlags.None,
            Array.AsReadOnly(new WindowDynamicFlags[4]),
            new MenuColorValue(1, 1, 1, 1),
            new MenuColorValue(0, 0, 0, 0),
            new MenuColorValue(1, 1, 1, 1),
            new MenuColorValue(1, 1, 1, 1),
            new MenuColorValue(1, 1, 1, 1),
            null);

        return new MenuItemValue(
            window,
            Array.AsReadOnly(Enumerable.Repeat(emptyRect, 4).ToArray()),
            type,
            (int)type,
            0,
            0,
            0,
            0,
            0,
            0.55f,
            0,
            0,
            0,
            null,
            0,
            null,
            null,
            null,
            0,
            null,
            0,
            Array.AsReadOnly(new int[4]),
            0,
            new MenuColorValue(0, 0, 0, 0),
            0,
            CreatePayload(type),
            EmptyBehavior);
    }

    public static MenuItemValue ChangeType(
        MenuItemValue item,
        ItemDefType type)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!Enum.IsDefined(type))
            throw new ArgumentOutOfRangeException(nameof(type));
        return MenuSnapshotFactory.Copy(item with
        {
            Type = type,
            DataType = (int)type,
            Payload = CreatePayload(type)
        });
    }

    public static MenuItemPayloadValue CreatePayload(ItemDefType type) =>
        type switch
        {
            ItemDefType.Text or
            ItemDefType.EditField or
            ItemDefType.NumericField or
            ItemDefType.Slider or
            ItemDefType.YesNo or
            ItemDefType.Bind or
            ItemDefType.Validation or
            ItemDefType.DecimalField or
            ItemDefType.UpDown or
            ItemDefType.EmailField or
            ItemDefType.PassWordField => new MenuEditFieldPayloadValue(
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0),
            ItemDefType.ListBox => new MenuListBoxPayloadValue(
                0,
                0,
                0,
                0,
                0,
                Array.AsReadOnly(Enumerable.Range(0, 16)
                    .Select(_ => new MenuListBoxColumnValue(0, 0, 0, 0))
                    .ToArray()),
                false,
                false,
                0,
                new MenuColorValue(0, 0, 0, 0),
                null,
                false),
            ItemDefType.Multi => new MenuMultiPayloadValue(
                0,
                0,
                Array.AsReadOnly(Enumerable.Range(0, MultiDef.EntryCapacity)
                    .Select(_ => new MenuMultiEntryValue(null, null, 0))
                    .ToArray())),
            ItemDefType.DvarEnum => new MenuDvarEnumPayloadValue(null),
            ItemDefType.NewsTicker => new MenuNewsTickerPayloadValue(0, 0, 0, 0),
            ItemDefType.TextScroll => MenuTextScrollPayloadValue.Instance,
            _ when Enum.IsDefined(type) => MenuNoItemPayloadValue.Instance,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

    private static MenuItemBehaviorSummary EmptyBehavior { get; } = new(
        false,
        false,
        false,
        false,
        false,
        false,
        false,
        false,
        false,
        false,
        false,
        false,
        false,
        0);
}
