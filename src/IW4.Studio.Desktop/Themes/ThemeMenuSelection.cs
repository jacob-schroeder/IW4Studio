using Avalonia.Controls;

namespace IW4.Studio.Desktop.Themes;

/// <summary>
/// Keeps native radio-menu state aligned with the application theme.
/// </summary>
internal static class ThemeMenuSelection
{
    public static void Set(Window window, ThemeMode mode)
    {
        ArgumentNullException.ThrowIfNull(window);

        NativeMenu? menu = NativeMenu.GetMenu(window);
        if (menu is not null)
            Set(menu, mode);
    }

    private static void Set(NativeMenu menu, ThemeMode mode)
    {
        foreach (NativeMenuItemBase item in menu.Items)
        {
            if (item is not NativeMenuItem menuItem)
                continue;

            if (menuItem.CommandParameter is string value
                && Enum.TryParse(value, ignoreCase: true, out ThemeMode itemMode))
            {
                menuItem.IsChecked = itemMode == mode;
            }

            if (menuItem.Menu is not null)
                Set(menuItem.Menu, mode);
        }
    }
}
