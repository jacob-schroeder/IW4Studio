namespace IW4.Render;

/// <summary>
/// Bounds host-frame elapsed time before it is applied to interactive camera
/// movement. Rendering or presentation stalls must not turn into a large
/// camera jump on the following update.
/// </summary>
internal static class MapRenderCameraUpdateTiming
{
    internal const float MaximumElapsedSeconds = 0.05f;

    public static float ClampElapsedSeconds(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0)
            return 0;

        return (float)Math.Min(elapsedSeconds, MaximumElapsedSeconds);
    }
}
