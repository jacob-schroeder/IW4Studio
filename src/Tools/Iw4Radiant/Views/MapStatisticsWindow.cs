using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Views;

internal sealed class MapStatisticsWindow : Window
{
    internal MapStatisticsWindow(EditorSession session)
    {
        Title = "Map statistics";
        Width = 460;
        Height = 580;
        MinWidth = 380;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        MapDocument document = session.Document;
        var brushes = document.Brushes.ToArray();
        var terrains = document.Terrains.ToArray();
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = Path.GetFileName(session.FilePath ?? "Untitled.map"),
            FontWeight = Avalonia.Media.FontWeight.SemiBold, FontSize = 16
        });
        var totals = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowSpacing = 4 };
        void Row(string label, int value)
        {
            int row = totals.RowDefinitions.Count;
            totals.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var name = new TextBlock { Text = label };
            var count = new TextBlock { Text = value.ToString("N0"), HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetRow(name, row); Grid.SetRow(count, row); Grid.SetColumn(count, 1);
            totals.Children.Add(name); totals.Children.Add(count);
        }
        Row("Brushes", brushes.Length);
        foreach (BrushKind kind in Enum.GetValues<BrushKind>())
            Row("    " + (kind == BrushKind.NonColliding ? "Non-colliding" : kind == BrushKind.WeaponClip ? "Weapon clip" : kind.ToString()),
                brushes.Count(brush => BrushContents.Read(brush) == kind));
        Row("Brush faces", brushes.Sum(brush => brush.Faces.Count));
        Row("Terrain meshes", terrains.Count(terrain => !terrain.IsCurve));
        Row("Curves / patches", terrains.Count(terrain => terrain.IsCurve));
        Row("Entities (excluding worldspawn)", document.Entities.Count(entity => entity.ClassName != "worldspawn"));
        Row("Surface materials", brushes.SelectMany(brush => brush.Faces).Select(face => face.Material)
            .Concat(terrains.Select(terrain => terrain.Material)).Distinct(StringComparer.Ordinal).Count());
        Row("Selected items", session.Selection.Count);
        Row("Preserved primitives", document.Entities.Sum(entity => entity.PreservedPrimitives.Count));
        panel.Children.Add(totals);
        panel.Children.Add(new Separator());
        panel.Children.Add(new TextBlock { Text = "Entity classes", FontWeight = Avalonia.Media.FontWeight.SemiBold });
        foreach (var group in document.Entities.Where(entity => entity.ClassName != "worldspawn")
                     .GroupBy(entity => entity.ClassName).OrderBy(group => group.Key, StringComparer.Ordinal))
            panel.Children.Add(new TextBlock { Text = $"{group.Key}  ·  {group.Count():N0}" });
        panel.Children.Add(new TextBlock
        {
            Text = "Includes hidden objects. Prefabs are counted as instances.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap, Opacity = 0.65
        });
        var close = new Button { Content = "Close", MinWidth = 76, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        var layout = new Grid { Margin = new Thickness(20), RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 12 };
        layout.Children.Add(new ScrollViewer { Content = panel });
        Grid.SetRow(close, 1); layout.Children.Add(close);
        Content = layout;
        KeyDown += (_, e) => { if (e.Key is Avalonia.Input.Key.Escape or Avalonia.Input.Key.M) { e.Handled = true; Close(); } };
    }
}
