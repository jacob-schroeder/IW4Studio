using Avalonia.Controls;
using Avalonia.Data;
using IW4.Studio.Desktop.Themes;

namespace IW4.Studio.Desktop.Views;

/// <summary>
/// Defines the application commands once, then projects them into native and
/// in-window menu surfaces.
/// </summary>
internal static class StudioMenu
{
    private static readonly MenuEntry ApplicationEntry = new(
        "IW4 Studio",
        Items:
        [
            new("About IW4 Studio", StudioMenuAction.ShowAbout)
        ]);

    private static readonly MenuEntry[] Entries =
    [
        new(
            "File",
            Items:
            [
                new("Save As…", StudioMenuAction.SaveAs, "CanSaveAs"),
                new("Open another fastfile…", StudioMenuAction.OpenAnother),
                MenuEntry.Separator,
                new("Exit", StudioMenuAction.Exit)
            ]),
        new(
            "Edit",
            Items:
            [
                new("Copy", IsEnabled: false),
                new("Paste", IsEnabled: false)
            ]),
        new(
            "Tools",
            Items:
            [
                new("Live Preview", StudioMenuAction.LivePreview)
            ]),
        new(
            "Options",
            Items:
            [
                new(
                    "Theme",
                    Items:
                    [
                        new("Dark", StudioMenuAction.SelectDarkTheme, Theme: ThemeMode.Dark),
                        new("Light", StudioMenuAction.SelectLightTheme, Theme: ThemeMode.Light)
                    ])
            ])
    ];

    private static readonly MenuEntry[] WelcomeEntries =
    [
        Entries.Single(entry => entry.Header == "Options")
    ];

    public static NativeMenu CreateNativeMenu(
        Window owner,
        Action<StudioMenuAction> execute)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(execute);

        var menu = new NativeMenu();
        foreach (MenuEntry entry in Entries)
            menu.Items.Add(CreateNativeItem(entry, owner, execute));
        return menu;
    }

    public static NativeMenu CreateApplicationMenu(
        Action<StudioMenuAction> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);

        var menu = new NativeMenu();
        foreach (MenuEntry entry in ApplicationEntry.Items!)
            menu.Items.Add(CreateNativeItem(entry, owner: null, execute));
        return menu;
    }

    public static NativeMenu CreateWelcomeNativeMenu(
        Window owner,
        Action<StudioMenuAction> execute)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(execute);

        var menu = new NativeMenu();
        foreach (MenuEntry entry in WelcomeEntries)
            menu.Items.Add(CreateNativeItem(entry, owner, execute));
        return menu;
    }

    public static void PopulateWindowMenu(
        Menu menu,
        Window owner,
        Action<StudioMenuAction> execute)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(execute);

        menu.Items.Add(CreateWindowItem(ApplicationEntry, owner, execute));
        foreach (MenuEntry entry in Entries)
            menu.Items.Add(CreateWindowItem(entry, owner, execute));
    }

    public static void PopulateWelcomeWindowMenu(
        Menu menu,
        Window owner,
        Action<StudioMenuAction> execute)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(execute);

        menu.Items.Add(CreateWindowItem(ApplicationEntry, owner, execute));
        foreach (MenuEntry entry in WelcomeEntries)
            menu.Items.Add(CreateWindowItem(entry, owner, execute));
    }

    private static NativeMenuItemBase CreateNativeItem(
        MenuEntry entry,
        Window? owner,
        Action<StudioMenuAction> execute)
    {
        if (entry.IsSeparator)
            return new NativeMenuItemSeparator();

        var item = new NativeMenuItem
        {
            Header = entry.Header,
            IsEnabled = entry.IsEnabled,
            ToggleType = entry.Theme is null
                ? MenuItemToggleType.None
                : MenuItemToggleType.Radio,
            CommandParameter = entry.Theme?.ToString()
        };
        if (entry.IsEnabledBinding is not null)
            item.Bind(
                NativeMenuItem.IsEnabledProperty,
                CreateEnabledBinding(owner!, entry.IsEnabledBinding));
        if (entry.Action is { } action)
            item.Click += (_, _) => execute(action);
        if (entry.Items is { } children)
        {
            var childMenu = new NativeMenu();
            foreach (MenuEntry child in children)
                childMenu.Items.Add(CreateNativeItem(child, owner, execute));
            item.Menu = childMenu;
        }

        return item;
    }

    private static Control CreateWindowItem(
        MenuEntry entry,
        Window owner,
        Action<StudioMenuAction> execute)
    {
        if (entry.IsSeparator)
            return new Separator();

        var item = new MenuItem
        {
            Header = entry.Header,
            IsEnabled = entry.IsEnabled,
            ToggleType = entry.Theme is null
                ? MenuItemToggleType.None
                : MenuItemToggleType.Radio,
            CommandParameter = entry.Theme?.ToString()
        };
        if (entry.IsEnabledBinding is not null)
            item.Bind(
                MenuItem.IsEnabledProperty,
                CreateEnabledBinding(owner, entry.IsEnabledBinding));
        if (entry.Action is { } action)
            item.Click += (_, _) => execute(action);
        if (entry.Items is { } children)
        {
            foreach (MenuEntry child in children)
                item.Items.Add(CreateWindowItem(child, owner, execute));
        }

        return item;
    }

    private static Binding CreateEnabledBinding(Window owner, string propertyName) =>
        new($"DataContext.{propertyName}") { Source = owner };

    private sealed record MenuEntry(
        string? Header = null,
        StudioMenuAction? Action = null,
        string? IsEnabledBinding = null,
        ThemeMode? Theme = null,
        IReadOnlyList<MenuEntry>? Items = null,
        bool IsEnabled = true,
        bool IsSeparator = false)
    {
        public static MenuEntry Separator { get; } = new(IsSeparator: true);
    }
}

internal enum StudioMenuAction
{
    ShowAbout,
    SaveAs,
    OpenAnother,
    Exit,
    LivePreview,
    SelectDarkTheme,
    SelectLightTheme
}
