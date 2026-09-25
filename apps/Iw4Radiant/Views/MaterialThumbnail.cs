using Avalonia.Media.Imaging;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Views;

internal sealed class MaterialThumbnail(MaterialSource material, Bitmap? preview)
{
    public string Name => Material.Name;
    public bool IsSky => Material.IsSky;
    public bool HasNoPreview => Preview is null;
    public Bitmap? Preview { get; } = preview;
    internal MaterialSource Material { get; } = material;
}
