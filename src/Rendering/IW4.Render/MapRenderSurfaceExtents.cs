namespace IW4.Render;

/// <summary>
/// A positive pixel extent. The type prevents logical window dimensions and
/// physical framebuffer dimensions from being passed as unlabelled integer
/// pairs at renderer boundaries.
/// </summary>
public readonly record struct MapRenderPixelExtent
{
    public MapRenderPixelExtent(int width, int height)
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

    public long PixelCount => checked((long)Width * Height);

    public override string ToString() => $"{Width}x{Height}";
}

/// <summary>
/// Explicit EditorPreview render/presentation extent contract. SceneTarget is
/// the logical-resolution target-2/target-4 size; HostFramebuffer is the
/// physical default-framebuffer size. A one-extent caller uses
/// <see cref="Unified"/> and retains the historical behavior.
/// </summary>
public readonly record struct MapRenderSurfaceExtents
{
    public MapRenderSurfaceExtents(
        MapRenderPixelExtent sceneTarget,
        MapRenderPixelExtent hostFramebuffer)
    {
        if (sceneTarget.Width <= 0 || sceneTarget.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(sceneTarget));
        if (hostFramebuffer.Width <= 0 || hostFramebuffer.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(hostFramebuffer));

        SceneTarget = sceneTarget;
        HostFramebuffer = hostFramebuffer;
    }

    public MapRenderPixelExtent SceneTarget { get; }

    public MapRenderPixelExtent HostFramebuffer { get; }

    public bool IsValid =>
        SceneTarget.Width > 0 &&
        SceneTarget.Height > 0 &&
        HostFramebuffer.Width > 0 &&
        HostFramebuffer.Height > 0;

    public bool RequiresHostScale => SceneTarget != HostFramebuffer;

    public static MapRenderSurfaceExtents Unified(int width, int height)
    {
        var extent = new MapRenderPixelExtent(width, height);
        return new MapRenderSurfaceExtents(extent, extent);
    }
}

public enum MapRenderScreenshotSource
{
    /// <summary>
    /// The resolved single-sample target 4 at SceneTarget resolution. This is
    /// stable across host swaps but requires a completed EditorPreview frame.
    /// </summary>
    ResolvedScene,

    /// <summary>
    /// The physical default framebuffer's current back buffer at
    /// HostFramebuffer resolution. Callers own swap-order timing.
    /// </summary>
    HostBackBuffer
}
