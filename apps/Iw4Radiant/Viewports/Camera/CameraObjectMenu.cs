using Avalonia.Controls;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Viewports.Camera;

internal static class CameraObjectMenu
{
    internal static ContextMenu Open(Control viewport, EditorSession session,
        IReadOnlyList<(object Item, string Label)> hits, Action<BrushKind> classify)
    {
        MapDocument document = session.Document;
        var menu = new ContextMenu();
        var entries = new List<(object Item, MenuItem Menu)>();
        var kinds = new MenuItem { Header = "Brush type" };
        var selectAll = new MenuItem { Header = "Select all hit objects", StaysOpenOnClick = true };
        var deselectAll = new MenuItem { Header = "Deselect all hit objects", StaysOpenOnClick = true };
        foreach (var (item, label) in hits)
        {
            var entry = new MenuItem
            {
                Header = new TextBlock { Text = $"{entries.Count + 1}. {label}" },
                ToggleType = MenuItemToggleType.CheckBox,
                StaysOpenOnClick = true
            };
            entry.Click += (_, _) =>
            {
                if (IsCurrent()) session.Select(item, additive: true, toggle: true);
                Refresh();
            };
            entries.Add((item, entry));
            menu.Items.Add(entry);
        }
        if (hits.Count == 0)
            menu.Items.Add(new MenuItem { Header = "No selectable objects here", IsEnabled = false });
        menu.Items.Add(new Separator());
        selectAll.Click += (_, _) =>
        {
            if (IsCurrent()) session.SelectRange(session.Selection.Items.Concat(hits.Select(hit => hit.Item)));
            Refresh();
        };
        deselectAll.Click += (_, _) =>
        {
            if (IsCurrent())
            {
                var objects = new HashSet<object>(hits.Select(hit => hit.Item), ReferenceEqualityComparer.Instance);
                session.SelectRange(session.Selection.Items.Where(item => !objects.Contains(item)));
            }
            Refresh();
        };
        menu.Items.Add(selectAll);
        menu.Items.Add(deselectAll);
        menu.Items.Add(new Separator());
        foreach (var (label, kind) in new[]
                 {
                     ("Structural", BrushKind.Structural), ("Detail", BrushKind.Detail),
                     ("Non Collide", BrushKind.NonColliding), ("Weapon Clip", BrushKind.WeaponClip)
                 })
        {
            var entry = new MenuItem { Header = label };
            entry.Click += (_, _) => { if (IsCurrent()) classify(kind); };
            kinds.Items.Add(entry);
        }
        menu.Items.Add(kinds);
        Refresh();
        menu.Open(viewport);
        return menu;

        bool IsCurrent() => ReferenceEquals(document, session.Document);

        void Refresh()
        {
            foreach (var (item, entry) in entries)
            {
                entry.IsChecked = IsCurrent() && session.Selection.Contains(item);
                entry.IsEnabled = IsCurrent() && session.Visibility.CanSelect(document, item);
            }
            selectAll.IsEnabled = entries.Any(entry => entry.Menu.IsEnabled && !entry.Menu.IsChecked);
            deselectAll.IsEnabled = entries.Any(entry => entry.Menu.IsEnabled && entry.Menu.IsChecked);
            kinds.IsEnabled = IsCurrent() && session.Selection.Items.Select(EditorSelection.Owner).OfType<MapBrush>()
                .Any(brush => session.Visibility.CanSelect(document, brush));
        }
    }
}
