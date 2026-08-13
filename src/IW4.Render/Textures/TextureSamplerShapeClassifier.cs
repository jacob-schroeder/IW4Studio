using IW4.Assets.Assets.Image;

namespace IW4.Render.Textures;

internal static class TextureSamplerShapeClassifier
{
    internal static TextureSamplerShape ClassifyMaterialImage(GfxImageAsset? image)
    {
        if (image is null)
            return TextureSamplerShape.Unknown;
        if (image.MapType == 3 && image.DimensionCount == 2 &&
            image.MultiFaceControl == 0 && image.Depth == 1)
        {
            return TextureSamplerShape.TwoDimensional;
        }
        return image.MapType == 5 && image.DimensionCount == 2 &&
            image.MultiFaceControl != 0 && image.Depth == 1 &&
            image.Width != 0 && image.Width == image.Height
                ? TextureSamplerShape.Cube
                : TextureSamplerShape.Unknown;
    }
}
