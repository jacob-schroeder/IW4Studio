using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Iw4Radiant.Views;

public partial class ControlsWindow : Window
{
    public ControlsWindow()
    {
        InitializeComponent();
        string modifier = OperatingSystem.IsMacOS() ? "⌘" : "Ctrl";
        PlatformLabel.Text = OperatingSystem.IsMacOS() ? "macOS" : OperatingSystem.IsWindows() ? "Windows" : "Linux";

        EditorContent.Content = ModeContent(
            "Tools and gestures for editing your map.",
            [
                ("Save map", [modifier, "S"]),
                ("Undo", [modifier, "Z"]),
                ("Redo", [modifier, "Y"]),
                ("Duplicate", ["Space"]),
                ("Delete", ["Delete"]),
                ("Frame selection", ["End"])
            ],
            [
                ("Select / deselect", ["Shift", "Click"]),
                ("Select across objects", ["Shift", "Drag"]),
                ("Draw brush / move", ["Left drag"]),
                ("Orbit camera", ["Right drag"]),
                ("Pan camera", ["Middle drag"]),
                ("Zoom", ["Scroll"])
            ],
            "With nothing selected, left-drag in a grid draws a brush. Otherwise, it moves the selection.",
            ToolReference());

        FlyContent.Content = ModeContent(
            "Enable Fly in the camera header, then focus the camera.",
            [
                ("Forward / backward", ["W / S"]),
                ("Strafe left / right", ["A / D"]),
                ("Down / up", ["Q / E"]),
                ("Move faster", ["Shift"]),
                ("Return to Editor", ["Esc"])
            ],
            [
                ("Look around", ["Right drag"]),
                ("Forward / backward", ["Scroll"]),
                ("Focus the camera", ["Click"])
            ],
            "Movement keys act on the camera while it is focused.");

        WalkContent.Content = ModeContent(
            "Enable Walk in the camera header, then focus the camera.",
            [
                ("Forward / backward", ["W / S"]),
                ("Left / right", ["A / D"]),
                ("Jump", ["Space"]),
                ("Reset to entry", ["R"]),
                ("Return to Editor", ["Esc"])
            ],
            [
                ("Look around", ["Right drag"]),
                ("Resume after focus loss", ["Click"])
            ],
            "Losing focus pauses movement. Click the camera to resume.");
    }

    private static StackPanel ModeContent(string context,
        (string Label, string[] Keys)[] keyboard, (string Label, string[] Keys)[] mouse,
        string note, Control? tools = null)
    {
        var content = new StackPanel { Margin = new Thickness(24, 20, 24, 22) };
        content.Children.Add(new TextBlock
        {
            Text = context, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 21)
        });
        if (tools is not null) content.Children.Add(tools);

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 27 };
        columns.Children.Add(ControlReference("Keyboard", keyboard));
        StackPanel mouseReference = ControlReference("Mouse", mouse);
        Grid.SetColumn(mouseReference, 1);
        columns.Children.Add(mouseReference);
        content.Children.Add(columns);
        content.Children.Add(new Border
        {
            BorderBrush = Brush.Parse("#B4D3FF"), BorderThickness = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(10, 0, 0, 0), Margin = new Thickness(0, 20, 0, 0),
            Child = new TextBlock { Text = note, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap }
        });
        return content;
    }

    private static StackPanel ToolReference()
    {
        (string Key, string Label)[] tools =
        [
            ("Q", "Create brush"), ("S", "Select faces"), ("E", "Edit vertices"),
            ("X", "Clip brushes"), ("T", "Create terrain"), ("V", "Shape terrain")
        ];
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = 19, RowSpacing = 11
        };
        for (int i = 0; i < tools.Length; i++)
        {
            var tool = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children =
                {
                    Keycap(tools[i].Key),
                    new TextBlock { Text = tools[i].Label, VerticalAlignment = VerticalAlignment.Center }
                }
            };
            Grid.SetRow(tool, i / 3);
            Grid.SetColumn(tool, i % 3);
            grid.Children.Add(tool);
        }
        return new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 25),
            Children = { new TextBlock { Text = "Tools", Classes = { "sectionTitle" } }, grid }
        };
    }

    private static StackPanel ControlReference(string title, (string Label, string[] Keys)[] controls)
    {
        var reference = new StackPanel();
        reference.Children.Add(new TextBlock { Text = title, Classes = { "sectionTitle" } });
        for (int i = 0; i < controls.Length; i++)
        {
            var keys = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
            foreach (string key in controls[i].Keys)
            {
                if (keys.Children.Count > 0)
                    keys.Children.Add(new TextBlock
                    {
                        Text = "+", FontSize = 11, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center
                    });
                keys.Children.Add(Keycap(key));
            }
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
            row.Children.Add(new TextBlock
            {
                Text = controls[i].Label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(keys, 1);
            row.Children.Add(keys);
            if (controls[i].Label == "Pan camera") ToolTip.SetTip(row, "You can also pan with Shift + right-drag.");
            reference.Children.Add(new Border
            {
                Padding = new Thickness(0, 11), BorderBrush = Brush.Parse("#3D444E"),
                BorderThickness = new Thickness(0, 0, 0, i < controls.Length - 1 ? 1 : 0), Child = row
            });
        }
        return reference;
    }

    private static Border Keycap(string text) => new()
    {
        Classes = { "keycap" }, Child = new TextBlock { Text = text }
    };

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }
}
