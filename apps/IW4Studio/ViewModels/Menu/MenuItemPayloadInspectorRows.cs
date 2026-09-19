using System.Globalization;
using IW4.Assets.Assets.Menu;
using IW4.FastFiles.Zone;
using IW4.Studio.Desktop.Editors.Inspector;
using IW4.Studio.Documents.MenuEditing;

namespace IW4.Studio.Desktop.ViewModels.Menu;

internal static partial class MenuInspectorProjection
{
    private static InspectorFloatPropertyRowViewModel PayloadFloat(
        string label,
        string path,
        float value,
        Action<Func<MenuItemValue, MenuItemValue>>? update,
        Func<(MenuEditFieldPayloadValue Payload, float Value), MenuEditFieldPayloadValue> change) =>
        new(
            label,
            path,
            value,
            update is null
                ? null
                : number => update(current => current.Payload is MenuEditFieldPayloadValue payload
                    ? current with { Payload = change((payload, number)) }
                    : throw ChangedPayload()));

    private static InspectorIntegerPropertyRowViewModel PayloadInteger(
        string label,
        string path,
        int value,
        Action<Func<MenuItemValue, MenuItemValue>>? update,
        Func<(MenuEditFieldPayloadValue Payload, int Value), MenuEditFieldPayloadValue> change) =>
        new(
            label,
            path,
            value,
            update is null
                ? null
                : number => update(current => current.Payload is MenuEditFieldPayloadValue payload
                    ? current with { Payload = change((payload, number)) }
                    : throw ChangedPayload()));

    private static InspectorChoicePropertyRowViewModel PayloadBinary(
        string label,
        string path,
        int value,
        Action<Func<MenuItemValue, MenuItemValue>>? update,
        Func<
            (MenuEditFieldPayloadValue Payload, int Value),
            MenuEditFieldPayloadValue> change) =>
        BinaryChoice(
            label,
            path,
            value,
            update is null
                ? null
                : number => update(current =>
                    current.Payload is MenuEditFieldPayloadValue payload
                        ? current with
                        {
                            Payload = change((payload, number))
                        }
                        : throw ChangedPayload()));

    private static InspectorFloatPropertyRowViewModel ListFloat(
        string label,
        string path,
        float value,
        Action<Func<MenuItemValue, MenuItemValue>>? update,
        Func<MenuListBoxPayloadValue, float, MenuListBoxPayloadValue> change) =>
        new(
            label,
            path,
            value,
            update is null
                ? null
                : number => update(current => current.Payload is MenuListBoxPayloadValue payload
                    ? current with { Payload = change(payload, number) }
                    : throw ChangedPayload()));

    private static InspectorIntegerPropertyRowViewModel ListInteger(
        string label,
        string path,
        int value,
        Action<Func<MenuItemValue, MenuItemValue>>? update,
        Func<MenuListBoxPayloadValue, int, MenuListBoxPayloadValue> change) =>
        new(
            label,
            path,
            value,
            update is null
                ? null
                : number => update(current => current.Payload is MenuListBoxPayloadValue payload
                    ? current with { Payload = change(payload, number) }
                    : throw ChangedPayload()));

    private static InspectorBooleanPropertyRowViewModel ListBoolean(
        string label,
        string path,
        bool value,
        Action<Func<MenuItemValue, MenuItemValue>>? update,
        Func<MenuListBoxPayloadValue, bool, MenuListBoxPayloadValue> change) =>
        new(
            label,
            path,
            value,
            update is null
                ? null
                : flag => update(current => current.Payload is MenuListBoxPayloadValue payload
                    ? current with { Payload = change(payload, flag) }
                    : throw ChangedPayload()));

    private static InspectorChoicePropertyRowViewModel ListBinary(
        string label,
        string path,
        int value,
        Action<Func<MenuItemValue, MenuItemValue>>? update,
        Func<MenuListBoxPayloadValue, int, MenuListBoxPayloadValue> change) =>
        BinaryChoice(
            label,
            path,
            value,
            update is null
                ? null
                : number => update(current =>
                    current.Payload is MenuListBoxPayloadValue payload
                        ? current with
                        {
                            Payload = change(payload, number)
                        }
                        : throw ChangedPayload()));

    private static InspectorIntegerPropertyRowViewModel ListColumnInteger(
        string label,
        string path,
        int value,
        int columnIndex,
        Action<Func<MenuItemValue, MenuItemValue>>? update,
        Func<MenuListBoxColumnValue, int, MenuListBoxColumnValue> change) =>
        new(
            label,
            path,
            value,
            update is null
                ? null
                : number => update(current =>
                    current.Payload is MenuListBoxPayloadValue payload
                        ? current with
                        {
                            Payload = payload with
                            {
                                Columns = ReplaceAt(
                                    payload.Columns,
                                    columnIndex,
                                    change(
                                        payload.Columns[columnIndex],
                                        number))
                            }
                        }
                        : throw ChangedPayload()));

    private static InspectorTextPropertyRowViewModel MultiText(
        string label,
        string path,
        string? value,
        int entryIndex,
        Action<Func<MenuItemValue, MenuItemValue>>? update,
        Func<MenuMultiEntryValue, string?, MenuMultiEntryValue> change) =>
        Text(
            label,
            path,
            value,
            update is null
                ? null
                : text => update(current =>
                    current.Payload is MenuMultiPayloadValue payload
                        ? current with
                        {
                            Payload = payload with
                            {
                                Entries = ReplaceAt(
                                    payload.Entries,
                                    entryIndex,
                                    change(
                                        payload.Entries[entryIndex],
                                        EmptyToNull(text)))
                            }
                        }
                        : throw ChangedPayload()));

    private static InspectorFloatPropertyRowViewModel MultiFloat(
        string label,
        string path,
        float value,
        int entryIndex,
        Action<Func<MenuItemValue, MenuItemValue>>? update,
        Func<MenuMultiEntryValue, float, MenuMultiEntryValue> change) =>
        new(
            label,
            path,
            value,
            update is null
                ? null
                : number => update(current =>
                    current.Payload is MenuMultiPayloadValue payload
                        ? current with
                        {
                            Payload = payload with
                            {
                                Entries = ReplaceAt(
                                    payload.Entries,
                                    entryIndex,
                                    change(
                                        payload.Entries[entryIndex],
                                        number))
                            }
                        }
                        : throw ChangedPayload()));

    private static InspectorIntegerPropertyRowViewModel TickerInteger(
        string label,
        string path,
        int value,
        Action<Func<MenuItemValue, MenuItemValue>>? update,
        Func<MenuNewsTickerPayloadValue, int, MenuNewsTickerPayloadValue> change) =>
        new(
            label,
            path,
            value,
            update is null
                ? null
                : number => update(current => current.Payload is MenuNewsTickerPayloadValue payload
                    ? current with { Payload = change(payload, number) }
                    : throw ChangedPayload()));

    private static InspectorFloatPropertyRowViewModel TickerFloat(
        string label,
        string path,
        float value,
        Action<Func<MenuItemValue, MenuItemValue>>? update,
        Func<MenuNewsTickerPayloadValue, float, MenuNewsTickerPayloadValue> change) =>
        new(
            label,
            path,
            value,
            update is null
                ? null
                : number => update(current => current.Payload is MenuNewsTickerPayloadValue payload
                    ? current with { Payload = change(payload, number) }
                    : throw ChangedPayload()));
}
