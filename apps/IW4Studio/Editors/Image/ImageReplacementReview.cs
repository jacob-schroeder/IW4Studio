using Avalonia.Media.Imaging;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.Image;

public sealed class ImageReplacementReview : IDisposable
{
    internal ImageReplacementReview(string fileName, ImageDraft candidate, ImageEditorInfo current,
        ImageEditorMip? before, ImageEditorMip after)
    {
        FileName = fileName;
        Mip = after;
        Candidate = candidate;
        Current = current;
        Replacement = new ImageEditorInfo(candidate.Image);
        BeforeLabel = before is null ? "Current · Preview unavailable" : $"Current · Mip {before.Level}";
        After = ImageEditorPixels.CreateThumbnail(after, 240);
        try
        {
            Before = before is null ? null : ImageEditorPixels.CreateThumbnail(before, 240);
        }
        catch
        {
            After.Dispose();
            throw;
        }
    }

    internal ImageDraft Candidate { get; }
    internal ImageEditorMip Mip { get; }
    public string FileName { get; }
    public ImageEditorInfo Current { get; }
    public ImageEditorInfo Replacement { get; }
    public string BeforeLabel { get; }
    public Bitmap? Before { get; }
    public Bitmap After { get; }

    public void Dispose()
    {
        Before?.Dispose();
        After.Dispose();
    }
}
