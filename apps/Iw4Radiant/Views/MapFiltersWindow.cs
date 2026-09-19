using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Iw4Radiant.Editing;

namespace Iw4Radiant.Views;

internal sealed class MapFiltersWindow : Window
{
    internal MapFiltersWindow(EditorSession session)
    {
        Title = "Filters";
        Width = 400;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Hide in 2D and camera views", FontWeight = Avalonia.Media.FontWeight.SemiBold });
        (EditorFilter Kind, string Label)[] kinds =
        [
            (EditorFilter.Structural, "Structural brushes"), (EditorFilter.Detail, "Detail brushes"),
            (EditorFilter.NonColliding, "Non-colliding brushes"), (EditorFilter.WeaponClip, "Weapon clip brushes"),
            (EditorFilter.PlayerClip, "Player clip brushes"), (EditorFilter.Terrain, "Terrain"),
            (EditorFilter.Curves, "Curves and patches"), (EditorFilter.Models, "Models"),
            (EditorFilter.Prefabs, "Prefabs"), (EditorFilter.Lights, "Lights"),
            (EditorFilter.BrushEntities, "Brush entities"), (EditorFilter.OtherEntities, "Other entities")
        ];
        var checks = new List<CheckBox>();
        foreach (var (kind, label) in kinds)
        {
            var check = new CheckBox { Content = label, IsChecked = (session.Visibility.ExcludedKinds & kind) != 0 };
            check.IsCheckedChanged += (_, _) =>
            {
                if (check.IsChecked == true) session.Visibility.ExcludedKinds |= kind;
                else session.Visibility.ExcludedKinds &= ~kind;
                session.Refresh();
            };
            checks.Add(check);
            panel.Children.Add(check);
        }
        var showAll = new Button { Content = "Show all" };
        showAll.Click += (_, _) => { foreach (var check in checks) check.IsChecked = false; };
        var close = new Button { Content = "Close", MinWidth = 76 };
        close.Click += (_, _) => Close();
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8, Children = { showAll, close }
        });
        Content = panel;
        KeyDown += (_, e) => { if (e.Key is Avalonia.Input.Key.Escape or Avalonia.Input.Key.F) { e.Handled = true; Close(); } };
    }
}
