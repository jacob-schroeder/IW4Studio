using System.Globalization;
using IW4.Game.Assets.Menu;
using IW4.Game.Zone;
using IW4.Studio.Desktop.Editors.Inspector;
using IW4.Studio.Documents.MenuEditing;
using IW4.Studio.Documents.MenuEditing.Behavior;

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
            "Select an item to see its properties.");

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
                                "The referenced item could not be found.")
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
                        "Changing the type resets its type-specific settings. Shared item and window settings are kept."),
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
                        "Identifies the type-specific settings stored with this item. It can differ from Type in existing items and updates automatically when you change Type."),
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
                        "Aligns owner-drawn content horizontally. Use Text align for ordinary text."),
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
                            })),
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
                        "GameMessageWindow items can use windows 0 through 3."),
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
                        "GameMessageWindow items can use modes 0 through 3."),
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
                        "Controls where the item gets its text.")
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
                        "Choose what happens when the condition matches."),
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
                        "The dvar to check against Match values. If either field is empty, the condition always matches.",
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
                        "Matches any listed value, ignoring case. Whitespace or semicolons separate values, quotes preserve spaces, and comparison is textual rather than numeric. Example: \"war\"; \"dom\".",
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
                            value.FocusSoundName))
                ]),
            new InspectorSectionViewModel(
                "EFFECT",
                [
                    ReadOnly(
                        "Material track",
                        "item.imageTrack",
                        ImageTrack(value.ImageTrack),
                        "Set the material registration track on the parent menu."),
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
                        "Turns on this item's timed text effects and pulse sounds.")
                ]),
            Payload(designer, value, updatePayload),
            ItemBehavior(designer, item, value.Behavior)
        ];

        return new InspectorSelectionViewModel(
            MenuPresentationText.ItemTitle(value),
            value.Type.ToString().ToUpperInvariant(),
            sections);
    }

    private static InspectorSectionViewModel ItemBehavior(
        MenuDesignerViewModel designer,
        MenuItemSnapshot item,
        MenuItemBehaviorSummary value) =>
        new(
            "BEHAVIOR",
            [
                new InspectorActionPropertyRowViewModel(
                    "Builder",
                    "item.behavior",
                    BehaviorSummary(item.Behavior),
                    designer.IsEditable && !designer.HasStagedInput
                        ? () => designer.RequestItemBehaviorEdit(item.Id)
                        : null,
                    "Open the event-handler and expression builder",
                    "Open Item behavior builder",
                    "Edit event hooks, key handlers, fixed expressions, and " +
                    "float-expression bindings."),
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

    private static string BehaviorSummary(
        MenuItemBehaviorBindings value)
    {
        int eventSets = new[]
        {
            value.MouseEnterText,
            value.MouseExitText,
            value.MouseEnter,
            value.MouseExit,
            value.Action,
            value.Accept,
            value.OnFocus,
            value.LeaveFocus,
            value.ListBoxDoubleClick
        }.Count(binding => binding.Handlers is not null);
        int expressions = new[]
        {
            value.Expressions.Visible,
            value.Expressions.Disabled,
            value.Expressions.Text,
            value.Expressions.Material
        }.Count(binding => binding.Value is not null) +
            value.Expressions.FloatExpressions.Entries.Length;

        return $"{eventSets} events · " +
            $"{value.KeyHandlers.Handlers.Length} keys · " +
            $"{expressions} expressions";
    }
}
