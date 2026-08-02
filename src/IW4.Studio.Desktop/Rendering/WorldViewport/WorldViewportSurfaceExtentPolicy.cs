using IW4.Render;

namespace IW4.Studio.Desktop.Rendering.WorldViewport;

internal static class WorldViewportSurfaceExtentPolicy
{
    internal static MapRenderSurfaceExtents Measure(
        double logicalWidth,
        double logicalHeight,
        double renderScaling)
    {
        double width = FinitePositive(logicalWidth)
            ? logicalWidth
            : 1d;
        double height = FinitePositive(logicalHeight)
            ? logicalHeight
            : 1d;
        double scaling = FinitePositive(renderScaling)
            ? renderScaling
            : 1d;
        return new MapRenderSurfaceExtents(
            new MapRenderPixelExtent(
                Math.Max(1, checked((int)Math.Ceiling(width))),
                Math.Max(1, checked((int)Math.Ceiling(height)))),
            new MapRenderPixelExtent(
                Math.Max(
                    1,
                    checked((int)(width * scaling))),
                Math.Max(
                    1,
                    checked((int)(height * scaling)))));
    }

    private static bool FinitePositive(double value) =>
        double.IsFinite(value) && value > 0d;
}
