using Avalonia.Media.Imaging;

namespace Iw4Radiant.Views;

internal sealed class MaterialThumbnail(string name, string imagePath, Bitmap preview)
{
    public string Name { get; } = name;
    public Bitmap Preview { get; } = preview;
    internal string ImagePath { get; } = imagePath;
}
