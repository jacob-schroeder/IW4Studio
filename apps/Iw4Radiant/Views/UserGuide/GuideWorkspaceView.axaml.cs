using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Iw4Radiant.UserGuide;

namespace Iw4Radiant.Views.UserGuide;

public partial class GuideWorkspaceView : UserControl
{
    private GuideTour? _tour;
    private Bitmap? _image;
    private int _selected;
    internal event Action<string, string?>? NavigateRequested;
    internal event Action<int>? HotspotSelected;

    public GuideWorkspaceView() => InitializeComponent();

    internal void Show(GuideTour tour, int selected)
    {
        ReleaseImage();
        _tour = tour;
        using Stream stream = AssetLoader.Open(GuideLibrary.ResourceUri(tour.Image));
        _image = new Bitmap(stream);
        Diagram.Source = _image;
        AutomationProperties.SetName(Diagram, tour.Summary);
        TourTitle.Text = tour.Title;
        TourSummary.Text = tour.Summary;
        Hotspots.Children.Clear();
        for (int i = 0; i < tour.Hotspots.Count; i++)
        {
            int index = i;
            var button = new Button { Content = (i + 1).ToString(), Width = 34, Height = 34,
                Padding = new Thickness(0), CornerRadius = new CornerRadius(17),
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Background = Brush.Parse("#344962"), Foreground = Brush.Parse("#D7E6FA"),
                BorderBrush = Brush.Parse("#91A9CA"), BorderThickness = new Thickness(1) };
            AutomationProperties.SetName(button, $"{i + 1}. {tour.Hotspots[i].Title}");
            ToolTip.SetTip(button, tour.Hotspots[i].Title);
            button.Click += (_, _) => { Select(index); HotspotSelected?.Invoke(index); };
            Hotspots.Children.Add(button);
        }
        Select(Math.Clamp(selected, 0, tour.Hotspots.Count - 1));
        LayoutHotspots();
    }

    internal void Select(int index)
    {
        if (_tour is null || index < 0 || index >= _tour.Hotspots.Count) return;
        _selected = index;
        GuideHotspot area = _tour.Hotspots[index];
        AreaTitle.Text = $"{index + 1}. {area.Title}";
        AreaDescription.Text = area.Description;
        for (int i = 0; i < Hotspots.Children.Count; i++)
        {
            if (Hotspots.Children[i] is not Button button) continue;
            button.Background = Brush.Parse(i == index ? "#B4D3FF" : "#344962");
            button.Foreground = Brush.Parse(i == index ? "#202A37" : "#D7E6FA");
        }
    }

    internal void ReleaseImage()
    {
        Diagram.Source = null;
        _image?.Dispose();
        _image = null;
    }

    private void Surface_Changed(object? sender, SizeChangedEventArgs e) => LayoutHotspots();

    private void LayoutHotspots()
    {
        if (_tour is null || _image is null) return;
        double width = TourSurface.Bounds.Width;
        if (width <= 0) return;
        double height = width * _image.PixelSize.Height / _image.PixelSize.Width;
        TourSurface.Height = height;
        for (int i = 0; i < Hotspots.Children.Count; i++)
        {
            GuideHotspot area = _tour.Hotspots[i];
            Canvas.SetLeft(Hotspots.Children[i], Math.Clamp(area.X * width - 17, 0, Math.Max(0, width - 34)));
            Canvas.SetTop(Hotspots.Children[i], Math.Clamp(area.Y * height - 17, 0, Math.Max(0, height - 34)));
        }
    }

    private void ReadGuide_Click(object? sender, RoutedEventArgs e)
    {
        if (_tour is not null)
        {
            GuideHotspot area = _tour.Hotspots[_selected];
            NavigateRequested?.Invoke(area.Article, area.Section);
        }
    }
}
