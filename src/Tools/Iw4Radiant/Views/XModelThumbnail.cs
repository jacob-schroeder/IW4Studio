using Avalonia.Media.Imaging;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Views;

internal sealed class XModelThumbnail(XModelSource model, Bitmap? preview, string? error)
{
    internal XModelSource Model { get; } = model;
    public string Name => Model.Name;
    public Bitmap? Preview { get; } = preview;
    public string Description => Error ?? Name;
    internal string? Error { get; } = error;
}
