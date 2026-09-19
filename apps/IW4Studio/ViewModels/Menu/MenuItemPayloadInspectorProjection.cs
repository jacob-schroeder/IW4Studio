using System.Globalization;
using IW4.Assets.Assets.Menu;
using IW4.FastFiles.Zone;
using IW4.Studio.Desktop.Editors.Inspector;
using IW4.Studio.Documents.MenuEditing;

namespace IW4.Studio.Desktop.ViewModels.Menu;

internal static partial class MenuInspectorProjection
{
    private static InspectorSectionViewModel Payload(
        MenuDesignerViewModel designer,
        MenuItemValue item,
        Action<Func<MenuItemValue, MenuItemValue>>? update)
    {
        List<InspectorPropertyRowViewModel> rows =
        [
            .. SpecialRows(item, update)
        ];
        switch (item.Payload)
        {
            case MenuEditFieldPayloadValue edit:
                rows.AddRange(
                [
                    PayloadFloat("Minimum", "item.payload.min", edit.MinValue, update,
                        payload => payload.Payload with { MinValue = payload.Value }),
                    PayloadFloat("Maximum", "item.payload.max", edit.MaxValue, update,
                        payload => payload.Payload with { MaxValue = payload.Value }),
                    PayloadFloat("Default", "item.payload.default", edit.DefaultValue, update,
                        payload => payload.Payload with { DefaultValue = payload.Value }),
                    PayloadFloat("Range", "item.payload.range", edit.Range, update,
                        payload => payload.Payload with { Range = payload.Value }),
                    PayloadInteger("Max chars", "item.payload.maxChars", edit.MaxChars, update,
                        payload => payload.Payload with { MaxChars = payload.Value }),
                    PayloadBinary(
                        "Advance at max chars",
                        "item.payload.maxCharsGotoNext",
                        edit.MaxCharsGotoNext,
                        update,
                        payload => payload.Payload with
                        {
                            MaxCharsGotoNext = payload.Value
                        }),
                    PayloadInteger("Paint chars", "item.payload.maxPaintChars", edit.MaxPaintChars, update,
                        payload => payload.Payload with { MaxPaintChars = payload.Value }),
                    ReadOnly(
                        "Visible text start",
                        "item.payload.paintOffset",
                        edit.PaintOffset.ToString(CultureInfo.InvariantCulture),
                        "Internal int32 index of the first character drawn in an " +
                        "edit field. The runtime resets and moves it to keep the " +
                        "cursor visible, so it is preserved read-only.")
                ]);
                break;

            case MenuListBoxPayloadValue list:
                rows.AddRange(
                [
                    ReadOnly(
                        "Draw padding",
                        "item.payload.drawPadding",
                        list.DrawPadding.ToString(CultureInfo.InvariantCulture),
                        "Computed ListBox paint state is preserved read-only."),
                    ListFloat("Element width", "item.payload.elementWidth", list.ElementWidth, update,
                        (payload, number) => payload with { ElementWidth = number }),
                    ListFloat("Element height", "item.payload.elementHeight", list.ElementHeight, update,
                        (payload, number) => payload with { ElementHeight = number }),
                    ListInteger("Element style", "item.payload.elementStyle", list.ElementStyle, update,
                        (payload, number) => payload with { ElementStyle = number }),
                    ListInteger("Columns", "item.payload.numColumns", list.NumColumns, update,
                        (payload, number) => payload with { NumColumns = number }),
                    ListBoolean("Not selectable", "item.payload.notSelectable", list.NotSelectable, update,
                        (payload, flag) => payload with { NotSelectable = flag }),
                    ListBoolean("No scrollbars", "item.payload.noScrollbars", list.NoScrollbars, update,
                        (payload, flag) => payload with { NoScrollbars = flag }),
                    ListBinary("Use paging", "item.payload.usePaging", list.UsePaging, update,
                        (payload, number) => payload with { UsePaging = number }),
                    Color(
                        "Select border",
                        "item.payload.selectBorder",
                        list.SelectBorder,
                        update is null
                            ? null
                            : color => update(current =>
                                current.Payload is MenuListBoxPayloadValue payload
                                    ? current with
                                    {
                                        Payload = payload with
                                        {
                                            SelectBorder = Color(color)
                                        }
                                    }
                                    : throw ChangedPayload())),
                    new InspectorAssetReferencePropertyRowViewModel(
                        "Select icon",
                        "item.payload.selectIcon",
                        XAssetType.Material,
                        list.SelectIconMaterialName,
                        apply: update is null
                            ? null
                            : name => update(current => current.Payload is MenuListBoxPayloadValue payload
                                ? current with
                                {
                                    Payload = payload with
                                    {
                                        SelectIconMaterialName = name
                                    }
                                }
                                : throw ChangedPayload()),
                        requestSelection: update is null
                            ? null
                            : designer.RequestAssetReferenceSelection,
                        isMissing: designer.IsAssetReferenceMissing(
                            XAssetType.Material,
                            list.SelectIconMaterialName)),
                    ReadOnly("Double click", "item.payload.doubleClick", Bool(list.HasDoubleClickHandler))
                ]);
                for (int index = 0; index < list.Columns.Count; index++)
                {
                    int columnIndex = index;
                    MenuListBoxColumnValue column = list.Columns[index];
                    string columnPath =
                        $"item.payload.columns[{columnIndex}]";
                    rows.AddRange(
                    [
                        ListColumnInteger(
                            $"Column {columnIndex} position",
                            $"{columnPath}.position",
                            column.Position,
                            columnIndex,
                            update,
                            (current, number) => current with
                            {
                                Position = number
                            }),
                        ListColumnInteger(
                            $"Column {columnIndex} width",
                            $"{columnPath}.width",
                            column.Width,
                            columnIndex,
                            update,
                            (current, number) => current with
                            {
                                Width = number
                            }),
                        ListColumnInteger(
                            $"Column {columnIndex} max chars",
                            $"{columnPath}.maxChars",
                            column.MaxChars,
                            columnIndex,
                            update,
                            (current, number) => current with
                            {
                                MaxChars = number
                            }),
                        ListColumnInteger(
                            $"Column {columnIndex} alignment",
                            $"{columnPath}.alignment",
                            column.Alignment,
                            columnIndex,
                            update,
                            (current, number) => current with
                            {
                                Alignment = number
                            })
                    ]);
                }
                break;

            case MenuMultiPayloadValue multi:
                rows.AddRange(
                [
                    new InspectorIntegerPropertyRowViewModel(
                        "Count",
                        "item.payload.count",
                        multi.Count,
                        update is null
                            ? null
                            : number => update(current => current.Payload is MenuMultiPayloadValue payload
                                ? current with { Payload = payload with { Count = number } }
                                : throw ChangedPayload())),
                    BinaryChoice(
                        "Entry mode",
                        "item.payload.strDef",
                        multi.StringDefinition,
                        update is null
                            ? null
                            : number => update(current => current.Payload is MenuMultiPayloadValue payload
                                ? current with { Payload = payload with { StringDefinition = number } }
                                : throw ChangedPayload()),
                        falseLabel: "Numeric values",
                        trueLabel: "String values"),
                    ReadOnly("Capacity", "item.payload.capacity", multi.Entries.Count.ToString("N0"))
                ]);
                for (int index = 0; index < multi.Entries.Count; index++)
                {
                    int entryIndex = index;
                    MenuMultiEntryValue entry = multi.Entries[index];
                    string entryPath = $"item.payload.entries[{entryIndex}]";
                    rows.AddRange(
                    [
                        MultiText(
                            $"Entry {entryIndex} dvar list",
                            $"{entryPath}.dvarList",
                            entry.DvarListValue,
                            entryIndex,
                            update,
                            (current, text) => current with
                            {
                                DvarListValue = text
                            }),
                        MultiText(
                            $"Entry {entryIndex} dvar string",
                            $"{entryPath}.dvarString",
                            entry.DvarStringValue,
                            entryIndex,
                            update,
                            (current, text) => current with
                            {
                                DvarStringValue = text
                            }),
                        MultiFloat(
                            $"Entry {entryIndex} value",
                            $"{entryPath}.value",
                            entry.NumericValue,
                            entryIndex,
                            update,
                            (current, number) => current with
                            {
                                NumericValue = number
                            })
                    ]);
                }
                break;

            case MenuDvarEnumPayloadValue dvar:
                rows.Add(Text(
                    "Dvar name",
                    "item.payload.dvarEnum",
                    dvar.DvarName,
                    update is null
                        ? null
                        : text => update(current => current.Payload is MenuDvarEnumPayloadValue payload
                            ? current with
                            {
                                Payload = payload with { DvarName = EmptyToNull(text) }
                            }
                            : throw ChangedPayload())));
                break;

            case MenuNewsTickerPayloadValue ticker:
                rows.AddRange(
                [
                    TickerInteger("Feed", "item.payload.feedId", ticker.FeedId, update,
                        (payload, number) => payload with { FeedId = number }),
                    TickerInteger("Speed", "item.payload.speed", ticker.Speed, update,
                        (payload, number) => payload with { Speed = number }),
                    TickerInteger("Spacing", "item.payload.spacing", ticker.Spacing, update,
                        (payload, number) => payload with { Spacing = number }),
                    TickerFloat("X", "item.payload.x", ticker.X, update,
                        (payload, number) => payload with { X = number })
                ]);
                break;

            case MenuTextScrollPayloadValue:
                rows.Add(ReadOnly(
                    "Authored fields",
                    "item.payload",
                    "None",
                    "TextScroll timing is runtime state and is intentionally hidden."));
                break;

            default:
                rows.Add(ReadOnly("Payload", "item.payload", "None"));
                break;
        }

        return new InspectorSectionViewModel(
            "TYPE-SPECIFIC",
            rows,
            isExpanded: item.Payload is not (
                MenuListBoxPayloadValue or MenuMultiPayloadValue));
    }

    private static IReadOnlyList<InspectorPropertyRowViewModel> SpecialRows(
        MenuItemValue item,
        Action<Func<MenuItemValue, MenuItemValue>>? update) => item.Type switch
        {
            ItemDefType.ListBox =>
            [
                new InspectorFloatPropertyRowViewModel(
                    "Feeder ID",
                    "item.special",
                    item.Special,
                    update is null
                        ? null
                        : number => update(current => current with
                        {
                            Special = number
                        }),
                    "UI feeder identifier used to query ListBox rows, content, and selection. The runtime stores feeder IDs as floats, although authored values are commonly whole numbers.")
            ],
            ItemDefType.OwnerDraw =>
            [
                ReadOnly(
                    "Special (unused)",
                    "item.special",
                    item.Special.ToString("R", CultureInfo.InvariantCulture),
                    "Legacy float passed through the owner-draw API. The checked PS3 and Xbox 360 MW2 dispatchers never consume it, so it is preserved read-only.")
            ],
            _ when item.Special != 0f =>
            [
                ReadOnly(
                    "Special (raw)",
                    "item.special",
                    item.Special.ToString("R", CultureInfo.InvariantCulture),
                    "Preserved nonzero value with no proven meaning for this Item type.")
            ],
            _ => []
        };
}
