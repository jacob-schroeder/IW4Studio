namespace IW4.Render.Scheduling;

/// <summary>
/// Exact logical framebuffer/viewport extent used to derive one normal-camera
/// projection. Multisample backing dimensions do not belong in this value.
/// </summary>
public readonly record struct MapRenderNormalCameraFramebufferExtent
{
    public MapRenderNormalCameraFramebufferExtent(int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    public float AspectRatio => (float)Width / Height;
}
