using IW4.Render.Scheduling.Lifecycle;
using System.Collections.ObjectModel;

namespace IW4.Render.OpenGl.Targets;

/// <summary>
/// Portable host storage contract for normal-camera color targets. This names
/// logical shader component behavior, not raw PS3 surface byte packing.
/// </summary>
public enum MapRenderOpenGlNormalCameraColorStorageSemantics
{
    LogicalRgba8Unorm = 0
}

/// <summary>
/// Exact allocation key for one canonical normal-camera color resource.
/// Target-row aliases reuse the canonical key instead of allocating again.
/// </summary>
public sealed record MapRenderOpenGlNormalCameraColorTargetKey
{
    internal MapRenderOpenGlNormalCameraColorTargetKey(
        MapRenderNormalCameraTargetPlan canonicalTarget,
        int displayWidth,
        int displayHeight)
    {
        ArgumentNullException.ThrowIfNull(canonicalTarget);
        if (canonicalTarget.InitialAliasOf is not null)
        {
            throw new ArgumentException(
                "A color-target resource key must be created from its canonical target row.",
                nameof(canonicalTarget));
        }

        CanonicalTarget = canonicalTarget.Kind;
        DisplayWidth = displayWidth;
        DisplayHeight = displayHeight;
        Extent = canonicalTarget.ResolveExtent(displayWidth, displayHeight);
        RawProgramImageSlot = canonicalTarget.RawProgramImageSlot;
        RawDimensionFamily = canonicalTarget.RawDimensionFamily;
        RawDimensionShift = canonicalTarget.RawDimensionShift;
        RawImageSetupFormat = canonicalTarget.RawImageSetupFormat;
        RawImageSetupFlags = canonicalTarget.RawImageSetupFlags;
        RawImageFormatByte = canonicalTarget.RawImageFormatByte;
        RawSurfaceType = canonicalTarget.RawSurfaceType;
        RawAntialias = canonicalTarget.RawAntialias;
        RawColorTargetMask = canonicalTarget.RawColorTargetMask;
        RawColorFormat = canonicalTarget.RawColorFormat;
    }

    public MapRenderNormalCameraTargetKind CanonicalTarget { get; }

    public int DisplayWidth { get; }

    public int DisplayHeight { get; }

    public MapRenderNormalCameraTargetExtent Extent { get; }

    public byte RawProgramImageSlot { get; }

    public byte RawDimensionFamily { get; }

    public byte RawDimensionShift { get; }

    public uint RawImageSetupFormat { get; }

    public uint RawImageSetupFlags { get; }

    public byte RawImageFormatByte { get; }

    public byte RawSurfaceType { get; }

    public byte RawAntialias { get; }

    public byte RawColorTargetMask { get; }

    public byte RawColorFormat { get; }

    public MapRenderOpenGlNormalCameraColorStorageSemantics HostStorageSemantics =>
        MapRenderOpenGlNormalCameraColorStorageSemantics.LogicalRgba8Unorm;

    public int HostBytesPerTexel => 4;

    public int HostMipLevelCount => 1;

    public int Ps3SurfaceSampleCount => RawAntialias switch
    {
        0 => 1,
        3 => 2,
        _ => throw new InvalidOperationException(
            $"Unsupported PS3 surface antialias value {RawAntialias}.")
    };

    public int HostSampleCount => Ps3SurfaceSampleCount;

    public MapRenderOpenGlNormalCameraTextureTarget HostTextureTarget =>
        HostSampleCount > 1
            ? MapRenderOpenGlNormalCameraTextureTarget.Texture2DMultisample
            : MapRenderOpenGlNormalCameraTextureTarget.Texture2D;

    public int HostStorageWidth => HostSampleCount > 1
        ? Extent.LogicalWidth
        : Extent.BackingWidth;

    public int HostStorageHeight => HostSampleCount > 1
        ? Extent.LogicalHeight
        : Extent.BackingHeight;

    public bool HostUsesFixedSampleLocations => HostSampleCount > 1;

    public bool HostSampleCountMatchesPs3 =>
        HostSampleCount == Ps3SurfaceSampleCount;

    public ulong HostStorageByteCount =>
        (ulong)(uint)HostStorageWidth *
        (uint)HostStorageHeight *
        (uint)HostBytesPerTexel *
        (uint)HostSampleCount;

    public ulong Ps3BackingStorageByteCount =>
        (ulong)(uint)Extent.BackingWidth *
        (uint)Extent.BackingHeight *
        (uint)HostBytesPerTexel;

    public bool HostStorageFootprintMatchesPs3Backing =>
        HostStorageByteCount == Ps3BackingStorageByteCount;

    internal bool MatchesCanonicalTarget(
        MapRenderNormalCameraTargetPlan target,
        int displayWidth,
        int displayHeight) =>
        target.InitialAliasOf is null &&
        CanonicalTarget == target.Kind &&
        DisplayWidth == displayWidth &&
        DisplayHeight == displayHeight &&
        Extent == target.ResolveExtent(displayWidth, displayHeight) &&
        RawProgramImageSlot == target.RawProgramImageSlot &&
        RawDimensionFamily == target.RawDimensionFamily &&
        RawDimensionShift == target.RawDimensionShift &&
        RawImageSetupFormat == target.RawImageSetupFormat &&
        RawImageSetupFlags == target.RawImageSetupFlags &&
        RawImageFormatByte == target.RawImageFormatByte &&
        RawSurfaceType == target.RawSurfaceType &&
        RawAntialias == target.RawAntialias &&
        RawColorTargetMask == target.RawColorTargetMask &&
        RawColorFormat == target.RawColorFormat;
}

/// <summary>
/// One complete color-only texture/FBO pair. The FBO deliberately has no
/// depth/stencil attachment and is not authorized as an Event20 draw target.
/// </summary>
public sealed record MapRenderOpenGlNormalCameraColorTargetResource
{
    public MapRenderOpenGlNormalCameraColorTargetResource(
        MapRenderOpenGlNormalCameraColorTargetKey key,
        uint textureHandle,
        uint framebufferHandle)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (textureHandle == 0)
            throw new ArgumentOutOfRangeException(nameof(textureHandle));
        if (framebufferHandle == 0)
            throw new ArgumentOutOfRangeException(nameof(framebufferHandle));

        Key = key;
        TextureHandle = textureHandle;
        FramebufferHandle = framebufferHandle;
    }

    public MapRenderOpenGlNormalCameraColorTargetKey Key { get; }

    public uint TextureHandle { get; }

    public uint FramebufferHandle { get; }

    public MapRenderOpenGlNormalCameraTextureTarget TextureTarget =>
        Key.HostTextureTarget;

    public int SampleCount => Key.HostSampleCount;

    /// <summary>
    /// Allocation configures only <c>GL_COLOR_ATTACHMENT0</c> as a draw
    /// buffer; no implicit MRT availability is inferred from shader outputs.
    /// </summary>
    public int HostDrawBufferCount => 1;

}

/// <summary>One planned target id resolved to its context-owned color pair.</summary>
public sealed record MapRenderOpenGlNormalCameraColorTargetResourceBinding
{
    internal MapRenderOpenGlNormalCameraColorTargetResourceBinding(
        MapRenderOpenGlNormalCameraColorTargetBindingPlan plan,
        MapRenderOpenGlNormalCameraColorTargetResource resource)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(resource);
        if (resource.Key != plan.ResourceKey)
        {
            throw new ArgumentException(
                "The color resource does not match the target binding key.",
                nameof(resource));
        }

        Plan = plan;
        Resource = resource;
    }

    public MapRenderOpenGlNormalCameraColorTargetBindingPlan Plan { get; }

    public MapRenderOpenGlNormalCameraColorTargetResource Resource { get; }
}

/// <summary>
/// Complete atomic color-only frame mapping. This object does not make the
/// lifecycle target resources executable while depth ownership is open.
/// </summary>
public sealed class MapRenderOpenGlNormalCameraColorTargetResourceFrame
{
    private readonly MapRenderOpenGlNormalCameraColorTargetResourceBinding[] _bindings;

    internal MapRenderOpenGlNormalCameraColorTargetResourceFrame(
        string contextIdentity,
        MapRenderOpenGlNormalCameraColorFramePlan plan,
        IReadOnlyList<MapRenderOpenGlNormalCameraColorTargetResourceBinding> bindings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contextIdentity);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(bindings);
        _bindings = bindings.ToArray();
        if (_bindings.Length != plan.Bindings.Count ||
            !_bindings.Select(binding => binding.Plan)
                .SequenceEqual(plan.Bindings))
        {
            throw new ArgumentException(
                "A color resource frame must resolve every planned target in exact order.",
                nameof(bindings));
        }
        MapRenderOpenGlNormalCameraColorTargetResourceBinding target3 =
            _bindings.Single(binding => binding.Plan.Target ==
                MapRenderNormalCameraTargetKind.ResolvedPostSun);
        MapRenderOpenGlNormalCameraColorTargetResourceBinding target4 =
            _bindings.Single(binding => binding.Plan.Target ==
                MapRenderNormalCameraTargetKind.ResolvedScene);
        if (!ReferenceEquals(target3.Resource, target4.Resource))
        {
            throw new ArgumentException(
                "Targets 3 and 4 must resolve to the same context-owned color resource.",
                nameof(bindings));
        }

        ContextIdentity = contextIdentity;
        Plan = plan;
        Bindings = Array.AsReadOnly(_bindings);
    }

    public string ContextIdentity { get; }

    public MapRenderOpenGlNormalCameraColorFramePlan Plan { get; }

    public ReadOnlyCollection<MapRenderOpenGlNormalCameraColorTargetResourceBinding> Bindings { get; }

    public int UniqueResourceCount => _bindings
        .Select(binding => binding.Resource)
        .Distinct(ReferenceEqualityComparer.Instance)
        .Count();

    public MapRenderOpenGlNormalCameraColorTargetResourceBinding GetBinding(
        MapRenderNormalCameraTargetKind target)
    {
        if (!Enum.IsDefined(target))
            throw new ArgumentOutOfRangeException(nameof(target));
        return _bindings.Single(binding => binding.Plan.Target == target);
    }

}
