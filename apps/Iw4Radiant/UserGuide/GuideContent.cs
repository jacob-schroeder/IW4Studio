namespace Iw4Radiant.UserGuide;

internal sealed class GuideCatalog
{
    public List<GuideTopic> Topics { get; set; } = [];
    public List<string> Paths { get; set; } = [];
    public List<string> Tours { get; set; } = [];
}

internal sealed class GuideTopic
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public List<string> Articles { get; set; } = [];
}

internal sealed class GuideArticle
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Outcome { get; set; } = "";
    public List<string> Keywords { get; set; } = [];
    public List<GuideSection> Sections { get; set; } = [];
    public List<string> Related { get; set; } = [];
}

internal sealed class GuideSection
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public List<string> Paragraphs { get; set; } = [];
    public List<GuideInstruction> Steps { get; set; } = [];
    public List<string> Tips { get; set; } = [];
    public string? Image { get; set; }
    public string? Caption { get; set; }
}

internal sealed class GuideInstruction
{
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public List<string> Keys { get; set; } = [];
}

internal sealed class GuidePath
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<GuideLesson> Steps { get; set; } = [];
}

internal sealed class GuideLesson
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Article { get; set; } = "";
    public string? Section { get; set; }
    public string Checkpoint { get; set; } = "";
}

internal sealed class GuideTour
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Image { get; set; } = "";
    public List<GuideHotspot> Hotspots { get; set; } = [];
}

internal sealed class GuideHotspot
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Article { get; set; } = "";
    public string? Section { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
}
