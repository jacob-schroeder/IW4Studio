using System.Text.Json;
using Avalonia.Platform;

namespace Iw4Radiant.UserGuide;

internal sealed class GuideLibrary
{
    private const string ResourceRoot = "avares://Iw4Radiant/UserGuide/Content/";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly Dictionary<string, GuideArticle> _articles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GuideTopic> _articleTopics = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _searchText = new(StringComparer.Ordinal);

    internal IReadOnlyList<GuideTopic> Topics { get; }
    internal IReadOnlyList<GuidePath> Paths { get; }
    internal IReadOnlyList<GuideTour> Tours { get; }
    internal IReadOnlyDictionary<string, GuideArticle> Articles => _articles;

    internal GuideLibrary()
    {
        GuideCatalog catalog = Read<GuideCatalog>("catalog.json");
        Topics = catalog.Topics;
        foreach (GuideTopic topic in Topics)
        {
            foreach (string resource in topic.Articles)
            {
                GuideArticle article = Read<GuideArticle>(resource);
                if (string.IsNullOrWhiteSpace(article.Id) || !_articles.TryAdd(article.Id, article))
                    throw new InvalidDataException($"The guide contains a missing or repeated article ID: {resource}.");
                _articleTopics.Add(article.Id, topic);
                _searchText.Add(article.Id, string.Join(" ", new[] { topic.Title, article.Title, article.Summary, article.Outcome }
                    .Concat(article.Keywords)
                    .Concat(article.Sections.SelectMany(section => new[] { section.Title }
                        .Concat(section.Paragraphs).Concat(section.Tips)
                        .Concat(section.Steps.Select(step => step.Title + " " + step.Text + " " + string.Join(" ", step.Keys)))))));
                if (article.Sections.Select(section => section.Id).Distinct(StringComparer.Ordinal).Count() != article.Sections.Count)
                    throw new InvalidDataException($"The guide contains repeated sections in {article.Id}.");
            }
        }
        Paths = catalog.Paths.Select(Read<GuidePath>).ToArray();
        Tours = catalog.Tours.Select(Read<GuideTour>).ToArray();
        if (_articles.Count == 0 || Paths.Count == 0 || Tours.Count == 0)
            throw new InvalidDataException("The bundled guide is incomplete.");
        foreach (GuideArticle article in _articles.Values)
        {
            foreach (string related in article.Related) RequireArticle(related, null);
            foreach (GuideSection section in article.Sections)
                if (section.Image is { Length: > 0 } image && !AssetLoader.Exists(ResourceUri(image)))
                    throw new InvalidDataException($"The guide image {image} is missing.");
        }
        foreach (GuidePath path in Paths)
        {
            if (path.Steps.Count == 0 || path.Steps.Select(step => step.Id).Distinct(StringComparer.Ordinal).Count() != path.Steps.Count)
                throw new InvalidDataException($"The learning path {path.Id} has missing or repeated steps.");
            foreach (GuideLesson step in path.Steps) RequireArticle(step.Article, step.Section);
        }
        foreach (GuideTour tour in Tours)
        {
            if (!AssetLoader.Exists(ResourceUri(tour.Image)) || tour.Hotspots.Count == 0)
                throw new InvalidDataException($"The workspace tour {tour.Id} is incomplete.");
            foreach (GuideHotspot hotspot in tour.Hotspots)
            {
                RequireArticle(hotspot.Article, hotspot.Section);
                if (!double.IsFinite(hotspot.X) || !double.IsFinite(hotspot.Y) || hotspot.X is < 0 or > 1 || hotspot.Y is < 0 or > 1)
                    throw new InvalidDataException($"The workspace hotspot {hotspot.Title} is outside its image.");
            }
        }
    }

    internal IEnumerable<GuideArticle> InTopic(GuideTopic topic) =>
        _articles.Values.Where(article => ReferenceEquals(_articleTopics[article.Id], topic));

    internal string TopicTitle(string articleId) => _articleTopics[articleId].Title;

    internal bool Matches(GuideArticle article, string query) => query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .All(word => _searchText[article.Id].Contains(word, StringComparison.OrdinalIgnoreCase));

    internal static string Display(string text) => text.Replace("{mod}", OperatingSystem.IsMacOS() ? "⌘" : "Ctrl", StringComparison.Ordinal);

    internal static Uri ResourceUri(string path) => new(ResourceRoot + path);

    private static T Read<T>(string path)
    {
        using Stream stream = AssetLoader.Open(ResourceUri(path));
        return JsonSerializer.Deserialize<T>(stream, JsonOptions)
            ?? throw new InvalidDataException($"The guide resource {path} is empty.");
    }

    private void RequireArticle(string id, string? section)
    {
        if (!_articles.TryGetValue(id, out GuideArticle? article) ||
            (section is { Length: > 0 } && !article.Sections.Any(item => item.Id == section)))
            throw new InvalidDataException($"The guide link {id}{(section is null ? "" : "#" + section)} has no destination.");
    }
}
