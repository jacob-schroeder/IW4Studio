using Avalonia.Controls;
using Avalonia.Controls.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Iw4Radiant.Views;

public partial class MainWindow
{
    private readonly List<(MenuItem Source, NativeMenuItem Native)> _nativeMenuItems = [];

    private void InitializePlatformMenu()
    {
        if (!OperatingSystem.IsMacOS()) return;

        // The XAML menu remains the single owner of labels, actions and editor state.
        EditorMenu.IsVisible = false;
        NativeMenu menu = CreateNativeMenu(EditorMenu);
        RefreshNativeMenu();
        NativeMenu.SetMenu(this, menu);
        EditorMenu.PropertyChanged += (_, change) =>
        {
            if (change.Property == IsEnabledProperty) RefreshNativeMenu();
        };
        PropertyChanged += (_, change) =>
        {
            if (change.Property == IsEffectivelyEnabledProperty) RefreshNativeMenu();
        };
    }

    private NativeMenu CreateNativeMenu(ItemsControl source)
    {
        var menu = new NativeMenu();
        menu.NeedsUpdate += (_, _) => RefreshNativeMenu();
        foreach (object? child in source.Items)
        {
            if (child is Separator)
            {
                menu.Items.Add(new NativeMenuItemSeparator());
                continue;
            }
            if (child is not MenuItem item) continue;

            var native = new NativeMenuItem();
            _nativeMenuItems.Add((item, native));
            item.PropertyChanged += (_, _) => RefreshNativeMenuItem(item, native);
            if (item.Items.Count > 0) native.Menu = CreateNativeMenu(item);
            else native.Click += (_, _) =>
            {
                if (_dialogs.BlocksInput || !IsEffectivelyEnabled || !EditorMenu.IsEnabled ||
                    !item.IsEffectivelyEnabled || !item.IsVisible) return;

                // NativeMenuItem does not toggle itself before raising Click.
                if (item.ToggleType == MenuItemToggleType.CheckBox)
                    item.SetCurrentValue(MenuItem.IsCheckedProperty, !item.IsChecked);
                else if (item.ToggleType == MenuItemToggleType.Radio)
                    item.SetCurrentValue(MenuItem.IsCheckedProperty, true);
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            };
            menu.Items.Add(native);
        }
        return menu;
    }

    private void RefreshNativeMenu()
    {
        foreach (var (source, native) in _nativeMenuItems)
            RefreshNativeMenuItem(source, native);
    }

    private void RefreshNativeMenuItem(MenuItem source, NativeMenuItem native)
    {
        native.Header = (source.Header as string)?.Replace("_", "");
        native.ToolTip = ToolTip.GetTip(source) as string;
        native.IsEnabled = IsEffectivelyEnabled && EditorMenu.IsEnabled &&
            !_dialogs.BlocksInput && source.IsEffectivelyEnabled;
        native.IsVisible = source.IsVisible;
        native.ToggleType = source.ToggleType;
        native.IsChecked = source.IsChecked;
        // Keep context-sensitive keys in OnEditorKeyDown (text editing, walk and placement).
        // Native key equivalents can run before that handler; show their shared hints as tooltips.
        if (source.InputGesture is { } gesture)
        {
            var shortcut = new KeyGesture(gesture.Key, gesture.KeyModifiers.HasFlag(KeyModifiers.Control)
                ? (gesture.KeyModifiers & ~KeyModifiers.Control) | KeyModifiers.Meta
                : gesture.KeyModifiers);
            string hint = PlatformKeyGestureConverter.ToPlatformString(shortcut);
            native.ToolTip = string.IsNullOrEmpty(native.ToolTip) ? hint : $"{native.ToolTip}\n{hint}";
        }
    }
}
