using IW4.Assets.Assets.Image;

namespace IW4.Render.Textures;

internal static class TextureSamplerShapeClassifier
{
    internal static TextureSamplerShape ClassifyMaterialImage(GfxImageAsset? image)
    {
        if (image is null)
            return TextureSamplerShape.Unknown;
        if (image.MapType == MapType.TwoDimensional &&
            image.DimensionCount == GfxImageDimension.TwoDimensional &&
            !image.IsCubemap && image.Depth == 1)
        {
            return TextureSamplerShape.TwoDimensional;
        }
        return image.MapType == MapType.Cube &&
            image.DimensionCount == GfxImageDimension.TwoDimensional &&
            image.IsCubemap && image.Depth == 1 &&
            image.Width != 0 && image.Width == image.Height
                ? TextureSamplerShape.Cube
                : TextureSamplerShape.Unknown;
    }
}
