using System.Collections.ObjectModel;
using IW4.Render.Scheduling.Lifecycle;

namespace IW4.Render.OpenGl.Targets;

/// <summary>
/// Pure allocation plan for the two dedicated normal-camera depth
/// resources. Resolved targets 3 and 4 are deliberately absent.
/// </summary>
public sealed class MapRenderOpenGlNormalCameraDepthStencilFramePlan
{
    private readonly MapRenderOpenGlNormalCameraDepthStencilTargetKey[] _targets;

    internal MapRenderOpenGlNormalCameraDepthStencilFramePlan(
        MapRenderOpenGlNormalCameraColorFramePlan colorFramePlan,
        IReadOnlyList<MapRenderOpenGlNormalCameraDepthStencilTargetKey> targets)
    {
        ArgumentNullException.ThrowIfNull(colorFramePlan);
        ArgumentNullException.ThrowIfNull(targets);
        _targets = targets.ToArray();
        MapRenderNormalCameraTargetKind[] expected =
        [
            MapRenderNormalCameraTargetKind.Scene,
            MapRenderNormalCameraTargetKind.HalfParticles
        ];
        if (!_targets.Select(target => target.Target).SequenceEqual(expected))
        {
            throw new ArgumentException(
                "The depth/stencil frame must contain exact targets 2 and 6.",
                nameof(targets));
        }
        if (_targets.Any(target =>
                target.ColorTargetKey != colorFramePlan.GetBinding(target.Target)
                    .ResourceKey))
        {
            throw new ArgumentException(
                "Every depth/stencil target must borrow its exact color-frame key.",
                nameof(targets));
        }

        ColorFramePlan = colorFramePlan;
        Targets = Array.AsReadOnly(_targets);
    }

    public MapRenderOpenGlNormalCameraColorFramePlan ColorFramePlan { get; }

    public int DisplayWidth => ColorFramePlan.DisplayWidth;

    public int DisplayHeight => ColorFramePlan.DisplayHeight;

    public ReadOnlyCollection<MapRenderOpenGlNormalCameraDepthStencilTargetKey>
        Targets { get; }

    public MapRenderOpenGlNormalCameraDepthStencilTargetKey GetTarget(
        MapRenderNormalCameraTargetKind target)
    {
        if (!Enum.IsDefined(target))
            throw new ArgumentOutOfRangeException(nameof(target));
        return _targets.Single(candidate => candidate.Target == target);
    }

}

/// <summary>Builds the exact dedicated-depth subset without GL calls.</summary>
public static class MapRenderOpenGlNormalCameraDepthStencilFramePlanner
{
    public static MapRenderOpenGlNormalCameraDepthStencilFramePlan CreatePs3(
        MapRenderOpenGlNormalCameraColorFramePlan colorFramePlan)
    {
        ArgumentNullException.ThrowIfNull(colorFramePlan);
        MapRenderEditorPreviewNormalCameraRecipe recipe =
            MapRenderEditorPreviewNormalCameraRecipe.Current;
        MapRenderNormalCameraTargetKind[] targetOrder =
        [
            MapRenderNormalCameraTargetKind.Scene,
            MapRenderNormalCameraTargetKind.HalfParticles
        ];
        MapRenderOpenGlNormalCameraDepthStencilTargetKey[] targets = targetOrder
            .Select(kind => new MapRenderOpenGlNormalCameraDepthStencilTargetKey(
                recipe.GetTarget(kind),
                colorFramePlan.GetBinding(kind).ResourceKey))
            .ToArray();
        return new MapRenderOpenGlNormalCameraDepthStencilFramePlan(
            colorFramePlan,
            targets);
    }
}

public enum MapRenderOpenGlNormalCameraDepthStencilTargetPrewarmFailureKind
{
    ColorFrameMismatch = 0,
    ContextCapabilityUnavailable = 1,
    AllocationLimitExceeded = 2,
    ResourceAllocationFailed = 3
}

/// <summary>One dedicated depth/stencil resource prewarm failure.</summary>
public sealed record MapRenderOpenGlNormalCameraDepthStencilTargetPrewarmFailure(
    MapRenderOpenGlNormalCameraDepthStencilTargetPrewarmFailureKind Kind,
    MapRenderNormalCameraTargetKind Target,
    MapRenderOpenGlNormalCameraDepthStencilTargetKey ResourceKey,
    string Detail,
    string? ExceptionType = null);

/// <summary>Atomic publication: either both combined targets or failures.</summary>
public sealed class MapRenderOpenGlNormalCameraDepthStencilTargetPrewarmResult
{
    private readonly AllOrNothingOutcome<
        MapRenderOpenGlNormalCameraDepthStencilTargetResourceFrame,
        MapRenderOpenGlNormalCameraDepthStencilTargetPrewarmFailure> _outcome;

    internal MapRenderOpenGlNormalCameraDepthStencilTargetPrewarmResult(
        MapRenderOpenGlNormalCameraDepthStencilTargetResourceFrame? frame,
        IReadOnlyList<
            MapRenderOpenGlNormalCameraDepthStencilTargetPrewarmFailure>
            failures)
    {
        _outcome = new(
            frame,
            failures,
            "A depth/stencil prewarm result must contain either one complete frame or failures.");
    }

    public MapRenderOpenGlNormalCameraDepthStencilTargetResourceFrame? Frame =>
        _outcome.Value;

    public IReadOnlyList<
        MapRenderOpenGlNormalCameraDepthStencilTargetPrewarmFailure> Failures =>
        _outcome;

    public bool IsComplete => Frame is not null;
}

/// <summary>
/// Atomically maps the pure target-2/6 plan to combined resources. Capability,
/// color-frame identity, and dimension limits are checked before allocation.
/// </summary>
public static class MapRenderOpenGlNormalCameraDepthStencilTargetPrewarmer
{
    public static MapRenderOpenGlNormalCameraDepthStencilTargetPrewarmResult
        TryPrewarm(
            MapRenderOpenGlNormalCameraDepthStencilTargetResourceCache cache,
            MapRenderOpenGlNormalCameraDepthStencilFramePlan plan)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(plan);

        MapRenderSilkNormalCameraDepthStencilTargetCapabilities capabilities =
            cache.Capabilities;
        MapRenderOpenGlNormalCameraColorTargetResourceFrame colorFrame =
            cache.ColorFrame;
        var failures = new List<
            MapRenderOpenGlNormalCameraDepthStencilTargetPrewarmFailure>();
        foreach (MapRenderOpenGlNormalCameraDepthStencilTargetKey key in
                 plan.Targets)
        {
            MapRenderOpenGlNormalCameraColorTargetResourceBinding colorBinding =
                colorFrame.GetBinding(key.Target);
            if (colorBinding.Resource.Key != key.ColorTargetKey ||
                colorFrame.Plan.DisplayWidth != plan.DisplayWidth ||
                colorFrame.Plan.DisplayHeight != plan.DisplayHeight)
            {
                failures.Add(Failure(
                    MapRenderOpenGlNormalCameraDepthStencilTargetPrewarmFailureKind
                        .ColorFrameMismatch,
                    key,
                    "The dedicated depth plan does not match the exact borrowed color resource frame."));
                continue;
            }
            if (!capabilities.SupportsSampleCount(key.HostSampleCount))
            {
                failures.Add(Failure(
                    MapRenderOpenGlNormalCameraDepthStencilTargetPrewarmFailureKind
                        .ContextCapabilityUnavailable,
                    key,
                    $"The current context cannot allocate a D24S8 target with {key.HostSampleCount} sample(s) (GL_MAX_SAMPLES={capabilities.MaximumSamples}, GL_MAX_DEPTH_TEXTURE_SAMPLES={capabilities.MaximumDepthTextureSamples})."));
                continue;
            }
            if (key.HostStorageWidth > capabilities.MaximumTextureSize ||
                key.HostStorageHeight > capabilities.MaximumTextureSize)
            {
                failures.Add(Failure(
                    MapRenderOpenGlNormalCameraDepthStencilTargetPrewarmFailureKind
                        .AllocationLimitExceeded,
                    key,
                    $"Host storage {key.HostStorageWidth}x{key.HostStorageHeight} exceeds GL_MAX_TEXTURE_SIZE {capabilities.MaximumTextureSize}."));
            }
            if (!key.HostStorageFootprintMatchesPs3Backing)
            {
                failures.Add(Failure(
                    MapRenderOpenGlNormalCameraDepthStencilTargetPrewarmFailureKind
                        .AllocationLimitExceeded,
                    key,
                    "The host depth/stencil sample footprint does not match the PS3 backing allocation arithmetic."));
            }
            _ = key.HostStorageByteCount;
        }

        if (failures.Count != 0)
        {
            return new MapRenderOpenGlNormalCameraDepthStencilTargetPrewarmResult(
                null,
                failures);
        }

        var resources = new Dictionary<
            MapRenderOpenGlNormalCameraDepthStencilTargetKey,
            MapRenderOpenGlNormalCameraDepthStencilTargetResource>();
        foreach (MapRenderOpenGlNormalCameraDepthStencilTargetKey key in
                 plan.Targets)
        {
            try
            {
                resources.Add(key, cache.GetOrAllocate(key));
            }
            catch (Exception exception)
            {
                failures.Add(Failure(
                    MapRenderOpenGlNormalCameraDepthStencilTargetPrewarmFailureKind
                        .ResourceAllocationFailed,
                    key,
                    exception.Message,
                    exception.GetType().FullName));
            }
        }

        // Successfully allocated cache-owned pairs remain reusable, but no
        // partial frame mapping is published when either target failed.
        if (failures.Count != 0)
        {
            return new MapRenderOpenGlNormalCameraDepthStencilTargetPrewarmResult(
                null,
                failures);
        }

        MapRenderOpenGlNormalCameraDepthStencilTargetResourceBinding[]
            bindings = plan.Targets
                .Select(key =>
                    new MapRenderOpenGlNormalCameraDepthStencilTargetResourceBinding(
                        key,
                        resources[key]))
                .ToArray();
        return new MapRenderOpenGlNormalCameraDepthStencilTargetPrewarmResult(
            new MapRenderOpenGlNormalCameraDepthStencilTargetResourceFrame(
                cache.ContextIdentity,
                plan,
                colorFrame,
                bindings),
            []);
    }

    private static
        MapRenderOpenGlNormalCameraDepthStencilTargetPrewarmFailure Failure(
            MapRenderOpenGlNormalCameraDepthStencilTargetPrewarmFailureKind kind,
            MapRenderOpenGlNormalCameraDepthStencilTargetKey key,
            string detail,
            string? exceptionType = null) =>
        new(kind, key.Target, key, detail, exceptionType);
}
