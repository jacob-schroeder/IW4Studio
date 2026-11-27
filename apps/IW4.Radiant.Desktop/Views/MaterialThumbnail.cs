using Avalonia.Media.Imaging;
using IW4.Radiant.Desktop.Materials;

namespace IW4.Radiant.Desktop.Views;

internal sealed class MaterialThumbnail(MaterialSource material, Bitmap? preview)
{
    public string Name => Material.Name;
    public bool IsSky => Material.IsSky;
    public bool HasNoPreview => Preview is null;
    public Bitmap? Preview { get; } = preview;
    internal MaterialSource Material { get; } = material;
}
