using IW4.Assets.Assets.Image;

namespace IW4.Render.Shaders;

/// <summary>
/// Pure selected-pass sampler classifier. The material-image rules are bounded
/// to supported PS3 GfxImage tuples; Xbox MAPTYPE_1D/MAPTYPE_3D names are not
/// used for PS3 shape selection.
/// </summary>
internal static class MapRenderSelectedPassSamplerClassifier
{
    internal static MapRenderSelectedPassSamplerClassification ClassifyMaterialImage(
        GfxImageAsset? image)
    {
        if (image is null)
        {
            return new MapRenderSelectedPassSamplerClassification(
                MapRenderSelectedPassSamplerShape.Unknown,
                MapRenderSelectedPassSamplerResourceStatus.Unknown,
                "<unresolved-material-image>");
        }

        if (IsTwoDimensional(image))
        {
            return MaterialImage(
                image,
                MapRenderSelectedPassSamplerShape.TwoDimensional);
        }

        if (IsCube(image))
        {
            return MaterialImage(
                image,
                MapRenderSelectedPassSamplerShape.Cube);
        }

        return MaterialImage(
            image,
            MapRenderSelectedPassSamplerShape.Unknown);
    }

    private static bool IsTwoDimensional(GfxImageAsset image) =>
        image.MapType == 3 &&
        image.DimensionCount == 2 &&
        image.MultiFaceControl == 0 &&
        image.Depth == 1;

    private static bool IsCube(GfxImageAsset image) =>
        image.MapType == 5 &&
        image.DimensionCount == 2 &&
        image.MultiFaceControl != 0 &&
        image.Depth == 1 &&
        image.Width != 0 &&
        image.Width == image.Height;

    private static MapRenderSelectedPassSamplerClassification MaterialImage(
        GfxImageAsset image,
        MapRenderSelectedPassSamplerShape shape) =>
        new(
            shape,
            MapRenderSelectedPassSamplerResourceStatus
                .MaterialImageDescriptorResolved,
            image.Name ?? "<unnamed-material-image>");
}
