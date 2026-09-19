using IW4.Studio.Documents.MenuEditing;

namespace IW4.Studio.Desktop.ViewModels.Menu;

/// <summary>
/// Keeps arbitrary authored strings out of presentation contracts that
/// require non-whitespace labels. Source values remain unchanged.
/// </summary>
internal static class MenuPresentationText
{
    public static string MenuTitle(string? name) =>
        !string.IsNullOrWhiteSpace(name) ? name : "Menu";

    public static string ItemTitle(MenuItemValue value, int? index = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!string.IsNullOrWhiteSpace(value.Window.Name))
            return value.Window.Name;
        if (!string.IsNullOrWhiteSpace(value.Text))
            return value.Text;
        return index is { } itemIndex
            ? $"{value.Type} {itemIndex + 1:N0}"
            : value.Type.ToString();
    }
}
