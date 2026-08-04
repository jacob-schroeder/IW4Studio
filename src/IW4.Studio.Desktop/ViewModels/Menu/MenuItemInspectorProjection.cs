using System.Globalization;
using IW4.Assets.Assets.Menu;
using IW4.FastFiles.Zone;
using IW4.Studio.Desktop.Editors.Inspector;
using IW4.Studio.Documents.MenuEditing;

namespace IW4.Studio.Desktop.ViewModels.Menu;

internal static partial class MenuInspectorProjection
{
    private static InspectorSelectionViewModel Items(MenuEditorSnapshot snapshot) =>
        new(
            "Items",
            "COLLECTION",
            [
                new InspectorSectionViewModel(
                    "SUMMARY",
                    [
                        ReadOnly(
                            "Count",
                            "menu.items",
                            snapshot.Items.Count.ToString("N0")),
                        ReadOnly(
                            "Resolved",
                            "menu.items.resolved",
                            snapshot.Items.Count(item => item.IsResolved)
                                .ToString("N0")),
                        ReadOnly(
                            "Unresolved",
                            "menu.items.unresolved",
                            snapshot.Items.Count(item => !item.IsResolved)
                                .ToString("N0"))
                    ])
            ],
            "Select an Item to inspect it. Structural commands are hosted by the designer toolbar.");

    private static InspectorSelectionViewModel Item(
        MenuDesignerViewModel designer,
        MenuEditorSnapshot snapshot,
        MenuNodeId itemId)
    {
        MenuItemSnapshot item = snapshot.Items.SingleOrDefault(value =>
                value.Id == itemId)
            ?? throw new InvalidOperationException(
                $"Menu item '{itemId}' is no longer present.");
        if (!item.IsResolved)
        {
            return new InspectorSelectionViewModel(
                "Unresolved Item",
                "ITEM",
                [
                    new InspectorSectionViewModel(
                        "SOURCE",
                        [
                            ReadOnly(
                                "State",
                                "item.resolution",
                                "Unresolved",
                                "This serialized Item pointer has no detached definition.")
                        ])
                ]);
        }

        MenuItemValue value = item.Value;
        Action<Func<MenuItemValue, MenuItemValue>>? update = designer.IsEditable
            ? change => designer.UpdateItem(item.Id, change)
            : null;
        Action<Func<MenuItemValue, MenuItemValue>>? updatePayload =
            designer.IsEditable
                ? change => designer.UpdateItemPayload(item.Id, change)
                : null;
        Action<Func<MenuWindowValue, MenuWindowValue>>? updateWindow = designer.IsEditable
            ? change => designer.UpdateItemWindow(item.Id, change)
            : null;

        List<InspectorSectionViewModel> sections =
        [
            new InspectorSectionViewModel(
                "ITEM",
                [
                    ReadOnly("Identity", "item.id", item.Id.ToString()),
                    Choice(
                        "Type",
                        "item.type",
                        value.Type,
                        designer.CanChangeSelectedItemType
                            ? designer.ChangeSelectedItemType
                            : null,
                        "Changing Type rebuilds the type-specific payload with " +
                        "safe defaults; common Item and Window fields remain."),
                    Text(
                        "Text",
                        "item.text",
                        value.Text,
                        update is null
                            ? null
                            : text => update(current => current with
                            {
                                Text = EmptyToNull(text)
                            })),
                    ReadOnly(
                        "Payload type tag",
                        "item.dataType",
                        PayloadTypeTag(value.DataType),
                        "Internal int32 discriminator for the allocated native " +
                        "type-data union. Runtime accessors use it to select the " +
                        "EditField, ListBox, Multi, or related payload. It may " +
                        "differ from Type and is preserved rather than authored."),
                    Choice(
                        "Owner-draw alignment",
                        "item.align",
                        value.Align,
                        update is null
                            ? null
                            : alignment => update(current => current with
                            {
                                Align = alignment
                            }),
                        "Horizontal alignment passed to owner-draw handlers. It does not control ordinary item text; use Text align for that."),
                    Choice(
                        "Font",
                        "item.fontEnum",
                        value.FontEnum,
                        update is null
                            ? null
                            : font => update(current => current with
                            {
                                FontEnum = font
                            }),
                        "IW4 font role. Default and Normal select a concrete font from the effective text scale."),
                    IntegerChoice(
                        "Text align",
                        "item.textAlignMode",
                        value.TextAlignMode,
                        TextAlignmentChoices(),
                        update is null
                            ? null
                            : number => update(current => current with
                            {
                                TextAlignMode = number
                            }),
                        "Horizontal and vertical text placement inside the item rectangle."),
                    new InspectorFloatPropertyRowViewModel(
                        "Text X",
                        "item.textAlignX",
                        value.TextAlignX,
                        update is null
                            ? null
                            : number => update(current => current with
                            {
                                TextAlignX = number
                            })),
                    new InspectorFloatPropertyRowViewModel(
                        "Text Y",
                        "item.textAlignY",
                        value.TextAlignY,
                        update is null
                            ? null
                            : number => update(current => current with
                            {
                                TextAlignY = number
                            })),
                    new InspectorFloatPropertyRowViewModel(
                        "Text scale",
                        "item.textScale",
                        value.TextScale,
                        update is null
                            ? null
                            : number => update(current => current with
                            {
                                TextScale = number
                            })),
                    Choice(
                        "Text style",
                        "item.textStyle",
                        value.TextStyle,
                        update is null
                            ? null
                            : style => update(current => current with
                            {
                                TextStyle = style
                            }),
                        "Text rendering effect passed to IW4's text-draw helpers."),
                    IntegerChoice(
                        "Game message window",
                        "item.gameMessageWindowIndex",
                        value.GameMessageWindowIndex,
                        Enumerable.Range(0, 4).Select(number =>
                            new InspectorChoice(
                                number.ToString(CultureInfo.InvariantCulture),
                                $"Window {number}")),
                        update is null
                            ? null
                            : number => update(current => current with
                            {
                                GameMessageWindowIndex = number
                            }),
                        "Used only by GameMessageWindow items; the runtime accepts indices 0 through 3."),
                    IntegerChoice(
                        "Game message mode",
                        "item.gameMessageWindowMode",
                        value.GameMessageWindowMode,
                        Enumerable.Range(0, 4).Select(number =>
                            new InspectorChoice(
                                number.ToString(CultureInfo.InvariantCulture),
                                $"Mode {number}")),
                        update is null
                            ? null
                            : number => update(current => current with
                            {
                                GameMessageWindowMode = number
                            }),
                        "Used only by GameMessageWindow items; the runtime accepts modes 0 through 3."),
                    Flags(
                        "Item flags",
                        "item.itemFlags",
                        value.ItemFlags,
                        update is null
                            ? null
                            : flags => update(current => current with
                            {
                                ItemFlags = flags
                            }),
                        "Special text-source flags. Unknown serialized bits are preserved.")
                ]),
            .. WindowSections(
                designer,
                value.Window,
                isRoot: false,
                supportsOwnerDraw: value.Type is ItemDefType.OwnerDraw,
                update: updateWindow),
            new InspectorSectionViewModel(
                "DVAR BINDING AND CONDITIONS",
                [
                    Text(
                        "Bound dvar",
                        "item.dvar",
                        value.Dvar,
                        update is null
                            ? null
                            : text => update(current => current with
                            {
                                Dvar = EmptyToNull(text)
                            }),
                        "Dvar read or written by the item's value control. This is separate from the conditional dvar below."),
                    Flags(
                        "Dvar flags",
                        "item.dvarFlags",
                        value.DvarFlags,
                        update is null
                            ? null
                            : flags => update(current => current with
                            {
                                DvarFlags = flags
                            }),
                        "Chooses what a match does: enable, disable, show, hide, or permit scripted focus. Unknown serialized bits are preserved."),
                    Text(
                        "Condition dvar",
                        "item.dvarTest",
                        value.DvarTest,
                        update is null
                            ? null
                            : text => update(current => current with
                            {
                                DvarTest = EmptyToNull(text)
                            }),
                        "Name of the dvar whose current value is read as text and compared case-insensitively against Match values. If either condition field is empty, the runtime treats the condition as true.",
                        showInfoIcon: true),
                    Text(
                        "Match values",
                        "item.enableDvar",
                        value.EnableDvar,
                        update is null
                            ? null
                            : text => update(current => current with
                            {
                                EnableDvar = EmptyToNull(text)
                            }),
                        "Text is correct here: this is a case-insensitive OR-list, not a dvar name. Whitespace or semicolons separate values, quotes preserve spaces, and comparison is textual rather than numeric. Example: \"war\"; \"dom\".",
                        showInfoIcon: true),
                    new InspectorAssetReferencePropertyRowViewModel(
                        "Focus sound",
                        "item.focusSound",
                        XAssetType.Sound,
                        value.FocusSoundName,
                        apply: update is null
                            ? null
                            : name => update(current => current with
                            {
                                FocusSoundName = name
                            }),
                        requestSelection: update is null
                            ? null
                            : designer.RequestAssetReferenceSelection,
                        isMissing: designer.IsAssetReferenceMissing(
                            XAssetType.Sound,
                            value.FocusSoundName),
                        description: "Selection is enabled when the shared asset picker is attached.")
                ]),
            new InspectorSectionViewModel(
                "EFFECT",
                [
                    ReadOnly(
                        "Material track",
                        "item.imageTrack",
                        ImageTrack(value.ImageTrack),
                        "Engine-supplied int32 material-registration context " +
                        "inherited from the menu during raw menu loading. Its " +
                        "known values match the engine's IMAGE_TRACK_* table, " +
                        "but it is provenance rather than an image selector or " +
                        "an authored setting, so it is preserved read-only."),
                    Color(
                        "Glow",
                        "item.glowColor",
                        value.GlowColor,
                        update is null
                            ? null
                            : color => update(current => current with
                            {
                                GlowColor = Color(color)
                            })),
                    new InspectorBooleanPropertyRowViewModel(
                        "Text FX active",
                        "item.decayActive",
                        value.DecayActive != 0,
                        update is null
                            ? null
                            : enabled => update(current => current with
                            {
                                DecayActive = enabled ? (byte)1 : (byte)0
                            }),
                        "Native one-byte boolean. When enabled, text painting " +
                        "runs the configured text-FX timing lifecycle and pulse " +
                        "sounds. The runtime mutates the neighboring birth-time " +
                        "and last-sound cache fields, not this toggle.")
                ]),
            Payload(designer, value, updatePayload),
            ItemBehavior(value.Behavior)
        ];

        return new InspectorSelectionViewModel(
            MenuPresentationText.ItemTitle(value),
            value.Type.ToString().ToUpperInvariant(),
            sections,
            "Item and Window fields are presented together to avoid a second nested selection level.");
    }

    private static InspectorSectionViewModel ItemBehavior(
        MenuItemBehaviorSummary value) =>
        new(
            "BEHAVIOR",
            [
                ReadOnly("Mouse enter text", "item.mouseEnterText", Bool(value.HasMouseEnterText)),
                ReadOnly("Mouse exit text", "item.mouseExitText", Bool(value.HasMouseExitText)),
                ReadOnly("Mouse enter", "item.mouseEnter", Bool(value.HasMouseEnter)),
                ReadOnly("Mouse exit", "item.mouseExit", Bool(value.HasMouseExit)),
                ReadOnly("Action", "item.action", Bool(value.HasAction)),
                ReadOnly("Accept", "item.accept", Bool(value.HasAccept)),
                ReadOnly("On focus", "item.onFocus", Bool(value.HasOnFocus)),
                ReadOnly("Leave focus", "item.leaveFocus", Bool(value.HasLeaveFocus)),
                ReadOnly("Key handlers", "item.onKey", Bool(value.HasKeyHandlers)),
                ReadOnly(
                    "Expressions",
                    "item.expressions",
                    Count(
                        value.HasVisibleExpression,
                        value.HasDisabledExpression,
                        value.HasTextExpression,
                        value.HasMaterialExpression)),
                ReadOnly(
                    "Float expressions",
                    "item.floatExpressions",
                    value.FloatExpressionCount.ToString("N0"))
            ]);
}
