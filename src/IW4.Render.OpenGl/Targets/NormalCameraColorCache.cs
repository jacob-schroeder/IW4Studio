using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl.Targets;

/// <summary>Context-local allocation contract for color texture/FBO pairs.</summary>
public interface IMapRenderOpenGlNormalCameraColorTargetResourceAllocator
{
    string ContextIdentity { get; }

    MapRenderSilkNormalCameraColorTargetCapabilities Capabilities { get; }

    MapRenderOpenGlNormalCameraColorTargetResource Allocate(
        MapRenderOpenGlNormalCameraColorTargetKey key);

    void DeleteTexture(uint textureHandle);

    void DeleteFramebuffer(uint framebufferHandle);
}

/// <summary>
/// Render-thread/context-owned allocation of one single-color RGBA8 texture
/// and complete FBO. One-sample and logical-size multisample resources are
/// both supported; temporary target-specific texture/read/draw bindings are
/// restored.
/// </summary>
public sealed class SilkMapRenderOpenGlNormalCameraColorTargetResourceAllocator :
    IMapRenderOpenGlNormalCameraColorTargetResourceAllocator
{
    private readonly IMapRenderSilkNormalCameraColorTargetApi _api;
    private readonly int _ownerThreadId;

    public SilkMapRenderOpenGlNormalCameraColorTargetResourceAllocator(
        GL gl,
        string contextIdentity)
        : this(
            new SilkMapRenderOpenGlNormalCameraColorTargetApi(gl),
            contextIdentity)
    {
    }

    internal SilkMapRenderOpenGlNormalCameraColorTargetResourceAllocator(
        IMapRenderSilkNormalCameraColorTargetApi api,
        string contextIdentity)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentException.ThrowIfNullOrWhiteSpace(contextIdentity);
        if (!api.Capabilities.SupportsSingleColorRgba8Framebuffer)
        {
            throw new NotSupportedException(
                "The current GL context does not expose one RGBA8 color attachment and framebuffer objects.");
        }

        _api = api;
        ContextIdentity = contextIdentity;
        Capabilities = api.Capabilities;
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    public string ContextIdentity { get; }

    public MapRenderSilkNormalCameraColorTargetCapabilities Capabilities { get; }

    public MapRenderOpenGlNormalCameraColorTargetResource Allocate(
        MapRenderOpenGlNormalCameraColorTargetKey key)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(key);
        ValidateAllocationLimits(key);

        MapRenderOpenGlNormalCameraTextureTarget textureTarget =
            key.HostTextureTarget;
        uint previousTexture = _api.GetBoundTexture(textureTarget);
        uint previousDrawFramebuffer = _api.GetBoundDrawFramebuffer();
        uint previousReadFramebuffer = _api.GetBoundReadFramebuffer();
        uint texture = 0;
        uint framebuffer = 0;
        var failures = new GlResourceFailureCollector();
        try
        {
            texture = _api.CreateTexture();
            if (texture == 0)
                throw new InvalidOperationException("Silk returned texture handle zero.");
            _api.BindTexture(textureTarget, texture);
            _api.AllocateLogicalRgba8LevelZero(
                textureTarget,
                key.HostStorageWidth,
                key.HostStorageHeight,
                key.HostSampleCount,
                key.HostUsesFixedSampleLocations);
            if (textureTarget ==
                MapRenderOpenGlNormalCameraTextureTarget.Texture2D)
            {
                // Restrict storage completeness to the allocated level
                // without authoring later exact sampler state. Multisample
                // texture objects have no mip-level parameters.
                _api.SetTextureMipLevelRange(textureTarget, 0, 0);
            }

            framebuffer = _api.CreateFramebuffer();
            if (framebuffer == 0)
                throw new InvalidOperationException("Silk returned framebuffer handle zero.");
            _api.BindDrawFramebuffer(framebuffer);
            _api.AttachTextureToColorZero(textureTarget, texture);
            _api.SelectDrawColorZero();
            if (!_api.IsDrawFramebufferComplete())
            {
                throw new InvalidOperationException(
                    "The color-only RGBA8 framebuffer is incomplete.");
            }
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
        finally
        {
            failures.TryExecute(
                () => _api.BindTexture(textureTarget, previousTexture));
            failures.TryExecute(
                () => _api.BindDrawFramebuffer(previousDrawFramebuffer));
            failures.TryExecute(
                () => _api.BindReadFramebuffer(previousReadFramebuffer));
        }

        if (failures.HasFailures)
        {
            failures.TryExecute(() =>
            {
                if (framebuffer != 0)
                    _api.DeleteFramebuffer(framebuffer);
            });
            failures.TryExecute(() =>
            {
                if (texture != 0)
                    _api.DeleteTexture(texture);
            });
            failures.ThrowAggregate(
                "Silk normal-camera color-target allocation failed; partial objects were deleted when possible.");
        }

        return new MapRenderOpenGlNormalCameraColorTargetResource(
            key,
            texture,
            framebuffer);
    }

    public void DeleteTexture(uint textureHandle)
    {
        EnsureOwnerThread();
        if (textureHandle == 0)
            throw new ArgumentOutOfRangeException(nameof(textureHandle));
        _api.DeleteTexture(textureHandle);
    }

    public void DeleteFramebuffer(uint framebufferHandle)
    {
        EnsureOwnerThread();
        if (framebufferHandle == 0)
            throw new ArgumentOutOfRangeException(nameof(framebufferHandle));
        _api.DeleteFramebuffer(framebufferHandle);
    }

    private void ValidateAllocationLimits(
        MapRenderOpenGlNormalCameraColorTargetKey key)
    {
        int maximum = Capabilities.MaximumTextureSize;
        if (key.HostStorageWidth > maximum ||
            key.HostStorageHeight > maximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(key),
                $"Host color storage {key.HostStorageWidth}x{key.HostStorageHeight} exceeds GL_MAX_TEXTURE_SIZE {maximum}.");
        }
        if (!Capabilities.SupportsSampleCount(key.HostSampleCount))
        {
            throw new NotSupportedException(
                $"Host color sample count {key.HostSampleCount} exceeds the current GL texture-multisample capability (GL_MAX_SAMPLES={Capabilities.MaximumSamples}, GL_MAX_COLOR_TEXTURE_SAMPLES={Capabilities.MaximumColorTextureSamples}).");
        }
        if (!key.HostStorageFootprintMatchesPs3Backing)
        {
            throw new InvalidOperationException(
                "The host color sample footprint no longer matches the PS3 backing allocation arithmetic.");
        }
        _ = key.HostStorageByteCount;
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException(
                "Silk normal-camera color-target allocator may only be used on its owning render thread.");
        }
    }
}

/// <summary>
/// Allocation-once ownership for normal-camera color texture/FBO pairs in one
/// current GL context and on one render thread.
/// </summary>
public sealed class MapRenderOpenGlNormalCameraColorTargetResourceCache : IDisposable
{
    private readonly IMapRenderOpenGlNormalCameraColorTargetResourceAllocator _allocator;
    private readonly MapRenderSilkNormalCameraColorTargetCapabilities _capabilities;
    private readonly Dictionary<
        MapRenderOpenGlNormalCameraColorTargetKey,
        MapRenderOpenGlNormalCameraColorTargetResource> _resources = [];
    private readonly HashSet<uint> _ownedTextures = [];
    private readonly HashSet<uint> _ownedFramebuffers = [];
    private readonly GlResourceCacheScope _scope;

    public MapRenderOpenGlNormalCameraColorTargetResourceCache(
        IMapRenderOpenGlNormalCameraColorTargetResourceAllocator allocator)
    {
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentException.ThrowIfNullOrWhiteSpace(allocator.ContextIdentity);
        ArgumentNullException.ThrowIfNull(allocator.Capabilities);
        _allocator = allocator;
        _scope = new GlResourceCacheScope(
            allocator.ContextIdentity,
            "OpenGL normal-camera color-target cache may only be used and disposed on its owning render thread.");
        _capabilities = allocator.Capabilities;
    }

    public string ContextIdentity
    {
        get
        {
            EnsureUsableOnOwnerThread();
            return _scope.ContextIdentity;
        }
    }

    public MapRenderSilkNormalCameraColorTargetCapabilities Capabilities
    {
        get
        {
            EnsureUsableOnOwnerThread();
            return _capabilities;
        }
    }

    public int ResourceCount
    {
        get
        {
            EnsureUsableOnOwnerThread();
            return _resources.Count;
        }
    }

    public MapRenderOpenGlNormalCameraColorTargetResource GetOrAllocate(
        MapRenderOpenGlNormalCameraColorTargetKey key)
    {
        EnsureUsableOnOwnerThread();
        ArgumentNullException.ThrowIfNull(key);
        EnsureAllocatorIdentityAndCapabilities();
        ValidateLimits(key);
        if (_resources.TryGetValue(
                key,
                out MapRenderOpenGlNormalCameraColorTargetResource? cached))
        {
            return cached;
        }

        MapRenderOpenGlNormalCameraColorTargetResource resource =
            _allocator.Allocate(key) ??
            throw new InvalidOperationException(
                "OpenGL normal-camera color allocator returned no resource.");
        bool textureCollision = _ownedTextures.Contains(resource.TextureHandle);
        bool framebufferCollision = _ownedFramebuffers.Contains(
            resource.FramebufferHandle);
        if (resource.Key != key)
        {
            RejectResource(
                resource,
                "OpenGL normal-camera color allocator returned a resource for another exact key.",
                textureCollision,
                framebufferCollision);
        }
        if (textureCollision || framebufferCollision)
        {
            RejectResource(
                resource,
                textureCollision
                    ? $"OpenGL normal-camera color allocator reused owned texture handle {resource.TextureHandle} for another key."
                    : $"OpenGL normal-camera color allocator reused owned framebuffer handle {resource.FramebufferHandle} for another key.",
                textureCollision,
                framebufferCollision);
        }

        _ownedTextures.Add(resource.TextureHandle);
        _ownedFramebuffers.Add(resource.FramebufferHandle);
        _resources.Add(key, resource);
        return resource;
    }

    public void Dispose()
    {
        if (!_scope.BeginDispose())
            return;

        var failures = new GlResourceFailureCollector();
        foreach (uint framebuffer in _ownedFramebuffers)
        {
            failures.TryExecute(() => _allocator.DeleteFramebuffer(framebuffer));
        }
        foreach (uint texture in _ownedTextures)
            failures.TryExecute(() => _allocator.DeleteTexture(texture));

        _resources.Clear();
        _ownedFramebuffers.Clear();
        _ownedTextures.Clear();
        if (failures.HasFailures)
        {
            failures.ThrowAggregate(
                "One or more normal-camera color-target objects could not be deleted.");
        }
    }

    private void RejectResource(
        MapRenderOpenGlNormalCameraColorTargetResource resource,
        string message,
        bool textureCollision,
        bool framebufferCollision)
    {
        var mismatch = new InvalidOperationException(message);
        var failures = new GlResourceFailureCollector();
        failures.Add(mismatch);
        if (!framebufferCollision)
        {
            failures.TryExecute(
                () => _allocator.DeleteFramebuffer(resource.FramebufferHandle));
        }
        if (!textureCollision)
        {
            failures.TryExecute(
                () => _allocator.DeleteTexture(resource.TextureHandle));
        }

        if (failures.Count == 1)
            throw mismatch;
        failures.ThrowAggregate(message);
    }

    private void ValidateLimits(MapRenderOpenGlNormalCameraColorTargetKey key)
    {
        if (!_capabilities.SupportsSampleCount(key.HostSampleCount))
        {
            throw new NotSupportedException(
                $"The current GL context cannot allocate an RGBA8 framebuffer with {key.HostSampleCount} sample(s).");
        }
        if (key.HostStorageWidth > _capabilities.MaximumTextureSize ||
            key.HostStorageHeight > _capabilities.MaximumTextureSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(key),
                $"Host color storage {key.HostStorageWidth}x{key.HostStorageHeight} exceeds GL_MAX_TEXTURE_SIZE {_capabilities.MaximumTextureSize}.");
        }
        if (!key.HostStorageFootprintMatchesPs3Backing)
        {
            throw new InvalidOperationException(
                "The host color sample footprint does not match the PS3 backing allocation arithmetic.");
        }
        _ = key.HostStorageByteCount;
    }

    private void EnsureUsableOnOwnerThread()
    {
        _scope.EnsureUsable(this);
    }

    private void EnsureAllocatorIdentityAndCapabilities()
    {
        _scope.EnsureContextIdentity(
            _allocator.ContextIdentity,
            "OpenGL normal-camera color allocator context identity changed after cache creation.");
        if (_allocator.Capabilities != _capabilities)
        {
            throw new InvalidOperationException(
                "OpenGL normal-camera color allocator capabilities changed after cache creation.");
        }
    }
}
