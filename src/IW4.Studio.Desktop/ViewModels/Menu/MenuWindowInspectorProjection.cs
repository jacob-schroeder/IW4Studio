using System.Globalization;
using IW4.Assets.Assets.Menu;
using IW4.FastFiles.Zone;
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
            WindowSections(designer, window, isRoot, update),
            isRoot
                ? "The root Window Rect defines the Menu canvas. RectClient is preserved runtime state."
                : "The Item RectClient is authored local geometry. Rect is recomputed from it at runtime and shown read-only.");

    private static IReadOnlyList<InspectorSectionViewModel> WindowSections(
        MenuDesignerViewModel designer,
        MenuWindowValue window,
        bool isRoot,
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
                            ? "The root Window name is the Menu identity and is locked."
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
                        "window.rect",
                        window.Rect,
                        update is null || !isRoot
                            ? null
                            : value => update(current => current with
                            {
                                Rect = Rect(current.Rect, value)
                            }),
                        isRoot
                            ? "Authored Menu screen geometry."
                            : "Runtime screen geometry derived from RectClient and the root Menu."),
                    Choice(
                        "Horizontal",
                        "window.rect.horizontalAlignment",
                        window.Rect.HorizontalAlignment,
                        update is null || !isRoot
                            ? null
                            : value => update(current => current with
                            {
                                Rect = current.Rect with
                                {
                                    HorizontalAlignment = value
                                }
                            })),
                    Choice(
                        "Vertical",
                        "window.rect.verticalAlignment",
                        window.Rect.VerticalAlignment,
                        update is null || !isRoot
                            ? null
                            : value => update(current => current with
                            {
                                Rect = current.Rect with
                                {
                                    VerticalAlignment = value
                                }
                            })),
                    Rect(
                        "Client rect",
                        "window.rectClient",
                        window.RectClient,
                        update is null || isRoot
                            ? null
                            : value => update(current => current with
                            {
                                RectClient = Rect(current.RectClient, value)
                            }),
                        isRoot
                            ? "Runtime client geometry is preserved read-only for the root Menu."
                            : "Authored Item geometry relative to the root Menu."),
                    Choice(
                        "Client H",
                        "window.rectClient.horizontalAlignment",
                        window.RectClient.HorizontalAlignment,
                        update is null || isRoot
                            ? null
                            : value => update(current => current with
                            {
                                RectClient = current.RectClient with
                                {
                                    HorizontalAlignment = value
                                }
                            })),
                    Choice(
                        "Client V",
                        "window.rectClient.verticalAlignment",
                        window.RectClient.VerticalAlignment,
                        update is null || isRoot
                            ? null
                            : value => update(current => current with
                            {
                                RectClient = current.RectClient with
                                {
                                    VerticalAlignment = value
                                }
                            }))
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
                        description: "Backgrounds reference Material assets. Selection is enabled when the shared asset picker is attached."),
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
                    Choice(
                        "Owner draw",
                        "window.ownerDraw",
                        window.OwnerDraw,
                        update is null
                            ? null
                            : value => update(current => current with
                            {
                                OwnerDraw = value
                            })),
                    new InspectorIntegerPropertyRowViewModel(
                        "Owner flags",
                        "window.ownerDrawFlags",
                        window.OwnerDrawFlags,
                        update is null
                            ? null
                            : value => update(current => current with
                            {
                                OwnerDrawFlags = value
                            })),
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
                            description: "Serialized per-client initial state; the game may update these flags at runtime."))
                ])
        ];
    }

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
