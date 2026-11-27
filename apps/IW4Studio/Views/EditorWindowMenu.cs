using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AvaloniaEdit.Editing;
using CommunityToolkit.Mvvm.Input;
using IW4.Studio.Desktop.Themes;

namespace IW4.Studio.Desktop.Views;

/// <summary>
/// Defines the application commands once, then projects them into native and
/// in-window menu surfaces.
/// </summary>
internal static class StudioMenu
{
    private static readonly MenuEntry AboutEntry = new("About IW4 Studio", StudioMenuAction.ShowAbout);
    private static readonly MenuEntry ApplicationEntry = new("IW4 Studio", Items: [AboutEntry]);
    private static readonly MenuEntry MapConverterEntry = new("Map Converter", StudioMenuAction.ShowMapConverter);

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
                new("Copy", Clipboard: ApplicationClipboardAction.Copy),
                new("Paste", Clipboard: ApplicationClipboardAction.Paste)
            ]),
        new(
            "Tools",
            Items:
            [
                MapConverterEntry,
                new(
                    "Live Preview",
                    StudioMenuAction.LivePreview,
                    IsVisibleBinding: "LivePreview.CanLaunch"),
                new("Extract Zone", StudioMenuAction.ExtractZone),
                new(
                    "Dump Source Assets",
                    StudioMenuAction.DumpSourceAssets,
                    "CanDumpSourceAssets")
            ]),
        new(
            "Options",
            Items:
            [
                new(
                    "Theme",
                    Items:
                    [
                        new("Dark Green", StudioMenuAction.SelectDarkTheme, Theme: ThemeMode.Dark),
                        new("Light Green", StudioMenuAction.SelectLightTheme, Theme: ThemeMode.Light),
                        new("Dark Yellow", StudioMenuAction.SelectDarkYellowTheme, Theme: ThemeMode.DarkYellow),
                        new("Light Yellow", StudioMenuAction.SelectLightYellowTheme, Theme: ThemeMode.LightYellow)
                    ])
            ])
    ];

    private static readonly MenuEntry WelcomeToolsEntry = new("Tools", Items: [MapConverterEntry]);
    private static readonly MenuEntry WelcomeOptionsEntry = Entries.Single(entry => entry.Header == "Options");

    private static readonly MenuEntry[] WelcomeEntries =
    [
        WelcomeToolsEntry,
        WelcomeOptionsEntry
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

        var welcomeMenu = ApplicationEntry with { Items = [AboutEntry, MenuEntry.Separator, WelcomeOptionsEntry] };
        menu.Items.Add(CreateWindowItem(welcomeMenu, owner, execute));
        menu.Items.Add(CreateWindowItem(WelcomeToolsEntry, owner, execute));
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
            Command = (owner as EditorWindow)?.ClipboardCommands.For(entry.Clipboard),
            ToggleType = entry.Theme is null
                ? MenuItemToggleType.None
                : MenuItemToggleType.Radio,
            CommandParameter = entry.Theme?.ToString()
        };
        if (entry.IsEnabledBinding is not null)
            item.Bind(
                NativeMenuItem.IsEnabledProperty,
                CreateEnabledBinding(owner!, entry.IsEnabledBinding));
        if (entry.IsVisibleBinding is not null)
            item.Bind(
                NativeMenuItem.IsVisibleProperty,
                CreateBinding(owner!, entry.IsVisibleBinding));
        if (entry.Action is { } action)
            item.Click += (_, _) => execute(action);
        if (entry.Items is { } children)
        {
            var childMenu = new NativeMenu();
            foreach (MenuEntry child in children)
                childMenu.Items.Add(CreateNativeItem(child, owner, execute));
            if (owner is EditorWindow editor && children.Any(child => child.Clipboard is not null))
                childMenu.NeedsUpdate += (_, _) => editor.ClipboardCommands.Refresh();
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
            Command = (owner as EditorWindow)?.ClipboardCommands.For(entry.Clipboard),
            ToggleType = entry.Theme is null
                ? MenuItemToggleType.None
                : MenuItemToggleType.Radio,
            CommandParameter = entry.Theme?.ToString()
        };
        if (entry.IsEnabledBinding is not null)
            item.Bind(
                MenuItem.IsEnabledProperty,
                CreateEnabledBinding(owner, entry.IsEnabledBinding));
        if (entry.IsVisibleBinding is not null)
            item.Bind(
                MenuItem.IsVisibleProperty,
                CreateBinding(owner, entry.IsVisibleBinding));
        if (entry.Action is { } action)
            item.Click += (_, _) => execute(action);
        if (entry.Items is { } children)
        {
            foreach (MenuEntry child in children)
                item.Items.Add(CreateWindowItem(child, owner, execute));
            if (owner is EditorWindow editor && children.Any(child => child.Clipboard is not null))
                item.SubmenuOpened += (_, _) => editor.ClipboardCommands.Refresh();
        }

        return item;
    }

    private static Binding CreateEnabledBinding(Window owner, string propertyName) =>
        CreateBinding(owner, propertyName);

    private static Binding CreateBinding(Window owner, string propertyName) =>
        new($"DataContext.{propertyName}") { Source = owner };

    private sealed record MenuEntry(
        string? Header = null,
        StudioMenuAction? Action = null,
        string? IsEnabledBinding = null,
        string? IsVisibleBinding = null,
        ThemeMode? Theme = null,
        IReadOnlyList<MenuEntry>? Items = null,
        ApplicationClipboardAction? Clipboard = null,
        bool IsSeparator = false)
    {
        public static MenuEntry Separator { get; } = new(IsSeparator: true);
    }
}

internal enum ApplicationClipboardAction { Copy, Paste }

internal sealed class EditorClipboardCommands
{
    private readonly Window _owner;
    private readonly RelayCommand _copy;
    private readonly RelayCommand _paste;
    private Control? _target;

    public EditorClipboardCommands(Window owner)
    {
        _owner = owner;
        _copy = new RelayCommand(() => CopyOrPaste(true), () => CanCopyOrPaste(true));
        _paste = new RelayCommand(() => CopyOrPaste(false), () => CanCopyOrPaste(false));
        owner.AddHandler(InputElement.GotFocusEvent, (_, e) => RememberFocus(e.NewFocusedElement),
            RoutingStrategies.Bubble, handledEventsToo: true);
    }

    public RelayCommand? For(ApplicationClipboardAction? action) => action switch
    {
        ApplicationClipboardAction.Copy => _copy,
        ApplicationClipboardAction.Paste => _paste,
        _ => null
    };

    public void Refresh()
    {
        RememberFocus(_owner.FocusManager?.GetFocusedElement());
        _copy.NotifyCanExecuteChanged();
        _paste.NotifyCanExecuteChanged();
    }

    private void RememberFocus(IInputElement? element)
    {
        if (element is not Control control || control.FindAncestorOfType<MenuBase>(includeSelf: true) is not null ||
            control.FindAncestorOfType<MenuItem>(includeSelf: true) is not null)
            return;

        _target = control.FindAncestorOfType<TextBox>(includeSelf: true) as Control
            ?? control.FindAncestorOfType<TextArea>(includeSelf: true) as Control
            ?? control.FindAncestorOfType<SelectableTextBlock>(includeSelf: true);
    }

    private bool CanCopyOrPaste(bool copy) =>
        _target is { IsEffectivelyEnabled: true, IsEffectivelyVisible: true } target &&
        TopLevel.GetTopLevel(target) == _owner && (target switch
        {
            TextBox box => copy ? box.CanCopy : box.CanPaste,
            TextArea area => (copy ? AvaloniaEdit.ApplicationCommands.Copy : AvaloniaEdit.ApplicationCommands.Paste).CanExecute(null, area),
            SelectableTextBlock label => copy && label.CanCopy,
            _ => false
        });

    private void CopyOrPaste(bool copy)
    {
        if (!CanCopyOrPaste(copy))
            return;
        switch (_target)
        {
            case TextBox box:
                if (copy) box.Copy();
                else box.Paste();
                break;
            case TextArea area:
                (copy ? AvaloniaEdit.ApplicationCommands.Copy : AvaloniaEdit.ApplicationCommands.Paste).Execute(null, area);
                break;
            case SelectableTextBlock label:
                label.Copy();
                break;
        }
    }
}

internal enum StudioMenuAction
{
    ShowAbout,
    ShowMapConverter,
    SaveAs,
    ExtractZone,
    DumpSourceAssets,
    OpenAnother,
    Exit,
    LivePreview,
    SelectDarkTheme,
    SelectLightTheme,
    SelectDarkYellowTheme,
    SelectLightYellowTheme
}
