using System.Globalization;
using IW4.Assets.Assets.Menu;
using IW4.FastFiles.Zone;
using IW4.Studio.Desktop.Editors.Inspector;
using IW4.Studio.Documents.MenuEditing;

namespace IW4.Studio.Desktop.ViewModels.Menu;

internal static partial class MenuInspectorProjection
{
    private static InspectorSelectionViewModel Menu(
        MenuDesignerViewModel designer,
        MenuEditorSnapshot snapshot)
    {
        MenuSettingsValue settings = snapshot.Settings;
        Action<Func<MenuSettingsValue, MenuSettingsValue>>? update =
            designer.IsEditable ? designer.UpdateSettings : null;

        return new InspectorSelectionViewModel(
            MenuPresentationText.MenuTitle(snapshot.Name),
            "MENU",
            [
                new InspectorSectionViewModel(
                    "IDENTITY",
                    [
                        ReadOnly(
                            "Name",
                            "menu.window.name",
                            snapshot.Name,
                            "The root name is the serialized Menu identity and is locked."),
                        ReadOnly(
                            "Items",
                            "menu.items",
                            snapshot.Items.Count.ToString("N0")),
                        ReadOnly(
                            "Complete",
                            "menu.complete",
                            Bool(snapshot.IsComplete))
                    ]),
                new InspectorSectionViewModel(
                    "GENERAL",
                    [
                        Text(
                            "Font",
                            "menu.font",
                            settings.Font,
                            update is null
                                ? null
                                : value => update(current => current with
                                {
                                    Font = EmptyToNull(value)
                                }),
                            "Font-set string used by the Menu definition."),
                        new InspectorBooleanPropertyRowViewModel(
                            "Fullscreen",
                            "menu.fullscreen",
                            settings.Fullscreen != 0,
                            update is null
                                ? null
                                : value => update(current => current with
                                {
                                    Fullscreen = value ? 1 : 0
                                })),
                        new InspectorIntegerPropertyRowViewModel(
                            "Font index",
                            "menu.fontIndex",
                            settings.FontIndex,
                            update is null
                                ? null
                                : value => update(current => current with
                                {
                                    FontIndex = value
                                })),
                        ReadOnly(
                            "Material track",
                            "menu.imageTrack",
                            ImageTrack(settings.ImageTrack),
                            "Engine-supplied int32 material-registration context. " +
                            "Its known values match IMAGE_TRACK_* (commonly UI=3 " +
                            "or HUD=7); it is loader provenance rather than an " +
                            "authored setting, so it is preserved read-only."),
                        Text(
                            "Allowed bind",
                            "menu.allowedBinding",
                            settings.AllowedBinding,
                            update is null
                                ? null
                                : value => update(current => current with
                                {
                                    AllowedBinding = EmptyToNull(value)
                                })),
                        Text(
                            "Sound set",
                            "menu.soundName",
                            settings.SoundName,
                            update is null
                                ? null
                                : value => update(current => current with
                                {
                                    SoundName = EmptyToNull(value)
                                }),
                            "This is an authored sound-set string, not a Sound XAsset reference.")
                    ]),
                new InspectorSectionViewModel(
                    "TRANSITION AND FOCUS",
                    [
                        new InspectorIntegerPropertyRowViewModel(
                            "Fade cycle",
                            "menu.fadeCycle",
                            settings.FadeCycle,
                            update is null
                                ? null
                                : value => update(current => current with
                                {
                                    FadeCycle = value
                                })),
                        new InspectorFloatPropertyRowViewModel(
                            "Fade clamp",
                            "menu.fadeClamp",
                            settings.FadeClamp,
                            update is null
                                ? null
                                : value => update(current => current with
                                {
                                    FadeClamp = value
                                })),
                        new InspectorFloatPropertyRowViewModel(
                            "Fade amount",
                            "menu.fadeAmount",
                            settings.FadeAmount,
                            update is null
                                ? null
                                : value => update(current => current with
                                {
                                    FadeAmount = value
                                })),
                        new InspectorFloatPropertyRowViewModel(
                            "Fade in",
                            "menu.fadeInAmount",
                            settings.FadeInAmount,
                            update is null
                                ? null
                                : value => update(current => current with
                                {
                                    FadeInAmount = value
                                })),
                        new InspectorFloatPropertyRowViewModel(
                            "Blur radius",
                            "menu.blurRadius",
                            settings.BlurRadius,
                            update is null
                                ? null
                                : value => update(current => current with
                                {
                                    BlurRadius = value
                                })),
                        Color(
                            "Focus color",
                            "menu.focusColor",
                            settings.FocusColor,
                            update is null
                                ? null
                                : value => update(current => current with
                                {
                                    FocusColor = Color(value)
                                })),
                        ReadOnly(
                            "Scale rows",
                            "menu.scaleTransitions",
                            settings.ScaleTransitions.Count.ToString("N0")),
                        ReadOnly(
                            "Alpha rows",
                            "menu.alphaTransitions",
                            settings.AlphaTransitions.Count.ToString("N0")),
                        ReadOnly(
                            "X rows",
                            "menu.xTransitions",
                            settings.XTransitions.Count.ToString("N0")),
                        ReadOnly(
                            "Y rows",
                            "menu.yTransitions",
                            settings.YTransitions.Count.ToString("N0"))
                    ]),
                Behavior(snapshot.Behavior)
            ],
            "Select the Window or an Item in the outline for its authored fields.");
    }



    private static InspectorSectionViewModel Behavior(MenuBehaviorSummary value) =>
        new(
            "BEHAVIOR",
            [
                ReadOnly("On open", "menu.onOpen", Bool(value.HasOnOpen)),
                ReadOnly("Close request", "menu.onCloseRequest", Bool(value.HasOnCloseRequest)),
                ReadOnly("On close", "menu.onClose", Bool(value.HasOnClose)),
                ReadOnly("On escape", "menu.onEsc", Bool(value.HasOnEscape)),
                ReadOnly("Key handlers", "menu.execKeys", Bool(value.HasKeyHandlers)),
                ReadOnly("Visibility", "menu.visibleExpression", Bool(value.HasVisibleExpression)),
                ReadOnly(
                    "Rect expressions",
                    "menu.rectExpressions",
                    Count(
                        value.HasRectXExpression,
                        value.HasRectYExpression,
                        value.HasRectWidthExpression,
                        value.HasRectHeightExpression)),
                ReadOnly(
                    "Expression support",
                    "menu.expressionData",
                    Bool(value.HasExpressionSupportingData))
            ]);
}
