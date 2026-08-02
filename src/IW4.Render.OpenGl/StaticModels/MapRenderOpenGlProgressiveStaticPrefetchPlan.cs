using IW4.Render;

namespace IW4.Render.OpenGl.StaticModels;

/// <summary>
/// CPU-only camera and resource-group scratch used to pre-admit static-model
/// resources before an interactive camera turn reaches them.
/// </summary>
internal sealed class MapRenderOpenGlProgressiveStaticPrefetchPlan
{
    private const int MaximumYawViewCount = 4096;
    private readonly MapRenderCamera[] _yawRing;

    private MapRenderOpenGlProgressiveStaticPrefetchPlan(
        MapRenderCamera initialCamera,
        float aspectRatio,
        double horizontalFieldOfViewRadians,
        MapRenderCamera[] yawRing)
    {
        InitialCamera = initialCamera;
        AspectRatio = aspectRatio;
        HorizontalFieldOfViewRadians = horizontalFieldOfViewRadians;
        _yawRing = yawRing;
    }

    /// <summary>
    /// The exact camera supplied by the caller. Renderer-owned visibility and
    /// selected-LOD scratch must be restored to this view after walking
    /// <see cref="YawRing"/>.
    /// </summary>
    public MapRenderCamera InitialCamera { get; }

    public float AspectRatio { get; }

    public double HorizontalFieldOfViewRadians { get; }

    /// <summary>
    /// Minimal evenly-spaced full-yaw ring for the supplied projection. The
    /// first entry is exactly <see cref="InitialCamera"/>; no closing duplicate
    /// at initial yaw + 2π is included.
    /// </summary>
    public ReadOnlySpan<MapRenderCamera> YawRing => _yawRing;

    public static MapRenderOpenGlProgressiveStaticPrefetchPlan CreateYawRing(
        MapRenderCamera initialCamera,
        float aspectRatio)
    {
        if (!(aspectRatio > 0f) || !float.IsFinite(aspectRatio))
            throw new ArgumentOutOfRangeException(nameof(aspectRatio));
        if (!(initialCamera.FieldOfViewRadians > 0f) ||
            initialCamera.FieldOfViewRadians >= MathF.PI ||
            !float.IsFinite(initialCamera.FieldOfViewRadians))
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialCamera),
                "The camera field of view must be finite and between zero and π.");
        }

        double verticalHalfFieldOfView =
            initialCamera.FieldOfViewRadians * 0.5d;
        double horizontalFieldOfView = 2d * Math.Atan(
            Math.Tan(verticalHalfFieldOfView) * aspectRatio);
        if (!(horizontalFieldOfView > 0d) ||
            !double.IsFinite(horizontalFieldOfView))
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialCamera),
                "The camera projection does not produce a finite horizontal field of view.");
        }

        double requiredViewCount =
            Math.Ceiling(Math.Tau / horizontalFieldOfView);
        if (requiredViewCount > MaximumYawViewCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialCamera),
                $"The camera projection requires more than {MaximumYawViewCount} yaw-prefetch views.");
        }

        int viewCount = Math.Max(1, checked((int)requiredViewCount));
        var yawRing = new MapRenderCamera[viewCount];
        double yawStep = Math.Tau / viewCount;
        for (int viewIndex = 0; viewIndex < yawRing.Length; viewIndex++)
        {
            yawRing[viewIndex] = viewIndex == 0
                ? initialCamera
                : initialCamera with
                {
                    YawRadians = initialCamera.YawRadians +
                        (float)(viewIndex * yawStep)
                };
        }

        return new MapRenderOpenGlProgressiveStaticPrefetchPlan(
            initialCamera,
            aspectRatio,
            horizontalFieldOfView,
            yawRing);
    }
}

/// <summary>
/// Allocation-free, deterministic union of resource-group selections gathered
/// from multiple prefetch cameras for one immutable resource plan.
/// </summary>
internal sealed class MapRenderOpenGlStaticResourceGroupUnion
{
    private readonly bool[] _included;
    private readonly int[] _groups;
    private int _groupCount;
    private bool _sorted = true;

    public MapRenderOpenGlStaticResourceGroupUnion(int groupCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(groupCount);
        _included = new bool[groupCount];
        _groups = new int[groupCount];
    }

    public int Capacity => _groups.Length;

    public int Count => _groupCount;

    /// <summary>
    /// Returns the selected resource groups in stable ascending plan order.
    /// The span remains valid until the next <see cref="Add"/> or
    /// <see cref="Reset"/>.
    /// </summary>
    public ReadOnlySpan<int> Groups
    {
        get
        {
            if (!_sorted)
            {
                Array.Sort(_groups, 0, _groupCount);
                _sorted = true;
            }
            return _groups.AsSpan(0, _groupCount);
        }
    }

    public void Add(ReadOnlySpan<int> selectedGroups)
    {
        // Validate the whole input before mutating the reusable union so a
        // malformed selection cannot leave a partially admitted plan.
        foreach (int groupIndex in selectedGroups)
        {
            if ((uint)groupIndex >= (uint)_included.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(selectedGroups),
                    "A selected resource group is outside the immutable plan.");
            }
        }

        foreach (int groupIndex in selectedGroups)
        {
            if (_included[groupIndex])
                continue;

            _included[groupIndex] = true;
            _groups[_groupCount++] = groupIndex;
            _sorted = _groupCount <= 1;
        }
    }

    public void Reset()
    {
        for (int groupOrdinal = 0;
             groupOrdinal < _groupCount;
             groupOrdinal++)
        {
            _included[_groups[groupOrdinal]] = false;
        }
        _groupCount = 0;
        _sorted = true;
    }
}
