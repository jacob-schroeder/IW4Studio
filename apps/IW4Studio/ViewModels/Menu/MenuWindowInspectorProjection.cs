using System.Globalization;
using IW4.Game.Assets.Menu;
using IW4.Game.Zone;
using IW4.Studio.Desktop.Editors.Inspector;
using IW4.Studio.Documents.MenuEditing;

namespace IW4.Studio.Desktop.ViewModels.Menu;

internal static partial class MenuInspectorProjection
{
    private static InspectorSelectionViewModel Window(
        MenuDesignerViewModel designer,
        MenuWindowValue window,
        bool isRoot,
        string title,
        Action<Func<MenuWindowValue, MenuWindowValue>>? update) =>
        new(
            title,
            "WINDOW",
            WindowSections(
                designer,
                window,
                isRoot,
                supportsOwnerDraw: true,
                update: update));

    private static IReadOnlyList<InspectorSectionViewModel> WindowSections(
        MenuDesignerViewModel designer,
        MenuWindowValue window,
        bool isRoot,
        bool supportsOwnerDraw,
        Action<Func<MenuWindowValue, MenuWindowValue>>? update)
    {
        return
        [
            new InspectorSectionViewModel(
                "IDENTITY",
                [
                    Text(
                        "Name",
                        "window.name",
                        window.Name,
                        isRoot || update is null
                            ? null
                            : value => update(current => current with
                            {
                                Name = EmptyToNull(value)
                            }),
                        isRoot
                            ? "The root window uses the menu's name."
                            : null),
                    Text(
                        "Group",
                        "window.group",
                        window.Group,
                        update is null
                            ? null
                            : value => update(current => current with
                            {
                                Group = EmptyToNull(value)
                            }))
                ]),
            new InspectorSectionViewModel(
                "LAYOUT",
                [
                    Rect(
                        "Rect",
                        isRoot ? "window.rect" : "window.rectClient",
                        isRoot ? window.Rect : window.RectClient,
                        update is null
                            ? null
                            : value => update(current => isRoot
                                ? current with
                                {
                                    Rect = Rect(current.Rect, value)
                                }
                                : current with
                                {
                                    RectClient = Rect(
                                        current.RectClient,
                                        value)
                                }),
                        isRoot
                            ? "Position and size of the menu on screen."
                            : "Position and size relative to the menu."),
                    Choice(
                        "Horizontal",
                        isRoot
                            ? "window.rect.horizontalAlignment"
                            : "window.rectClient.horizontalAlignment",
                        isRoot
                            ? window.Rect.HorizontalAlignment
                            : window.RectClient.HorizontalAlignment,
                        update is null
                            ? null
                            : value => update(current => isRoot
                                ? current with
                                {
                                    Rect = current.Rect with
                                    {
                                        HorizontalAlignment = value
                                    }
                                }
                                : current with
                                {
                                    RectClient = current.RectClient with
                                    {
                                        HorizontalAlignment = value
                                    }
                                })),
                    Choice(
                        "Vertical",
                        isRoot
                            ? "window.rect.verticalAlignment"
                            : "window.rectClient.verticalAlignment",
                        isRoot
                            ? window.Rect.VerticalAlignment
                            : window.RectClient.VerticalAlignment,
                        update is null
                            ? null
                            : value => update(current => isRoot
                                ? current with
                                {
                                    Rect = current.Rect with
                                    {
                                        VerticalAlignment = value
                                    }
                                }
                                : current with
                                {
                                    RectClient = current.RectClient with
                                    {
                                        VerticalAlignment = value
                                    }
                                })),
                    Rect(
                        isRoot ? "Client rect" : "Resolved rect",
                        isRoot ? "window.rectClient" : "window.rect",
                        isRoot ? window.RectClient : window.Rect,
                        apply: null,
                        isRoot
                            ? "The root window's client rectangle in the game."
                            : "The game calculates this screen position from the item and menu rectangles.")
                ]),
            new InspectorSectionViewModel(
                "APPEARANCE",
                [
                    Choice(
                        "Style",
                        "window.style",
                        window.Style,
                        update is null
                            ? null
                            : value => update(current => current with
                            {
                                Style = value
                            })),
                    Choice(
                        "Border",
                        "window.border",
                        window.Border,
                        update is null
                            ? null
                            : value => update(current => current with
                            {
                                Border = value
                            })),
                    new InspectorFloatPropertyRowViewModel(
                        "Border size",
                        "window.borderSize",
                        window.BorderSize,
                        update is null
                            ? null
                            : value => update(current => current with
                            {
                                BorderSize = value
                            })),
                    new InspectorAssetReferencePropertyRowViewModel(
                        "Background material",
                        "window.backgroundMaterial",
                        XAssetType.Material,
                        window.BackgroundMaterialName,
                        apply: update is null
                            ? null
                            : name => update(current => current with
                            {
                                BackgroundMaterialName = name
                            }),
                        requestSelection: update is null
                            ? null
                            : designer.RequestAssetReferenceSelection,
                        isMissing: designer.IsAssetReferenceMissing(
                            XAssetType.Material,
                            window.BackgroundMaterialName),
                        description: "Backgrounds reference Material assets."),
                    Color(
                        "Foreground",
                        "window.foreColor",
                        window.ForeColor,
                        update is null
                            ? null
                            : value => update(current => current with
                            {
                                ForeColor = Color(value)
                            })),
                    Color(
                        "Background color",
                        "window.backColor",
                        window.BackColor,
                        update is null
                            ? null
                            : value => update(current => current with
                            {
                                BackColor = Color(value)
                            })),
                    Color(
                        "Border color",
                        "window.borderColor",
                        window.BorderColor,
                        update is null
                            ? null
                            : value => update(current => current with
                            {
                                BorderColor = Color(value)
                            })),
                    Color(
                        "Outline",
                        "window.outlineColor",
                        window.OutlineColor,
                        update is null
                            ? null
                            : value => update(current => current with
                            {
                                OutlineColor = Color(value)
                            })),
                    Color(
                        "Disabled",
                        "window.disableColor",
                        window.DisableColor,
                        update is null
                            ? null
                            : value => update(current => current with
                            {
                                DisableColor = Color(value)
                            }))
                ]),
            new InspectorSectionViewModel(
                "ADVANCED",
                [
                    .. OwnerDrawRows(window, supportsOwnerDraw, update),
                    Flags(
                        "Static flags",
                        "window.staticFlags",
                        window.StaticFlags,
                        update is null
                            ? null
                            : value => update(current => current with
                            {
                                StaticFlags = value
                            })),
                    .. window.DynamicFlags.Select((value, clientIndex) =>
                        Flags(
                            $"Dynamic flags · Client {clientIndex}",
                            $"window.dynamicFlags[{clientIndex}]",
                            value,
                            apply: update is null
                                ? null
                                : replacement => update(current => current with
                                {
                                    DynamicFlags = ReplaceDynamicFlag(
                                        current.DynamicFlags,
                                        clientIndex,
                                        replacement)
                                }),
                            description: "Initial flags for each client. The game can change them during play."))
                ])
        ];
    }

    private static IReadOnlyList<InspectorPropertyRowViewModel> OwnerDrawRows(
        MenuWindowValue window,
        bool isSupported,
        Action<Func<MenuWindowValue, MenuWindowValue>>? update)
    {
        List<InspectorPropertyRowViewModel> rows = [];
        if (isSupported)
        {
            rows.Add(OwnerDrawChoice(
                window.OwnerDraw,
                update is null
                    ? null
                    : value => update(current => current with
                    {
                        OwnerDraw = value
                    }),
                "Select the HUD or menu content the game draws here."));
        }
        else if (window.OwnerDraw is not WindowOwnerDraw.None)
        {
            rows.Add(ReadOnly(
                "Owner draw (inactive)",
                "window.ownerDraw",
                OwnerDrawDisplay(window.OwnerDraw)));
        }

        if (window.OwnerDrawFlags != 0)
        {
            rows.Add(ReadOnly(
                "Owner-draw flags (raw)",
                "window.ownerDrawFlags",
                $"0x{window.OwnerDrawFlags:X8}"));
        }

        return rows;
    }

    private static InspectorChoicePropertyRowViewModel OwnerDrawChoice(
        WindowOwnerDraw value,
        Action<WindowOwnerDraw>? apply,
        string description)
    {
        List<InspectorChoice> choices = Enum.GetValues<WindowOwnerDraw>()
            .Select(option => new InspectorChoice(
                ((int)option).ToString(CultureInfo.InvariantCulture),
                OwnerDrawDisplay(option)))
            .ToList();
        int rawValue = (int)value;
        if (IsNoOpOwnerDraw(rawValue))
        {
            choices.Add(new InspectorChoice(
                rawValue.ToString(CultureInfo.InvariantCulture),
                $"{rawValue} · No runtime handler"));
        }

        return new InspectorChoicePropertyRowViewModel(
            "Owner draw",
            "window.ownerDraw",
            choices,
            rawValue.ToString(CultureInfo.InvariantCulture),
            apply is null
                ? null
                : selected => apply((WindowOwnerDraw)int.Parse(
                    selected,
                    CultureInfo.InvariantCulture)),
            description);
    }

    private static bool IsNoOpOwnerDraw(int value) =>
        !Enum.IsDefined(typeof(WindowOwnerDraw), value);

    private static string OwnerDrawDisplay(WindowOwnerDraw value) =>
        IsNoOpOwnerDraw((int)value)
            ? $"{(int)value} · No runtime handler"
            : DisplayEnum(value);

    private static IReadOnlyList<WindowDynamicFlags> ReplaceDynamicFlag(
        IReadOnlyList<WindowDynamicFlags> current,
        int clientIndex,
        WindowDynamicFlags replacement)
    {
        WindowDynamicFlags[] values = current.ToArray();
        values[clientIndex] = replacement;
        return Array.AsReadOnly(values);
    }
}
