using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Iw4Radiant.UserGuide;

namespace Iw4Radiant.Views.UserGuide;

public partial class GuideArticleView : UserControl
{
    private readonly List<Bitmap> _images = [];
    private readonly Dictionary<string, Control> _sections = new(StringComparer.Ordinal);
    internal event Action<string, string?>? NavigateRequested;
    internal double ScrollOffset => ArticleScroll.Offset.Y;
    private int _renderVersion;

    public GuideArticleView() => InitializeComponent();

    internal void Show(GuideLibrary library, GuideArticle article, string? sectionId = null, double offset = 0)
    {
        ReleaseImages();
        _sections.Clear();
        ArticleBody.Children.Clear();
        ArticleBody.Children.Add(Text(library.TopicTitle(article.Id), "guideMuted"));
        ArticleBody.Children.Add(Text(article.Title, "guideHeading"));
        ArticleBody.Children.Add(Paragraph(article.Summary));
        if (article.Outcome.Length > 0)
            ArticleBody.Children.Add(new Border { Classes = { "guideCallout" }, Child = Paragraph("Success looks like: " + article.Outcome) });
        foreach (GuideSection section in article.Sections)
        {
            var group = new StackPanel { Spacing = 10 };
            group.Children.Add(Text(section.Title, "guideSection"));
            _sections.Add(section.Id, group);
            foreach (string paragraph in section.Paragraphs) group.Children.Add(Paragraph(paragraph));
            if (section.Image is { Length: > 0 } imagePath)
            {
                using Stream stream = AssetLoader.Open(GuideLibrary.ResourceUri(imagePath));
                var bitmap = new Bitmap(stream);
                _images.Add(bitmap);
                var image = new Image { Source = bitmap, Stretch = Stretch.Uniform, MaxHeight = 360 };
                AutomationProperties.SetName(image, section.Caption ?? section.Title);
                group.Children.Add(image);
                if (section.Caption is { Length: > 0 }) group.Children.Add(Text(section.Caption, "guideMuted"));
            }
            for (int i = 0; i < section.Steps.Count; i++)
            {
                GuideInstruction step = section.Steps[i];
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("26,*"), ColumnSpacing = 10, Margin = new Thickness(0, 4) };
                row.Children.Add(new Border
                {
                    Width = 24, Height = 24, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
                    BorderBrush = Brush.Parse("#566A85"), VerticalAlignment = VerticalAlignment.Top,
                    Child = new TextBlock { Text = (i + 1).ToString(), Foreground = Brush.Parse("#B4D3FF"), FontSize = 11,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                });
                var body = new StackPanel { Spacing = 5 };
                Grid.SetColumn(body, 1);
                body.Children.Add(new TextBlock { Text = GuideLibrary.Display(step.Title), FontWeight = FontWeight.Medium, TextWrapping = TextWrapping.Wrap });
                body.Children.Add(Paragraph(step.Text));
                if (step.Keys.Count > 0)
                {
                    var keys = new WrapPanel();
                    foreach (string key in step.Keys)
                        keys.Children.Add(new Border { Classes = { "guideKeycap" }, Child = Text(GuideLibrary.Display(key)) });
                    body.Children.Add(keys);
                }
                row.Children.Add(body);
                group.Children.Add(row);
            }
            foreach (string tip in section.Tips)
                group.Children.Add(new Border { Classes = { "guideCallout" }, Child = Paragraph(tip) });
            ArticleBody.Children.Add(group);
        }
        if (article.Related.Count > 0)
        {
            ArticleBody.Children.Add(Text("Related guides", "guideSection"));
            foreach (string id in article.Related)
            {
                var button = new Button { Classes = { "guideNav" }, Content = Text(library.Articles[id].Title + "  →") };
                button.Click += (_, _) => NavigateRequested?.Invoke(id, null);
                ArticleBody.Children.Add(button);
            }
        }
        int version = ++_renderVersion;
        Dispatcher.UIThread.Post(() =>
        {
            if (version != _renderVersion) return;
            ArticleScroll.Offset = new Vector(0, offset);
            if (sectionId is not null && _sections.TryGetValue(sectionId, out Control? section)) section.BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    internal void ReleaseImages()
    {
        foreach (Bitmap bitmap in _images) bitmap.Dispose();
        _images.Clear();
    }

    private static TextBlock Text(string text, string? style = null)
    {
        var block = new TextBlock { Text = GuideLibrary.Display(text), TextWrapping = TextWrapping.Wrap };
        if (style is not null) block.Classes.Add(style);
        return block;
    }

    private static SelectableTextBlock Paragraph(string text) => new()
    {
        Text = GuideLibrary.Display(text), TextWrapping = TextWrapping.Wrap, LineHeight = 21
    };
}
