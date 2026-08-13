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
        SurfaceAntialias = target.SurfaceAntialias;
        Ps3SurfaceSampleCount = target.Ps3SurfaceSampleCount;
        ControlFlags = target.TargetSetAntiAliasingControlFlags;
        SampleMask = target.TargetSetSampleMask;
        MultisampleEnabled = target.TargetSetMultisampleEnabled;
        AlphaToCoverageEnabled = target.TargetSetAlphaToCoverageEnabled;
        AlphaToOneEnabled = target.TargetSetAlphaToOneEnabled;
        HostSampleCount = hostSampleCount;
        bool sampleTopologyMatches = SurfaceAntialias switch
        {
            RsxSurfaceAntialias.Center1 => hostSampleCount == 1,
            RsxSurfaceAntialias.DiagonalCentered2 => hostSampleCount == 2,
            _ => throw new InvalidOperationException(
                $"Unsupported target antialias value {SurfaceAntialias}.")
        };
        if (!sampleTopologyMatches)
        {
            throw new InvalidOperationException(
                $"Target {Target} requires {Ps3SurfaceSampleCount} host sample(s), but its resource has {hostSampleCount}.");
        }
    }

    public MapRenderNormalCameraTargetKind Target { get; }

    public RsxSurfaceAntialias SurfaceAntialias { get; }

    public byte RawSurfaceAntialias => (byte)SurfaceAntialias;

    public int Ps3SurfaceSampleCount { get; }

    public RsxAntiAliasingControlFlags ControlFlags { get; }

    public uint RawControl =>
        ((uint)SampleMask << 16) | (uint)ControlFlags;

    public bool MultisampleEnabled { get; }

    public bool AlphaToCoverageEnabled { get; }

    public bool AlphaToOneEnabled { get; }

    public ushort SampleMask { get; }

    public int HostSampleCount { get; }

    public uint HostSampleMaskWord => SampleMask;

    public uint HostSampleMaskWordIndex => 0;

    public bool AllocatesMultisampleStorage => HostSampleCount > 1;

}
