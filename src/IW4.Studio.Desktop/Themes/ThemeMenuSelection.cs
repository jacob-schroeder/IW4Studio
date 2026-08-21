using Avalonia.Controls;

namespace IW4.Studio.Desktop.Themes;

/// <summary>
/// Keeps native and in-window radio-menu state aligned with the application theme.
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

    public static void Set(Window window, Menu windowMenu, ThemeMode mode)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(windowMenu);

        Set(window, mode);
        Set(windowMenu, mode);
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

    private static void Set(Menu menu, ThemeMode mode)
    {
        foreach (MenuItem menuItem in menu.Items.OfType<MenuItem>())
            Set(menuItem, mode);
    }

    private static void Set(MenuItem menuItem, ThemeMode mode)
    {
        if (menuItem.CommandParameter is string value
            && Enum.TryParse(value, ignoreCase: true, out ThemeMode itemMode))
        {
            menuItem.IsChecked = itemMode == mode;
        }

        foreach (MenuItem child in menuItem.Items.OfType<MenuItem>())
            Set(child, mode);
    }
}
