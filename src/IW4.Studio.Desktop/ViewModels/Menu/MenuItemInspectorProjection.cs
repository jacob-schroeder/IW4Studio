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
                        "Native data tag",
                        "item.dataType",
                        value.DataType.ToString(CultureInfo.InvariantCulture),
                        "Native type-data accessor and allocation tag. It may " +
                        "legitimately differ from Type and is preserved rather " +
                        "than authored directly."),
                    new InspectorIntegerPropertyRowViewModel(
                        "Alignment",
                        "item.align",
                        value.Align,
                        update is null
                            ? null
                            : number => update(current => current with
                            {
                                Align = number
                            })),
                    new InspectorIntegerPropertyRowViewModel(
                        "Font",
                        "item.fontEnum",
                        value.FontEnum,
                        update is null
                            ? null
                            : number => update(current => current with
                            {
                                FontEnum = number
                            })),
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
                    new InspectorIntegerPropertyRowViewModel(
                        "Text style",
                        "item.textStyle",
                        value.TextStyle,
                        update is null
                            ? null
                            : number => update(current => current with
                            {
                                TextStyle = number
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
                    ReadOnly(
                        "Item flags",
                        "item.textSaveGameInfo",
                        $"0x{value.TextSaveGameInfo:X8}",
                        "The exact PS3 semantics of this serialized field are not established, so it is preserved read-only.")
                ]),
            .. WindowSections(designer, value.Window, isRoot: false, updateWindow),
            new InspectorSectionViewModel(
                "DVAR AND INPUT",
                [
                    Text(
                        "Dvar",
                        "item.dvar",
                        value.Dvar,
                        update is null
                            ? null
                            : text => update(current => current with
                            {
                                Dvar = EmptyToNull(text)
                            })),
                    Text(
                        "Dvar test",
                        "item.dvarTest",
                        value.DvarTest,
                        update is null
                            ? null
                            : text => update(current => current with
                            {
                                DvarTest = EmptyToNull(text)
                            })),
                    Text(
                        "Enable dvar",
                        "item.enableDvar",
                        value.EnableDvar,
                        update is null
                            ? null
                            : text => update(current => current with
                            {
                                EnableDvar = EmptyToNull(text)
                            })),
                    new InspectorIntegerPropertyRowViewModel(
                        "Dvar flags",
                        "item.dvarFlags",
                        value.DvarFlags,
                        update is null
                            ? null
                            : number => update(current => current with
                            {
                                DvarFlags = number
                            })),
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
                        description: "Selection is enabled when the shared asset picker is attached."),
                    new InspectorFloatPropertyRowViewModel(
                        "Special",
                        "item.special",
                        value.Special,
                        update is null
                            ? null
                            : number => update(current => current with
                            {
                                Special = number
                            }))
                ]),
            new InspectorSectionViewModel(
                "EFFECT",
                [
                    ReadOnly(
                        "Image track",
                        "item.imageTrack",
                        value.ImageTrack.ToString(
                            CultureInfo.InvariantCulture),
                        "Preserved read-only until its PS3 authoring semantics are established."),
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
                    ReadOnly(
                        "Decay active",
                        "item.decayActive",
                        value.DecayActive.ToString(
                            CultureInfo.InvariantCulture),
                        "FX decay state is runtime-managed and preserved read-only.")
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
