using IW4.Render.Scheduling.Lifecycle;
using System.Collections.ObjectModel;

namespace IW4.Render.OpenGl.Targets;

/// <summary>Host storage used for a dedicated Z24S8 target.</summary>
public enum MapRenderOpenGlNormalCameraDepthStencilStorageSemantics
{
    Depth24Stencil8 = 0
}

/// <summary>
/// Exact host-allocation key for one PS3 target-owned depth/stencil resource.
/// The color key identifies the RGBA8 attachment borrowed by the combined FBO.
/// </summary>
public sealed record MapRenderOpenGlNormalCameraDepthStencilTargetKey
{
    internal MapRenderOpenGlNormalCameraDepthStencilTargetKey(
        MapRenderNormalCameraTargetPlan target,
        MapRenderOpenGlNormalCameraColorTargetKey colorTargetKey)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(colorTargetKey);
        if (!target.HasDedicatedDepthAllocation)
        {
            throw new ArgumentException(
                "A depth/stencil key requires a dedicated Z24S8 target.",
                nameof(target));
        }
        if (target.InitialAliasOf is not null ||
            colorTargetKey.CanonicalTarget != target.Kind ||
            !colorTargetKey.MatchesCanonicalTarget(
                target,
                colorTargetKey.DisplayWidth,
                colorTargetKey.DisplayHeight))
        {
            throw new ArgumentException(
                "The color key must be the exact canonical resource for the depth target.",
                nameof(colorTargetKey));
        }

        Target = target.Kind;
        ColorTargetKey = colorTargetKey;
        Extent = colorTargetKey.Extent;
        RawDepthFormat = target.RawDepthFormat;
        RawDepthLocation = target.RawDepthLocation;
        RawDepthAllocationSetupFormat = target.RawDepthAllocationSetupFormat!.Value;
        RawDepthAllocationTextureFormatByte =
            target.RawDepthAllocationTextureFormatByte!.Value;
        RawDepthSamplingViewProgramImageSlot =
            target.RawDepthSamplingViewProgramImageSlot!.Value;
        RawDepthSamplingViewSetupFormat =
            target.RawDepthSamplingViewSetupFormat!.Value;
        RawDepthSamplingViewSetupFlags =
            target.RawDepthSamplingViewSetupFlags!.Value;
    }

    public MapRenderNormalCameraTargetKind Target { get; }

    public MapRenderOpenGlNormalCameraColorTargetKey ColorTargetKey { get; }

    public MapRenderNormalCameraTargetExtent Extent { get; }

    public byte RawDepthFormat { get; }

    public byte RawDepthLocation { get; }

    public uint RawDepthAllocationSetupFormat { get; }

    public byte RawDepthAllocationTextureFormatByte { get; }

    public byte RawDepthSamplingViewProgramImageSlot { get; }

    public uint RawDepthSamplingViewSetupFormat { get; }

    public uint RawDepthSamplingViewSetupFlags { get; }

    public MapRenderOpenGlNormalCameraDepthStencilStorageSemantics
        HostStorageSemantics =>
            MapRenderOpenGlNormalCameraDepthStencilStorageSemantics
                .Depth24Stencil8;

    public int HostBytesPerTexel => 4;

    public int HostMipLevelCount => 1;

    public int Ps3SurfaceSampleCount =>
        ColorTargetKey.Ps3SurfaceSampleCount;

    public int HostSampleCount => ColorTargetKey.HostSampleCount;

    public MapRenderOpenGlNormalCameraTextureTarget HostTextureTarget =>
        ColorTargetKey.HostTextureTarget;

    public int HostStorageWidth => ColorTargetKey.HostStorageWidth;

    public int HostStorageHeight => ColorTargetKey.HostStorageHeight;

    public bool HostUsesFixedSampleLocations =>
        ColorTargetKey.HostUsesFixedSampleLocations;

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

}

/// <summary>
/// One borrowed color texture plus owned D24S8 texture and combined FBO. The
/// source color-only FBO remains a distinct, unmodified resource.
/// </summary>
public sealed record MapRenderOpenGlNormalCameraDepthStencilTargetResource
{
    public MapRenderOpenGlNormalCameraDepthStencilTargetResource(
        MapRenderOpenGlNormalCameraDepthStencilTargetKey key,
        MapRenderOpenGlNormalCameraColorTargetResource colorResource,
        uint depthStencilTextureHandle,
        uint combinedFramebufferHandle)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(colorResource);
        if (colorResource.Key != key.ColorTargetKey)
        {
            throw new ArgumentException(
                "The borrowed color resource does not match the depth target key.",
                nameof(colorResource));
        }
        if (depthStencilTextureHandle == 0)
            throw new ArgumentOutOfRangeException(nameof(depthStencilTextureHandle));
        if (combinedFramebufferHandle == 0)
            throw new ArgumentOutOfRangeException(nameof(combinedFramebufferHandle));

        Key = key;
        ColorResource = colorResource;
        DepthStencilTextureHandle = depthStencilTextureHandle;
        CombinedFramebufferHandle = combinedFramebufferHandle;
    }

    public MapRenderOpenGlNormalCameraDepthStencilTargetKey Key { get; }

    public MapRenderOpenGlNormalCameraColorTargetResource ColorResource { get; }

    public uint DepthStencilTextureHandle { get; }

    public uint CombinedFramebufferHandle { get; }

    public MapRenderOpenGlNormalCameraTextureTarget TextureTarget =>
        Key.HostTextureTarget;

    public int SampleCount => Key.HostSampleCount;

}

/// <summary>One dedicated target resolved to its combined color/depth FBO.</summary>
public sealed record MapRenderOpenGlNormalCameraDepthStencilTargetResourceBinding
{
    internal MapRenderOpenGlNormalCameraDepthStencilTargetResourceBinding(
        MapRenderOpenGlNormalCameraDepthStencilTargetKey key,
        MapRenderOpenGlNormalCameraDepthStencilTargetResource resource)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(resource);
        if (resource.Key != key)
        {
            throw new ArgumentException(
                "The depth/stencil resource does not match the planned target key.",
                nameof(resource));
        }

        Key = key;
        Resource = resource;
    }

    public MapRenderOpenGlNormalCameraDepthStencilTargetKey Key { get; }

    public MapRenderOpenGlNormalCameraDepthStencilTargetResource Resource { get; }
}

/// <summary>
/// Complete atomic mapping for the two dedicated depth targets. This does not
/// implement the PS3 sampling reinterpret views or authorize lifecycle draws.
/// </summary>
public sealed class MapRenderOpenGlNormalCameraDepthStencilTargetResourceFrame
{
    private readonly
        MapRenderOpenGlNormalCameraDepthStencilTargetResourceBinding[]
        _bindings;

    internal MapRenderOpenGlNormalCameraDepthStencilTargetResourceFrame(
        string contextIdentity,
        MapRenderOpenGlNormalCameraDepthStencilFramePlan plan,
        MapRenderOpenGlNormalCameraColorTargetResourceFrame colorFrame,
        IReadOnlyList<
            MapRenderOpenGlNormalCameraDepthStencilTargetResourceBinding>
            bindings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contextIdentity);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(colorFrame);
        ArgumentNullException.ThrowIfNull(bindings);
        if (!string.Equals(
                contextIdentity,
                colorFrame.ContextIdentity,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The combined resource frame and color frame must share a GL context.",
                nameof(colorFrame));
        }

        _bindings = bindings.ToArray();
        if (_bindings.Length != plan.Targets.Count ||
            !_bindings.Select(binding => binding.Key)
                .SequenceEqual(plan.Targets))
        {
            throw new ArgumentException(
                "A depth/stencil resource frame must resolve both planned targets in exact order.",
                nameof(bindings));
        }
        foreach (MapRenderOpenGlNormalCameraDepthStencilTargetResourceBinding
                 binding in _bindings)
        {
            MapRenderOpenGlNormalCameraColorTargetResource borrowed = colorFrame
                .GetBinding(binding.Key.Target)
                .Resource;
            if (!ReferenceEquals(binding.Resource.ColorResource, borrowed))
            {
                throw new ArgumentException(
                    "Every combined FBO must borrow the exact color-frame resource object.",
                    nameof(bindings));
            }
        }

        ContextIdentity = contextIdentity;
        Plan = plan;
        ColorFrame = colorFrame;
        Bindings = Array.AsReadOnly(_bindings);
    }

    public string ContextIdentity { get; }

    public MapRenderOpenGlNormalCameraDepthStencilFramePlan Plan { get; }

    public MapRenderOpenGlNormalCameraColorTargetResourceFrame ColorFrame { get; }

    public ReadOnlyCollection<
        MapRenderOpenGlNormalCameraDepthStencilTargetResourceBinding>
        Bindings { get; }

    public MapRenderOpenGlNormalCameraDepthStencilTargetResourceBinding
        GetBinding(MapRenderNormalCameraTargetKind target)
    {
        if (!Enum.IsDefined(target))
            throw new ArgumentOutOfRangeException(nameof(target));
        return _bindings.Single(binding => binding.Key.Target == target);
    }

}
