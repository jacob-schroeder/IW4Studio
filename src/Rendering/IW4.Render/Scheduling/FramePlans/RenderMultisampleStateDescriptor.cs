namespace IW4.Render.Scheduling.FramePlans;

/// <summary>
/// Backend-neutral multisample intent. The mask is the authored/API-neutral
/// sample-coverage word, not an OpenGL or Vulkan object.
/// </summary>
public readonly record struct RenderMultisampleStateDescriptor
{
    public RenderMultisampleStateDescriptor(
        int sampleCount,
        uint sampleMask,
        bool alphaToCoverageEnabled,
        bool alphaToOneEnabled)
    {
        if (sampleCount is <= 0 or > 32)
            throw new ArgumentOutOfRangeException(nameof(sampleCount));
        if (sampleMask == 0)
            throw new ArgumentOutOfRangeException(nameof(sampleMask));

        SampleCount = sampleCount;
        SampleMask = sampleMask;
        AlphaToCoverageEnabled = alphaToCoverageEnabled;
        AlphaToOneEnabled = alphaToOneEnabled;
    }

    public int SampleCount { get; }

    public uint SampleMask { get; }

    public bool AlphaToCoverageEnabled { get; }

    public bool AlphaToOneEnabled { get; }

    public static RenderMultisampleStateDescriptor Ps3Target2 { get; } =
        new(
            sampleCount: 2,
            sampleMask: 0x0000ffff,
            alphaToCoverageEnabled: false,
            alphaToOneEnabled: false);
}
