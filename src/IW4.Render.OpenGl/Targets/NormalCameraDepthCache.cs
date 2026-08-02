using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl.Targets;

/// <summary>
/// Context-local allocation contract for a borrowed color texture plus an
/// owned D24S8 texture/combined-FBO pair.
/// </summary>
public interface IMapRenderOpenGlNormalCameraDepthStencilTargetResourceAllocator
{
    string ContextIdentity { get; }

    MapRenderSilkNormalCameraDepthStencilTargetCapabilities Capabilities { get; }

    MapRenderOpenGlNormalCameraDepthStencilTargetResource Allocate(
        MapRenderOpenGlNormalCameraDepthStencilTargetKey key,
        MapRenderOpenGlNormalCameraColorTargetResource colorResource);

    void DeleteTexture(uint textureHandle);

    void DeleteFramebuffer(uint framebufferHandle);
}

/// <summary>
/// Render-thread/context-owned allocation of one D24S8 texture and combined
/// RGBA8+D24S8 FBO. Attachment target and sample topology must exactly match
/// the borrowed color resource. Temporary bindings are restored.
/// </summary>
public sealed class SilkMapRenderOpenGlNormalCameraDepthStencilTargetResourceAllocator :
    IMapRenderOpenGlNormalCameraDepthStencilTargetResourceAllocator
{
    private readonly IMapRenderSilkNormalCameraDepthStencilTargetApi _api;
    private readonly int _ownerThreadId;

    public SilkMapRenderOpenGlNormalCameraDepthStencilTargetResourceAllocator(
        GL gl,
        string contextIdentity)
        : this(
            new SilkMapRenderOpenGlNormalCameraDepthStencilTargetApi(gl),
            contextIdentity)
    {
    }

    internal SilkMapRenderOpenGlNormalCameraDepthStencilTargetResourceAllocator(
        IMapRenderSilkNormalCameraDepthStencilTargetApi api,
        string contextIdentity)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentException.ThrowIfNullOrWhiteSpace(contextIdentity);
        if (!api.Capabilities.SupportsCombinedRgba8Depth24Stencil8Framebuffer)
        {
            throw new NotSupportedException(
                "The current GL context does not expose a D24S8 texture plus one-color framebuffer.");
        }

        _api = api;
        ContextIdentity = contextIdentity;
        Capabilities = api.Capabilities;
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    public string ContextIdentity { get; }

    public MapRenderSilkNormalCameraDepthStencilTargetCapabilities
        Capabilities { get; }

    public MapRenderOpenGlNormalCameraDepthStencilTargetResource Allocate(
        MapRenderOpenGlNormalCameraDepthStencilTargetKey key,
        MapRenderOpenGlNormalCameraColorTargetResource colorResource)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(colorResource);
        if (colorResource.Key != key.ColorTargetKey)
        {
            throw new ArgumentException(
                "The borrowed color resource does not match the depth target key.",
                nameof(colorResource));
        }
        if (colorResource.Key.HostTextureTarget != key.HostTextureTarget ||
            colorResource.Key.HostSampleCount != key.HostSampleCount)
        {
            throw new ArgumentException(
                "The borrowed color attachment does not match the depth/stencil texture target and sample count.",
                nameof(colorResource));
        }
        ValidateAllocationLimits(key);

        MapRenderOpenGlNormalCameraTextureTarget textureTarget =
            key.HostTextureTarget;
        uint previousTexture = _api.GetBoundTexture(textureTarget);
        uint previousDrawFramebuffer = _api.GetBoundDrawFramebuffer();
        uint previousReadFramebuffer = _api.GetBoundReadFramebuffer();
        uint depthStencilTexture = 0;
        uint combinedFramebuffer = 0;
        List<Exception>? failures = null;
        try
        {
            depthStencilTexture = _api.CreateTexture();
            if (depthStencilTexture == 0)
                throw new InvalidOperationException("Silk returned texture handle zero.");
            if (depthStencilTexture == colorResource.TextureHandle)
            {
                throw new InvalidOperationException(
                    "Silk returned the borrowed color texture handle for the owned depth/stencil texture.");
            }
            _api.BindTexture(textureTarget, depthStencilTexture);
            _api.AllocateDepth24Stencil8LevelZero(
                textureTarget,
                key.HostStorageWidth,
                key.HostStorageHeight,
                key.HostSampleCount,
                key.HostUsesFixedSampleLocations);
            if (textureTarget ==
                MapRenderOpenGlNormalCameraTextureTarget.Texture2D)
            {
                // Multisample textures have no mip-level parameters.
                _api.SetTextureMipLevelRange(textureTarget, 0, 0);
            }

            combinedFramebuffer = _api.CreateFramebuffer();
            if (combinedFramebuffer == 0)
                throw new InvalidOperationException("Silk returned framebuffer handle zero.");
            if (combinedFramebuffer == colorResource.FramebufferHandle)
            {
                throw new InvalidOperationException(
                    "Silk returned the borrowed color-only framebuffer handle for the combined framebuffer.");
            }
            _api.BindDrawFramebuffer(combinedFramebuffer);
            _api.AttachTextureToColorZero(
                colorResource.Key.HostTextureTarget,
                colorResource.TextureHandle);
            _api.AttachTextureToDepthStencil(
                textureTarget,
                depthStencilTexture);
            _api.SelectDrawColorZero();
            if (!_api.IsDrawFramebufferComplete())
            {
                throw new InvalidOperationException(
                    "The combined RGBA8+D24S8 framebuffer is incomplete.");
            }
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }
        finally
        {
            Restore(() => _api.BindTexture(textureTarget, previousTexture));
            Restore(() => _api.BindDrawFramebuffer(previousDrawFramebuffer));
            Restore(() => _api.BindReadFramebuffer(previousReadFramebuffer));
        }

        if (failures is not null)
        {
            DeletePartial(() =>
            {
                if (combinedFramebuffer != 0 &&
                    combinedFramebuffer != colorResource.FramebufferHandle)
                {
                    _api.DeleteFramebuffer(combinedFramebuffer);
                }
            });
            DeletePartial(() =>
            {
                if (depthStencilTexture != 0 &&
                    depthStencilTexture != colorResource.TextureHandle)
                {
                    _api.DeleteTexture(depthStencilTexture);
                }
            });
            throw new AggregateException(
                "Silk normal-camera depth/stencil allocation failed; partial owned objects were deleted when possible.",
                failures);
        }

        return new MapRenderOpenGlNormalCameraDepthStencilTargetResource(
            key,
            colorResource,
            depthStencilTexture,
            combinedFramebuffer);

        void Restore(Action restore)
        {
            try
            {
                restore();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        void DeletePartial(Action delete)
        {
            try
            {
                delete();
            }
            catch (Exception exception)
            {
                failures!.Add(exception);
            }
        }
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
        MapRenderOpenGlNormalCameraDepthStencilTargetKey key)
    {
        int maximum = Capabilities.MaximumTextureSize;
        if (key.HostStorageWidth > maximum ||
            key.HostStorageHeight > maximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(key),
                $"Host depth/stencil storage {key.HostStorageWidth}x{key.HostStorageHeight} exceeds GL_MAX_TEXTURE_SIZE {maximum}.");
        }
        if (!Capabilities.SupportsSampleCount(key.HostSampleCount))
        {
            throw new NotSupportedException(
                $"Host depth/stencil sample count {key.HostSampleCount} exceeds the current GL texture-multisample capability (GL_MAX_SAMPLES={Capabilities.MaximumSamples}, GL_MAX_DEPTH_TEXTURE_SAMPLES={Capabilities.MaximumDepthTextureSamples}).");
        }
        if (!key.HostStorageFootprintMatchesPs3Backing)
        {
            throw new InvalidOperationException(
                "The host depth/stencil sample footprint no longer matches the PS3 backing allocation arithmetic.");
        }
        _ = key.HostStorageByteCount;
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException(
                "Silk normal-camera depth/stencil allocator may only be used on its owning render thread.");
        }
    }
}

/// <summary>
/// Allocation-once ownership for the two dedicated D24S8 texture/combined-FBO
/// pairs in one GL context and render thread. Borrowed color objects are never
/// deleted; this cache must be disposed before its color resource frame owner.
/// </summary>
public sealed class MapRenderOpenGlNormalCameraDepthStencilTargetResourceCache :
    IDisposable
{
    private readonly
        IMapRenderOpenGlNormalCameraDepthStencilTargetResourceAllocator
        _allocator;
    private readonly MapRenderSilkNormalCameraDepthStencilTargetCapabilities
        _capabilities;
    private readonly MapRenderOpenGlNormalCameraColorTargetResourceFrame
        _colorFrame;
    private readonly Dictionary<
        MapRenderOpenGlNormalCameraDepthStencilTargetKey,
        MapRenderOpenGlNormalCameraDepthStencilTargetResource> _resources = [];
    private readonly HashSet<uint> _borrowedColorTextures;
    private readonly HashSet<uint> _borrowedColorFramebuffers;
    private readonly HashSet<uint> _ownedDepthStencilTextures = [];
    private readonly HashSet<uint> _ownedCombinedFramebuffers = [];
    private readonly GlResourceCacheScope _scope;

    public MapRenderOpenGlNormalCameraDepthStencilTargetResourceCache(
        IMapRenderOpenGlNormalCameraDepthStencilTargetResourceAllocator allocator,
        MapRenderOpenGlNormalCameraColorTargetResourceFrame colorFrame)
    {
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(colorFrame);
        ArgumentException.ThrowIfNullOrWhiteSpace(allocator.ContextIdentity);
        ArgumentNullException.ThrowIfNull(allocator.Capabilities);
        if (!string.Equals(
                allocator.ContextIdentity,
                colorFrame.ContextIdentity,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The depth allocator and borrowed color frame must belong to the same GL context.",
                nameof(colorFrame));
        }

        _allocator = allocator;
        _scope = new GlResourceCacheScope(
            allocator.ContextIdentity,
            "OpenGL normal-camera depth/stencil cache may only be used and disposed on its owning render thread.");
        _capabilities = allocator.Capabilities;
        _colorFrame = colorFrame;
        _borrowedColorTextures = colorFrame.Bindings
            .Select(binding => binding.Resource.TextureHandle)
            .ToHashSet();
        _borrowedColorFramebuffers = colorFrame.Bindings
            .Select(binding => binding.Resource.FramebufferHandle)
            .ToHashSet();
    }

    public string ContextIdentity
    {
        get
        {
            EnsureUsableOnOwnerThread();
            return _scope.ContextIdentity;
        }
    }

    public MapRenderSilkNormalCameraDepthStencilTargetCapabilities Capabilities
    {
        get
        {
            EnsureUsableOnOwnerThread();
            return _capabilities;
        }
    }

    public MapRenderOpenGlNormalCameraColorTargetResourceFrame ColorFrame
    {
        get
        {
            EnsureUsableOnOwnerThread();
            return _colorFrame;
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

    public MapRenderOpenGlNormalCameraDepthStencilTargetResource GetOrAllocate(
        MapRenderOpenGlNormalCameraDepthStencilTargetKey key)
    {
        EnsureUsableOnOwnerThread();
        ArgumentNullException.ThrowIfNull(key);
        EnsureAllocatorIdentityAndCapabilities();
        MapRenderOpenGlNormalCameraColorTargetResource colorResource =
            ResolveBorrowedColorResource(key);
        ValidateLimits(key);
        if (_resources.TryGetValue(
                key,
                out MapRenderOpenGlNormalCameraDepthStencilTargetResource?
                    cached))
        {
            if (!ReferenceEquals(cached.ColorResource, colorResource))
            {
                throw new InvalidOperationException(
                    "The cached combined FBO does not reference the exact borrowed color resource.");
            }
            return cached;
        }

        MapRenderOpenGlNormalCameraDepthStencilTargetResource resource =
            _allocator.Allocate(key, colorResource) ??
            throw new InvalidOperationException(
                "OpenGL normal-camera depth/stencil allocator returned no resource.");
        bool depthOwnedCollision = _ownedDepthStencilTextures.Contains(
            resource.DepthStencilTextureHandle);
        bool depthBorrowedCollision = _borrowedColorTextures.Contains(
            resource.DepthStencilTextureHandle);
        bool framebufferOwnedCollision = _ownedCombinedFramebuffers.Contains(
            resource.CombinedFramebufferHandle);
        bool framebufferBorrowedCollision =
            _borrowedColorFramebuffers.Contains(
                resource.CombinedFramebufferHandle);
        if (resource.Key != key ||
            !ReferenceEquals(resource.ColorResource, colorResource))
        {
            RejectResource(
                resource,
                "OpenGL normal-camera depth/stencil allocator returned a resource for another exact key or color object.",
                depthOwnedCollision || depthBorrowedCollision,
                framebufferOwnedCollision || framebufferBorrowedCollision);
        }
        if (depthOwnedCollision || depthBorrowedCollision ||
            framebufferOwnedCollision || framebufferBorrowedCollision)
        {
            string message = depthOwnedCollision
                ? $"OpenGL normal-camera depth/stencil allocator reused owned texture handle {resource.DepthStencilTextureHandle} for another key."
                : depthBorrowedCollision
                    ? $"OpenGL normal-camera depth/stencil allocator reused borrowed color texture handle {resource.DepthStencilTextureHandle}."
                    : framebufferOwnedCollision
                        ? $"OpenGL normal-camera depth/stencil allocator reused owned framebuffer handle {resource.CombinedFramebufferHandle} for another key."
                        : $"OpenGL normal-camera depth/stencil allocator reused borrowed color framebuffer handle {resource.CombinedFramebufferHandle}.";
            RejectResource(
                resource,
                message,
                depthOwnedCollision || depthBorrowedCollision,
                framebufferOwnedCollision || framebufferBorrowedCollision);
        }

        _ownedDepthStencilTextures.Add(resource.DepthStencilTextureHandle);
        _ownedCombinedFramebuffers.Add(resource.CombinedFramebufferHandle);
        _resources.Add(key, resource);
        return resource;
    }

    public void Dispose()
    {
        if (!_scope.BeginDispose())
            return;

        List<Exception>? failures = null;
        foreach (uint framebuffer in _ownedCombinedFramebuffers)
            TryDelete(() => _allocator.DeleteFramebuffer(framebuffer));
        foreach (uint texture in _ownedDepthStencilTextures)
            TryDelete(() => _allocator.DeleteTexture(texture));

        _resources.Clear();
        _ownedCombinedFramebuffers.Clear();
        _ownedDepthStencilTextures.Clear();
        if (failures is not null)
        {
            throw new AggregateException(
                "One or more normal-camera depth/stencil objects could not be deleted.",
                failures);
        }

        void TryDelete(Action delete)
        {
            try
            {
                delete();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }
    }

    private MapRenderOpenGlNormalCameraColorTargetResource
        ResolveBorrowedColorResource(
            MapRenderOpenGlNormalCameraDepthStencilTargetKey key)
    {
        MapRenderOpenGlNormalCameraColorTargetResource resource = _colorFrame
            .GetBinding(key.Target)
            .Resource;
        if (resource.Key != key.ColorTargetKey)
        {
            throw new ArgumentException(
                "The depth/stencil key does not match this cache's borrowed color frame.",
                nameof(key));
        }
        return resource;
    }

    private void RejectResource(
        MapRenderOpenGlNormalCameraDepthStencilTargetResource resource,
        string message,
        bool textureCollision,
        bool framebufferCollision)
    {
        var mismatch = new InvalidOperationException(message);
        var failures = new List<Exception> { mismatch };
        if (!framebufferCollision)
        {
            try
            {
                _allocator.DeleteFramebuffer(resource.CombinedFramebufferHandle);
            }
            catch (Exception cleanup)
            {
                failures.Add(cleanup);
            }
        }
        if (!textureCollision)
        {
            try
            {
                _allocator.DeleteTexture(resource.DepthStencilTextureHandle);
            }
            catch (Exception cleanup)
            {
                failures.Add(cleanup);
            }
        }

        if (failures.Count == 1)
            throw mismatch;
        throw new AggregateException(message, failures);
    }

    private void ValidateLimits(
        MapRenderOpenGlNormalCameraDepthStencilTargetKey key)
    {
        if (!_capabilities.SupportsSampleCount(key.HostSampleCount))
        {
            throw new NotSupportedException(
                $"The current GL context cannot allocate a combined RGBA8+D24S8 framebuffer with {key.HostSampleCount} sample(s).");
        }
        if (key.HostStorageWidth > _capabilities.MaximumTextureSize ||
            key.HostStorageHeight > _capabilities.MaximumTextureSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(key),
                $"Host depth/stencil storage {key.HostStorageWidth}x{key.HostStorageHeight} exceeds GL_MAX_TEXTURE_SIZE {_capabilities.MaximumTextureSize}.");
        }
        if (!key.HostStorageFootprintMatchesPs3Backing)
        {
            throw new InvalidOperationException(
                "The host depth/stencil sample footprint does not match the PS3 backing allocation arithmetic.");
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
            "OpenGL normal-camera depth/stencil allocator context identity changed after cache creation.");
        if (_allocator.Capabilities != _capabilities)
        {
            throw new InvalidOperationException(
                "OpenGL normal-camera depth/stencil allocator capabilities changed after cache creation.");
        }
    }
}
