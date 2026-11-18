using IW4.Studio.Documents.MenuEditing;
using IW4.Studio.Documents.MenuEditing.Behavior.Expressions;
using IW4.Studio.Desktop.Rendering;

namespace IW4.Studio.Desktop.ViewModels.Menu;

/// <summary>
/// Keeps arbitrary authored strings out of presentation contracts that
/// require non-whitespace labels. Source values remain unchanged.
/// </summary>
internal static class MenuPresentationText
{
    public static string MenuTitle(string? name) =>
        !string.IsNullOrWhiteSpace(name) ? name : "Menu";

    public static string OutlineTitle(MenuItemSnapshot item, int index, IMenuTextResourceResolver? resolver)
    {
        MenuItemValue value = item.Value;
        if (!string.IsNullOrWhiteSpace(value.Window.Name))
            return value.Window.Name;
        if (!string.IsNullOrWhiteSpace(value.Text))
        {
            if (value.Text.StartsWith('@') && resolver?.ResolveText(value.Text) is { IsResolved: true } translation &&
                !string.IsNullOrWhiteSpace(translation.DisplayText))
                return translation.DisplayText;
            return value.Text;
        }
        if (item.Behavior.Expressions.Text.Value is { } expression &&
            BehaviorExpressionFormatter.Format(expression).Value is { Length: > 0 } formula)
            return formula;
        if (!string.IsNullOrWhiteSpace(value.Dvar))
            return value.Dvar;
        if (!string.IsNullOrWhiteSpace(value.Window.BackgroundMaterialName))
            return value.Window.BackgroundMaterialName;
        if (!string.IsNullOrWhiteSpace(value.Window.Group))
            return $"{value.Window.Group} · {value.Type}";
        return ItemTitle(value, index);
    }

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
