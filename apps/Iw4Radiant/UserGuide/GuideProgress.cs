namespace Iw4Radiant.UserGuide;

internal sealed class GuideProgress
{
    public string Mode { get; set; } = "learn";
    public string? Article { get; set; }
    public string? Section { get; set; }
    public string? Path { get; set; }
    public string? Lesson { get; set; }
    public string? Hotspot { get; set; }
    public double ArticleOffset { get; set; }
    public List<string> Bookmarks { get; set; } = [];
    public List<string> CompletedSteps { get; set; } = [];

    internal void Normalize()
    {
        Bookmarks ??= [];
        CompletedSteps ??= [];
        Bookmarks.RemoveAll(string.IsNullOrWhiteSpace);
        CompletedSteps.RemoveAll(string.IsNullOrWhiteSpace);
        if (!double.IsFinite(ArticleOffset) || ArticleOffset < 0) ArticleOffset = 0;
    }
}
