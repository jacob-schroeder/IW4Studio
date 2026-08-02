using IW4.Render.Scheduling.Lifecycle;

namespace IW4.Render.OpenGl.Targets;

/// <summary>
/// Exact target-set anti-aliasing tuple and matching host sample count.
/// </summary>
public sealed record MapRenderOpenGlNormalCameraTargetAntialiasingPlan
{
    internal MapRenderOpenGlNormalCameraTargetAntialiasingPlan(
        MapRenderNormalCameraTargetPlan target,
        int hostSampleCount)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (hostSampleCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(hostSampleCount));
        }

        Target = target.Kind;
        RawSurfaceAntialias = target.RawAntialias;
        Ps3SurfaceSampleCount = target.Ps3SurfaceSampleCount;
        RawControl = target.RawTargetSetAntiAliasingControl;
        MultisampleEnabled = target.TargetSetMultisampleEnabled;
        AlphaToCoverageEnabled = target.TargetSetAlphaToCoverageEnabled;
        AlphaToOneEnabled = target.TargetSetAlphaToOneEnabled;
        SampleMask = target.TargetSetSampleMask;
        HostSampleCount = hostSampleCount;
        bool sampleTopologyMatches = RawSurfaceAntialias switch
        {
            0 => hostSampleCount == 1,
            3 => hostSampleCount == 2,
            _ => throw new InvalidOperationException(
                $"Unsupported target antialias value {RawSurfaceAntialias}.")
        };
        if (!sampleTopologyMatches)
        {
            throw new InvalidOperationException(
                $"Target {Target} requires {Ps3SurfaceSampleCount} host sample(s), but its resource has {hostSampleCount}.");
        }
    }

    public MapRenderNormalCameraTargetKind Target { get; }

    public byte RawSurfaceAntialias { get; }

    public int Ps3SurfaceSampleCount { get; }

    public uint RawControl { get; }

    public bool MultisampleEnabled { get; }

    public bool AlphaToCoverageEnabled { get; }

    public bool AlphaToOneEnabled { get; }

    public ushort SampleMask { get; }

    public int HostSampleCount { get; }

    public uint HostSampleMaskWord => SampleMask;

    public uint HostSampleMaskWordIndex => 0;

    public bool AllocatesMultisampleStorage => HostSampleCount > 1;

}
