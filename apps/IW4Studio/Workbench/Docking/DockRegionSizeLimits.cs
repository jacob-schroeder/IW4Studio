namespace IW4.Studio.Desktop.Workbench.Docking;

/// <summary>
/// Device-independent size limits for a dock region while it is open.
/// </summary>
public sealed class DockRegionSizeLimits
{
    public DockRegionSizeLimits(double minimum, double maximum, double initial)
    {
        if (!double.IsFinite(minimum) || minimum < 0)
            throw new ArgumentOutOfRangeException(nameof(minimum));

        if (!double.IsFinite(maximum) || maximum < minimum)
            throw new ArgumentOutOfRangeException(nameof(maximum));

        if (!double.IsFinite(initial) || initial < minimum || initial > maximum)
            throw new ArgumentOutOfRangeException(nameof(initial));

        Minimum = minimum;
        Maximum = maximum;
        Initial = initial;
    }

    public double Minimum { get; }

    public double Maximum { get; }

    public double Initial { get; }

    public double Clamp(double size)
    {
        if (!double.IsFinite(size))
            throw new ArgumentOutOfRangeException(nameof(size));

        return Math.Clamp(size, Minimum, Maximum);
    }
}
