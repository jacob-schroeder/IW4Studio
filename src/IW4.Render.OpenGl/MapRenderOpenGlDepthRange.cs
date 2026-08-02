namespace IW4.Render.OpenGl;

/// <summary>Immutable OpenGL window-depth interval.</summary>
public readonly record struct MapRenderOpenGlDepthRange
{
    public MapRenderOpenGlDepthRange(double minimum, double maximum)
    {
        if (!double.IsFinite(minimum) ||
            !double.IsFinite(maximum) ||
            minimum < 0d ||
            maximum > 1d ||
            minimum > maximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimum),
                $"Invalid OpenGL depth range [{minimum:R},{maximum:R}].");
        }

        Minimum = minimum;
        Maximum = maximum;
    }

    public double Minimum { get; }

    public double Maximum { get; }
}
