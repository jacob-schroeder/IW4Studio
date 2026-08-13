using IW4.Render.Scheduling.Lifecycle;
using System.Collections.ObjectModel;

namespace IW4.Render.OpenGl.Targets;

/// <summary>One target id mapped to its canonical color allocation key.</summary>
public sealed record MapRenderOpenGlNormalCameraColorTargetBindingPlan
{
    internal MapRenderOpenGlNormalCameraColorTargetBindingPlan(
        MapRenderNormalCameraTargetPlan target,
        MapRenderNormalCameraTargetPlan canonicalTarget,
        MapRenderOpenGlNormalCameraColorTargetKey resourceKey,
        int displayWidth,
        int displayHeight)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(canonicalTarget);
        ArgumentNullException.ThrowIfNull(resourceKey);
        if (!resourceKey.MatchesCanonicalTarget(
                canonicalTarget,
                displayWidth,
                displayHeight))
        {
            throw new ArgumentException(
                "The color resource key does not match its canonical target row.",
                nameof(resourceKey));
        }

        bool isCanonical = target.Kind == canonicalTarget.Kind;
        if (isCanonical != (target.InitialAliasOf is null) ||
            (!isCanonical && target.InitialAliasOf != canonicalTarget.Kind))
        {
            throw new ArgumentException(
                "The target row does not name the supplied canonical initial alias.",
                nameof(canonicalTarget));
        }

        MapRenderNormalCameraTargetExtent targetExtent = target.ResolveExtent(
            displayWidth,
            displayHeight);
        if (targetExtent != resourceKey.Extent ||
            target.RawProgramImageSlot != resourceKey.RawProgramImageSlot ||
            target.RawDimensionFamily != resourceKey.RawDimensionFamily ||
            target.RawDimensionShift != resourceKey.RawDimensionShift ||
            target.RawImageSetupFormat != resourceKey.RawImageSetupFormat ||
            target.ImageSetupFlags != resourceKey.ImageSetupFlags ||
            target.ImageFormat != resourceKey.ImageFormat ||
            target.SurfaceType != resourceKey.SurfaceType ||
            target.SurfaceAntialias != resourceKey.SurfaceAntialias ||
            target.Ps3SurfaceTarget != resourceKey.Ps3SurfaceTarget ||
            target.SurfaceColorFormat != resourceKey.SurfaceColorFormat)
        {
            throw new ArgumentException(
                "An aliased target row must match every color allocation fact of its canonical row.",
                nameof(target));
        }

        Target = target.Kind;
        CanonicalTarget = canonicalTarget.Kind;
        Ps3Name = target.Ps3Name;
        Ps3RowAddress = target.Ps3RowAddress;
        Extent = targetExtent;
        ResourceKey = resourceKey;
    }

    public MapRenderNormalCameraTargetKind Target { get; }

    public MapRenderNormalCameraTargetKind CanonicalTarget { get; }

    public bool IsAlias => Target != CanonicalTarget;

    public string Ps3Name { get; }

    public uint Ps3RowAddress { get; }

    public MapRenderNormalCameraTargetExtent Extent { get; }

    public MapRenderOpenGlNormalCameraColorTargetKey ResourceKey { get; }
}

/// <summary>
/// Pure color-only allocation plan for one positive display size. It has no
/// depth/stencil, clear, resolve, draw, or draw-target binding authority.
/// </summary>
public sealed class MapRenderOpenGlNormalCameraColorFramePlan
{
    private readonly MapRenderOpenGlNormalCameraColorTargetBindingPlan[] _bindings;
    private readonly MapRenderOpenGlNormalCameraColorTargetKey[] _uniqueResourceKeys;

    internal MapRenderOpenGlNormalCameraColorFramePlan(
        int displayWidth,
        int displayHeight,
        IReadOnlyList<MapRenderOpenGlNormalCameraColorTargetBindingPlan> bindings)
    {
        if (displayWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(displayWidth));
        if (displayHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(displayHeight));
        ArgumentNullException.ThrowIfNull(bindings);

        _bindings = bindings.ToArray();
        MapRenderNormalCameraTargetKind[] expectedOrder =
        [
            MapRenderNormalCameraTargetKind.Scene,
            MapRenderNormalCameraTargetKind.ResolvedPostSun,
            MapRenderNormalCameraTargetKind.ResolvedScene,
            MapRenderNormalCameraTargetKind.HalfParticles
        ];
        if (!_bindings.Select(binding => binding.Target).SequenceEqual(expectedOrder))
        {
            throw new ArgumentException(
                "The color frame must contain exact normal-camera target order 2, 3, 4, 6.",
                nameof(bindings));
        }
        if (_bindings.Any(binding =>
                binding.ResourceKey.DisplayWidth != displayWidth ||
                binding.ResourceKey.DisplayHeight != displayHeight))
        {
            throw new ArgumentException(
                "Every color binding must be keyed by the frame display size.",
                nameof(bindings));
        }

        MapRenderOpenGlNormalCameraColorTargetBindingPlan target3 = _bindings[1];
        MapRenderOpenGlNormalCameraColorTargetBindingPlan target4 = _bindings[2];
        if (!target3.IsAlias ||
            target3.CanonicalTarget != MapRenderNormalCameraTargetKind.ResolvedScene ||
            target4.IsAlias ||
            !ReferenceEquals(target3.ResourceKey, target4.ResourceKey))
        {
            throw new ArgumentException(
                "Targets 3 and 4 must share the one canonical target-4 color key.",
                nameof(bindings));
        }

        _uniqueResourceKeys = _bindings
            .Select(binding => binding.ResourceKey)
            .Distinct()
            .ToArray();
        if (_uniqueResourceKeys.Length != 3)
        {
            throw new ArgumentException(
                "The normal-camera color frame must contain exactly three canonical resources.",
                nameof(bindings));
        }

        DisplayWidth = displayWidth;
        DisplayHeight = displayHeight;
        Bindings = Array.AsReadOnly(_bindings);
        UniqueResourceKeys = Array.AsReadOnly(_uniqueResourceKeys);
    }

    public int DisplayWidth { get; }

    public int DisplayHeight { get; }

    public ReadOnlyCollection<MapRenderOpenGlNormalCameraColorTargetBindingPlan> Bindings { get; }

    public ReadOnlyCollection<MapRenderOpenGlNormalCameraColorTargetKey> UniqueResourceKeys { get; }

    public MapRenderOpenGlNormalCameraColorTargetBindingPlan GetBinding(
        MapRenderNormalCameraTargetKind target)
    {
        if (!Enum.IsDefined(target))
            throw new ArgumentOutOfRangeException(nameof(target));
        return _bindings.Single(binding => binding.Target == target);
    }

}

/// <summary>Builds the PS3 color alias graph without GL calls.</summary>
public static class MapRenderOpenGlNormalCameraColorFramePlanner
{
    public static MapRenderOpenGlNormalCameraColorFramePlan CreatePs3(
        int displayWidth,
        int displayHeight)
    {
        MapRenderEditorPreviewNormalCameraRecipe recipe =
            MapRenderEditorPreviewNormalCameraRecipe.Current;
        if (displayWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(displayWidth));
        if (displayHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(displayHeight));

        // Resolve every extent before constructing a key so overflow or an
        // invalid display fails before a partial frame plan can be published.
        foreach (MapRenderNormalCameraTargetPlan target in recipe.Targets)
            _ = target.ResolveExtent(displayWidth, displayHeight);

        var keys = new Dictionary<
            MapRenderNormalCameraTargetKind,
            MapRenderOpenGlNormalCameraColorTargetKey>();
        foreach (MapRenderNormalCameraTargetPlan target in recipe.Targets
                     .Where(target => target.InitialAliasOf is null))
        {
            keys.Add(
                target.Kind,
                new MapRenderOpenGlNormalCameraColorTargetKey(
                    target,
                    displayWidth,
                    displayHeight));
        }

        MapRenderOpenGlNormalCameraColorTargetBindingPlan[] bindings = recipe
            .Targets
            .Select(target =>
            {
                MapRenderNormalCameraTargetKind canonicalKind =
                    target.InitialAliasOf ?? target.Kind;
                MapRenderNormalCameraTargetPlan canonical = recipe.GetTarget(
                    canonicalKind);
                return new MapRenderOpenGlNormalCameraColorTargetBindingPlan(
                    target,
                    canonical,
                    keys[canonicalKind],
                    displayWidth,
                    displayHeight);
            })
            .ToArray();

        return new MapRenderOpenGlNormalCameraColorFramePlan(
            displayWidth,
            displayHeight,
            bindings);
    }
}

public enum MapRenderOpenGlNormalCameraColorTargetPrewarmFailureKind
{
    ContextCapabilityUnavailable = 0,
    AllocationLimitExceeded = 1,
    ResourceAllocationFailed = 2
}

/// <summary>One canonical color-resource prewarm failure.</summary>
public sealed record MapRenderOpenGlNormalCameraColorTargetPrewarmFailure(
    MapRenderOpenGlNormalCameraColorTargetPrewarmFailureKind Kind,
    MapRenderNormalCameraTargetKind CanonicalTarget,
    MapRenderOpenGlNormalCameraColorTargetKey ResourceKey,
    string Detail,
    string? ExceptionType = null);

/// <summary>Atomic result: either all four target mappings or failures.</summary>
public sealed class MapRenderOpenGlNormalCameraColorTargetPrewarmResult
{
    private readonly AllOrNothingOutcome<
        MapRenderOpenGlNormalCameraColorTargetResourceFrame,
        MapRenderOpenGlNormalCameraColorTargetPrewarmFailure> _outcome;

    internal MapRenderOpenGlNormalCameraColorTargetPrewarmResult(
        MapRenderOpenGlNormalCameraColorTargetResourceFrame? frame,
        IReadOnlyList<MapRenderOpenGlNormalCameraColorTargetPrewarmFailure> failures)
    {
        _outcome = new(
            frame,
            failures,
            "A color-target prewarm result must contain either one complete frame or failures.");
    }

    public MapRenderOpenGlNormalCameraColorTargetResourceFrame? Frame =>
        _outcome.Value;

    public IReadOnlyList<MapRenderOpenGlNormalCameraColorTargetPrewarmFailure>
        Failures => _outcome;

    public bool IsComplete => Frame is not null;
}

/// <summary>
/// Atomically maps a pure frame plan to context-owned color resources. All
/// capability and dimension limits are checked before the first allocation.
/// </summary>
public static class MapRenderOpenGlNormalCameraColorTargetPrewarmer
{
    public static MapRenderOpenGlNormalCameraColorTargetPrewarmResult TryPrewarm(
        MapRenderOpenGlNormalCameraColorTargetResourceCache cache,
        MapRenderOpenGlNormalCameraColorFramePlan plan)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(plan);

        MapRenderSilkNormalCameraColorTargetCapabilities capabilities =
            cache.Capabilities;
        var failures = new List<
            MapRenderOpenGlNormalCameraColorTargetPrewarmFailure>();
        foreach (MapRenderOpenGlNormalCameraColorTargetKey key in
                 plan.UniqueResourceKeys)
        {
            if (!capabilities.SupportsSampleCount(key.HostSampleCount))
            {
                failures.Add(Failure(
                    MapRenderOpenGlNormalCameraColorTargetPrewarmFailureKind
                        .ContextCapabilityUnavailable,
                    key,
                    $"The current context cannot allocate an RGBA8 target with {key.HostSampleCount} sample(s) (GL_MAX_SAMPLES={capabilities.MaximumSamples}, GL_MAX_COLOR_TEXTURE_SAMPLES={capabilities.MaximumColorTextureSamples})."));
                continue;
            }
            if (key.HostStorageWidth > capabilities.MaximumTextureSize ||
                key.HostStorageHeight > capabilities.MaximumTextureSize)
            {
                failures.Add(Failure(
                    MapRenderOpenGlNormalCameraColorTargetPrewarmFailureKind
                        .AllocationLimitExceeded,
                    key,
                    $"Host storage {key.HostStorageWidth}x{key.HostStorageHeight} exceeds GL_MAX_TEXTURE_SIZE {capabilities.MaximumTextureSize}."));
            }
            if (!key.HostStorageFootprintMatchesPs3Backing)
            {
                failures.Add(Failure(
                    MapRenderOpenGlNormalCameraColorTargetPrewarmFailureKind
                        .AllocationLimitExceeded,
                    key,
                    "The host color sample footprint does not match the PS3 backing allocation arithmetic."));
            }
            _ = key.HostStorageByteCount;
        }

        // Validation is frame-atomic and occurs before the first cache call.
        if (failures.Count != 0)
        {
            return new MapRenderOpenGlNormalCameraColorTargetPrewarmResult(
                null,
                failures);
        }

        var resources = new Dictionary<
            MapRenderOpenGlNormalCameraColorTargetKey,
            MapRenderOpenGlNormalCameraColorTargetResource>();
        foreach (MapRenderOpenGlNormalCameraColorTargetKey key in
                 plan.UniqueResourceKeys)
        {
            try
            {
                resources.Add(key, cache.GetOrAllocate(key));
            }
            catch (Exception exception)
            {
                failures.Add(Failure(
                    MapRenderOpenGlNormalCameraColorTargetPrewarmFailureKind
                        .ResourceAllocationFailed,
                    key,
                    exception.Message,
                    exception.GetType().FullName));
            }
        }

        // Cache-owned successful allocations remain reusable, but no partial
        // frame mapping is published when any canonical resource failed.
        if (failures.Count != 0)
        {
            return new MapRenderOpenGlNormalCameraColorTargetPrewarmResult(
                null,
                failures);
        }

        MapRenderOpenGlNormalCameraColorTargetResourceBinding[] bindings = plan
            .Bindings
            .Select(binding =>
                new MapRenderOpenGlNormalCameraColorTargetResourceBinding(
                    binding,
                    resources[binding.ResourceKey]))
            .ToArray();
        return new MapRenderOpenGlNormalCameraColorTargetPrewarmResult(
            new MapRenderOpenGlNormalCameraColorTargetResourceFrame(
                cache.ContextIdentity,
                plan,
                bindings),
            []);
    }

    private static MapRenderOpenGlNormalCameraColorTargetPrewarmFailure Failure(
        MapRenderOpenGlNormalCameraColorTargetPrewarmFailureKind kind,
        MapRenderOpenGlNormalCameraColorTargetKey key,
        string detail,
        string? exceptionType = null) =>
        new(kind, key.CanonicalTarget, key, detail, exceptionType);
}
