using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Iw4Radiant.Views;

public sealed class LightColorPicker : UserControl
{
    private readonly Slider _red = new() { Minimum = 0, Maximum = 1, Height = 22 };
    private readonly Slider _green = new() { Minimum = 0, Maximum = 1, Height = 22 };
    private readonly Slider _blue = new() { Minimum = 0, Maximum = 1, Height = 22 };
    private readonly Border _preview = new()
    {
        Width = 38, Height = 24, CornerRadius = new CornerRadius(3),
        BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1)
    };
    private readonly TextBlock _readout = new()
    {
        FontSize = 11, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 0, 0, 0)
    };
    private Vector3 _selectedColor;
    private bool _preserveColorScale;
    private bool _updating;

    public LightColorPicker()
    {
        var content = new StackPanel { Spacing = 4 };
        var previewRow = new Grid { ColumnDefinitions = new ColumnDefinitions("38,*") };
        previewRow.Children.Add(_preview);
        Grid.SetColumn(_readout, 1);
        previewRow.Children.Add(_readout);
        content.Children.Add(previewRow);
        AddChannel(content, "R", _red);
        AddChannel(content, "G", _green);
        AddChannel(content, "B", _blue);
        var swatches = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (Color color in new[] { Colors.White, Colors.Red, Colors.Lime, Colors.DodgerBlue,
                     Colors.Gold, Colors.Orange, Colors.Magenta, Colors.Black })
        {
            var button = new Button
            {
                Width = 24, Height = 22, MinWidth = 0, MinHeight = 0,
                Padding = new Thickness(0), Margin = new Thickness(0, 0, 4, 0),
                Background = new SolidColorBrush(color), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1)
            };
            ToolTip.SetTip(button, color.ToString());
            button.Click += (_, _) =>
            {
                Vector3 display = new Vector3(color.R, color.G, color.B) / 255;
                SelectedColor = _preserveColorScale ? display * display : display;
            };
            swatches.Children.Add(button);
        }
        content.Children.Add(swatches);
        Content = content;
        SelectedColor = Vector3.One;
    }

    internal Vector3 SelectedColor
    {
        get => _selectedColor;
        set
        {
            _selectedColor = value;
            float maximum = Math.Max(value.X, Math.Max(value.Y, value.Z));
            Vector3 display = DisplayColor(value, maximum);
            _updating = true;
            try
            {
                _red.Value = display.X;
                _green.Value = display.Y;
                _blue.Value = display.Z;
            }
            finally { _updating = false; }
            RefreshColor();
        }
    }

    internal bool PreserveColorScale
    {
        get => _preserveColorScale;
        set
        {
            if (_preserveColorScale == value) return;
            _preserveColorScale = value;
            SelectedColor = _selectedColor;
        }
    }

    private void AddChannel(StackPanel content, string label, Slider slider)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*") };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(slider, 1);
        row.Children.Add(slider);
        content.Children.Add(row);
        slider.PropertyChanged += (_, change) =>
        {
            if (_updating || change.Property != Slider.ValueProperty) return;
            Vector3 display = new((float)_red.Value, (float)_green.Value, (float)_blue.Value);
            _selectedColor = _preserveColorScale ? display * display : display;
            RefreshColor();
        };
    }

    private void RefreshColor()
    {
        float maximum = Math.Max(_selectedColor.X, Math.Max(_selectedColor.Y, _selectedColor.Z));
        Vector3 display = DisplayColor(_selectedColor, maximum);
        _preview.Background = new SolidColorBrush(Color.FromRgb(
            (byte)Math.Clamp(MathF.Round(display.X * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(display.Y * 255), 0, 255),
            (byte)Math.Clamp(MathF.Round(display.Z * 255), 0, 255)));
        _readout.Text = FormattableString.Invariant($"RGB: {_selectedColor.X:G6} / {_selectedColor.Y:G6} / {_selectedColor.Z:G6}");
    }

    private Vector3 DisplayColor(Vector3 value, float maximum) => _preserveColorScale
        ? new(MathF.Sqrt(Math.Clamp(value.X, 0, 1)), MathF.Sqrt(Math.Clamp(value.Y, 0, 1)),
            MathF.Sqrt(Math.Clamp(value.Z, 0, 1)))
        : maximum > 0 ? value / maximum : Vector3.Zero;
}
