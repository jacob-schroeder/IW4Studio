using System.Diagnostics.CodeAnalysis;
using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl.Shadows;

/// <summary>Owned context-local comparison-depth objects.</summary>
internal sealed record MapRenderOpenGlSunShadowAtlasResource
{
    internal MapRenderOpenGlSunShadowAtlasResource(
        MapRenderOpenGlSunShadowAtlasPlan plan,
        uint depthTextureHandle,
        uint framebufferHandle,
        uint comparisonSamplerHandle)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (depthTextureHandle == 0)
            throw new ArgumentOutOfRangeException(nameof(depthTextureHandle));
        if (framebufferHandle == 0)
            throw new ArgumentOutOfRangeException(nameof(framebufferHandle));
        if (comparisonSamplerHandle == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(comparisonSamplerHandle));
        }

        Plan = plan;
        DepthTextureHandle = depthTextureHandle;
        FramebufferHandle = framebufferHandle;
        ComparisonSamplerHandle = comparisonSamplerHandle;
    }

    public MapRenderOpenGlSunShadowAtlasPlan Plan { get; }

    public uint DepthTextureHandle { get; }

    public uint FramebufferHandle { get; }

    public uint ComparisonSamplerHandle { get; }
}

/// <summary>
/// Write-only target exposed while one shadow partition is active. The depth
/// texture handle here is not a receiver-readiness publication.
/// </summary>
public sealed record MapRenderOpenGlSunShadowPartitionWriteTarget
{
    internal MapRenderOpenGlSunShadowPartitionWriteTarget(
        long frameRevision,
        MapRenderOpenGlSunShadowAtlasTile tile,
        uint framebufferHandle,
        uint depthTextureHandle)
    {
        if (frameRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(frameRevision));
        ArgumentNullException.ThrowIfNull(tile);
        if (framebufferHandle == 0)
            throw new ArgumentOutOfRangeException(nameof(framebufferHandle));
        if (depthTextureHandle == 0)
            throw new ArgumentOutOfRangeException(nameof(depthTextureHandle));

        FrameRevision = frameRevision;
        Tile = tile;
        FramebufferHandle = framebufferHandle;
        DepthTextureHandle = depthTextureHandle;
    }

    public long FrameRevision { get; }

    public MapRenderOpenGlSunShadowAtlasTile Tile { get; }

    public MapRenderOpenGlSunShadowAtlasPartition Partition => Tile.Partition;

    public uint FramebufferHandle { get; }

    public uint DepthTextureHandle { get; }
}

/// <summary>
/// Same-revision receiver publication. This object is created only after both
/// atlas partition scopes complete successfully.
/// </summary>
public sealed record MapRenderOpenGlSunShadowAtlasReadyFrame
{
    internal MapRenderOpenGlSunShadowAtlasReadyFrame(
        long frameRevision,
        MapRenderOpenGlSunShadowAtlasPlan plan,
        uint depthTextureHandle,
        uint comparisonSamplerHandle)
    {
        if (frameRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(frameRevision));
        ArgumentNullException.ThrowIfNull(plan);
        if (depthTextureHandle == 0)
            throw new ArgumentOutOfRangeException(nameof(depthTextureHandle));
        if (comparisonSamplerHandle == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(comparisonSamplerHandle));
        }

        FrameRevision = frameRevision;
        Plan = plan;
        DepthTextureHandle = depthTextureHandle;
        ComparisonSamplerHandle = comparisonSamplerHandle;
    }

    public long FrameRevision { get; }

    public MapRenderOpenGlSunShadowAtlasPlan Plan { get; }

    public uint DepthTextureHandle { get; }

    public uint ComparisonSamplerHandle { get; }
}

/// <summary>
/// One active depth-write partition. Dispose aborts publication; Complete is
/// the only operation that contributes the partition to frame readiness.
/// </summary>
public sealed class MapRenderOpenGlSunShadowAtlasPartitionScope : IDisposable
{
    private MapRenderOpenGlSunShadowAtlasBackend? _owner;
    private bool _completed;

    internal MapRenderOpenGlSunShadowAtlasPartitionScope(
        MapRenderOpenGlSunShadowAtlasBackend owner,
        long sequence,
        MapRenderOpenGlSunShadowPartitionWriteTarget writeTarget)
    {
        _owner = owner;
        Sequence = sequence;
        WriteTarget = writeTarget;
    }

    internal long Sequence { get; }

    public MapRenderOpenGlSunShadowPartitionWriteTarget WriteTarget { get; }

    public bool IsEnded => _owner is null;

    public bool CompletedSuccessfully => _completed;

    public void Complete()
    {
        MapRenderOpenGlSunShadowAtlasBackend owner = _owner ??
            throw new InvalidOperationException(
                "The sun-shadow partition scope has already ended.");
        owner.EndPartition(this, completed: true);
        _completed = true;
        _owner = null;
    }

    public void Dispose()
    {
        MapRenderOpenGlSunShadowAtlasBackend? owner = _owner;
        if (owner is null)
            return;

        owner.EndPartition(this, completed: false);
        _owner = null;
    }

    internal void DetachFromDisposedOwner()
    {
        _owner = null;
    }
}

/// <summary>
/// Context/thread-owned OpenGL backend for the two-tile sun-shadow
/// atlas. It owns allocation, depth-tile entry/clear, all-or-nothing frame
/// readiness, comparison-sampler binding, and cleanup. Caster admission,
/// caster bias, projection constants, and material selection are outside this
/// bounded contract.
/// </summary>
public sealed class MapRenderOpenGlSunShadowAtlasBackend : IDisposable
{
    private const int CompletePartitionMask = 0b11;

    private readonly IMapRenderOpenGlSunShadowAtlasApi _api;
    private readonly GlResourceCacheScope _scope;
    private readonly MapRenderOpenGlSunShadowAtlasResource _resource;
    private long _lastStartedFrameRevision = -1;
    private long _activeFrameRevision = -1;
    private long _nextScopeSequence;
    private int _completedPartitionMask;
    private MapRenderOpenGlSunShadowAtlasPartitionScope? _activePartition;
    private MapRenderOpenGlSunShadowAtlasReadyFrame? _readyFrame;

    public MapRenderOpenGlSunShadowAtlasBackend(
        GL gl,
        string contextIdentity)
        : this(
            new SilkMapRenderOpenGlSunShadowAtlasApi(gl, contextIdentity),
            MapRenderOpenGlSunShadowAtlasPlanner.CreatePs3Normal())
    {
    }

    internal MapRenderOpenGlSunShadowAtlasBackend(
        GL gl,
        SilkOpenGlStateShadow state,
        string contextIdentity)
        : this(
            new SilkMapRenderOpenGlSunShadowAtlasApi(
                gl,
                contextIdentity,
                state),
            MapRenderOpenGlSunShadowAtlasPlanner.CreatePs3Normal())
    {
    }

    internal MapRenderOpenGlSunShadowAtlasBackend(
        IMapRenderOpenGlSunShadowAtlasApi api,
        MapRenderOpenGlSunShadowAtlasPlan plan)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(api.ContextIdentity);
        ArgumentNullException.ThrowIfNull(api.Capabilities);
        if (!api.Capabilities.Supports(plan))
        {
            throw new NotSupportedException(
                $"The current GL context cannot allocate the {plan.Width}x{plan.Height} comparison-depth sun-shadow atlas (GL_MAX_TEXTURE_SIZE={api.Capabilities.MaximumTextureSize}).");
        }

        _api = api;
        Plan = plan;
        _scope = new GlResourceCacheScope(
            api.ContextIdentity,
            "OpenGL sun-shadow atlas may only be used and disposed on its owning render thread.");
        _resource = Allocate(api, plan);
    }

    public MapRenderOpenGlSunShadowAtlasPlan Plan { get; }

    public string ContextIdentity
    {
        get
        {
            EnsureUsable();
            return _scope.ContextIdentity;
        }
    }

    public long? ActiveFrameRevision
    {
        get
        {
            EnsureUsable();
            return _activeFrameRevision >= 0 ? _activeFrameRevision : null;
        }
    }

    public bool HasActivePartition
    {
        get
        {
            EnsureUsable();
            return _activePartition is not null;
        }
    }

    public bool IsCurrentFrameReady
    {
        get
        {
            EnsureUsable();
            return _readyFrame is not null;
        }
    }

    /// <summary>
    /// Begins a new monotonically increasing atlas revision. Any incomplete or
    /// previously published revision is invalidated before partition work.
    /// </summary>
    public void BeginFrame(long frameRevision)
    {
        EnsureUsable();
        if (frameRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(frameRevision));
        if (_activePartition is not null)
        {
            throw new InvalidOperationException(
                "An active sun-shadow partition must end before a new frame begins.");
        }
        if (frameRevision <= _lastStartedFrameRevision)
        {
            throw new InvalidOperationException(
                $"Sun-shadow frame revision {frameRevision} is not newer than {_lastStartedFrameRevision}.");
        }

        _lastStartedFrameRevision = frameRevision;
        _activeFrameRevision = frameRevision;
        _completedPartitionMask = 0;
        _readyFrame = null;
    }

    /// <summary>
    /// Publishes the already-complete depth contents of this persistent atlas
    /// for a newer renderer frame without binding, clearing, or writing the
    /// target. The caller must prove that every caster input and resource is
    /// identical to the frame that produced those contents.
    /// </summary>
    internal void BeginReusedFrame(long frameRevision)
    {
        EnsureUsable();
        if (frameRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(frameRevision));
        if (_activePartition is not null)
        {
            throw new InvalidOperationException(
                "An active sun-shadow partition must end before reusing an atlas frame.");
        }
        if (frameRevision <= _lastStartedFrameRevision)
        {
            throw new InvalidOperationException(
                $"Sun-shadow frame revision {frameRevision} is not newer than {_lastStartedFrameRevision}.");
        }

        _lastStartedFrameRevision = frameRevision;
        _activeFrameRevision = frameRevision;
        _completedPartitionMask = CompletePartitionMask;
        _readyFrame = new MapRenderOpenGlSunShadowAtlasReadyFrame(
            frameRevision,
            Plan,
            _resource.DepthTextureHandle,
            _resource.ComparisonSamplerHandle);
    }

    /// <summary>
    /// Binds and depth-clears one exact 1024x1024 vertical tile. Draw-time GL
    /// mutations are adopted by the renderer state shadow when supplied.
    /// </summary>
    public MapRenderOpenGlSunShadowAtlasPartitionScope BeginPartition(
        MapRenderOpenGlSunShadowAtlasPartition partition)
    {
        EnsureUsable();
        if (!Enum.IsDefined(partition))
            throw new ArgumentOutOfRangeException(nameof(partition));
        if (_activeFrameRevision < 0)
        {
            throw new InvalidOperationException(
                "BeginFrame must precede a sun-shadow partition.");
        }
        if (_activePartition is not null)
        {
            throw new InvalidOperationException(
                "Only one sun-shadow partition may be active at a time.");
        }

        int bit = 1 << (int)partition;
        if ((_completedPartitionMask & bit) != 0)
        {
            throw new InvalidOperationException(
                $"Sun-shadow {partition} already completed for frame {_activeFrameRevision}.");
        }

        MapRenderOpenGlSunShadowAtlasTile tile = Plan.GetTile(partition);
        _api.BindDrawFramebufferForPartition(_resource.FramebufferHandle);
        _api.Viewport(tile.X, tile.Y, tile.Width, tile.Height);
        _api.Scissor(tile.X, tile.Y, tile.Width, tile.Height);
        _api.SetScissorTestEnabled(true);
        _api.DepthMask(true);
        _api.ClearDepth(1.0);
        _api.ClearDepthBuffer();

        var writeTarget = new MapRenderOpenGlSunShadowPartitionWriteTarget(
            _activeFrameRevision,
            tile,
            _resource.FramebufferHandle,
            _resource.DepthTextureHandle);
        var scope = new MapRenderOpenGlSunShadowAtlasPartitionScope(
            this,
            checked(++_nextScopeSequence),
            writeTarget);
        _activePartition = scope;
        return scope;
    }

    public bool TryGetReadyFrame(
        long frameRevision,
        [NotNullWhen(true)]
        out MapRenderOpenGlSunShadowAtlasReadyFrame? readyFrame)
    {
        EnsureUsable();
        if (_readyFrame is not null &&
            _readyFrame.FrameRevision == frameRevision)
        {
            readyFrame = _readyFrame;
            return true;
        }

        readyFrame = null;
        return false;
    }

    /// <summary>
    /// Binds raw code sampler 6's host texture/sampler pair only when the
    /// supplied publication is the backend's complete current revision.
    /// </summary>
    public void BindReadyReceiver(
        MapRenderOpenGlSunShadowAtlasReadyFrame readyFrame,
        int textureUnit)
    {
        EnsureUsable();
        ArgumentNullException.ThrowIfNull(readyFrame);
        if (textureUnit < 0)
            throw new ArgumentOutOfRangeException(nameof(textureUnit));
        if (!ReferenceEquals(readyFrame, _readyFrame) ||
            readyFrame.FrameRevision != _activeFrameRevision ||
            _completedPartitionMask != CompletePartitionMask ||
            _activePartition is not null)
        {
            throw new InvalidOperationException(
                "Only the complete current sun-shadow frame may be bound to a receiver.");
        }

        _api.BindReadyReceiver(
            textureUnit,
            readyFrame.DepthTextureHandle,
            readyFrame.ComparisonSamplerHandle);
    }

    public void Dispose()
    {
        EnsureApiIdentity();
        if (!_scope.BeginDispose())
            return;

        _activePartition?.DetachFromDisposedOwner();
        _activePartition = null;
        _readyFrame = null;
        _activeFrameRevision = -1;

        List<Exception>? failures = null;
        TryDelete(() => _api.DeleteFramebuffer(_resource.FramebufferHandle));
        TryDelete(() =>
            _api.DeleteSampler(_resource.ComparisonSamplerHandle));
        TryDelete(() => _api.DeleteTexture(_resource.DepthTextureHandle));
        if (failures is not null)
        {
            throw new AggregateException(
                "One or more OpenGL sun-shadow atlas objects could not be deleted.",
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

    internal void EndPartition(
        MapRenderOpenGlSunShadowAtlasPartitionScope scope,
        bool completed)
    {
        EnsureUsable();
        ArgumentNullException.ThrowIfNull(scope);
        if (!ReferenceEquals(scope, _activePartition) ||
            scope.WriteTarget.FrameRevision != _activeFrameRevision)
        {
            throw new InvalidOperationException(
                "The supplied sun-shadow partition scope is not the active current-frame scope.");
        }

        _activePartition = null;
        if (!completed)
            return;

        _completedPartitionMask |= 1 << (int)scope.WriteTarget.Partition;
        if (_completedPartitionMask != CompletePartitionMask)
            return;

        _readyFrame = new MapRenderOpenGlSunShadowAtlasReadyFrame(
            _activeFrameRevision,
            Plan,
            _resource.DepthTextureHandle,
            _resource.ComparisonSamplerHandle);
    }

    private static MapRenderOpenGlSunShadowAtlasResource Allocate(
        IMapRenderOpenGlSunShadowAtlasApi api,
        MapRenderOpenGlSunShadowAtlasPlan plan)
    {
        uint previousTexture = api.GetBoundTexture2D();
        uint previousDrawFramebuffer = api.GetBoundDrawFramebuffer();
        uint previousReadFramebuffer = api.GetBoundReadFramebuffer();
        uint texture = 0;
        uint framebuffer = 0;
        uint sampler = 0;
        List<Exception>? failures = null;
        try
        {
            texture = api.CreateTexture();
            if (texture == 0)
            {
                throw new InvalidOperationException(
                    "OpenGL returned sun-shadow depth texture handle zero.");
            }
            api.BindTexture2DForAllocation(texture);
            api.AllocateDepthComponent24LevelZero(plan.Width, plan.Height);
            api.SetTextureMipLevelRange(0, 0);

            framebuffer = api.CreateFramebuffer();
            if (framebuffer == 0)
            {
                throw new InvalidOperationException(
                    "OpenGL returned sun-shadow framebuffer handle zero.");
            }
            api.BindDrawFramebufferForAllocation(framebuffer);
            api.BindReadFramebufferForAllocation(framebuffer);
            api.AttachDepthTexture2D(texture);
            api.SelectDrawNone();
            api.SelectReadNone();
            if (!api.IsDrawFramebufferComplete())
            {
                throw new InvalidOperationException(
                    "The OpenGL sun-shadow depth-only framebuffer is incomplete.");
            }

            sampler = api.CreateSampler();
            if (sampler == 0)
            {
                throw new InvalidOperationException(
                    "OpenGL returned sun-shadow comparison sampler handle zero.");
            }
            api.ConfigureLinearClampComparisonLessSampler(sampler);
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }
        finally
        {
            Restore(() => api.BindTexture2DForAllocation(previousTexture));
            Restore(() => api.BindDrawFramebufferForAllocation(
                previousDrawFramebuffer));
            Restore(() => api.BindReadFramebufferForAllocation(
                previousReadFramebuffer));
        }

        if (failures is not null)
        {
            DeletePartial(
                sampler,
                api.DeleteSampler,
                "sampler");
            DeletePartial(
                framebuffer,
                api.DeleteFramebuffer,
                "framebuffer");
            DeletePartial(
                texture,
                api.DeleteTexture,
                "texture");
            throw new AggregateException(
                "OpenGL sun-shadow atlas allocation failed; partial owned objects were deleted when possible.",
                failures);
        }

        return new MapRenderOpenGlSunShadowAtlasResource(
            plan,
            texture,
            framebuffer,
            sampler);

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

        void DeletePartial(
            uint handle,
            Action<uint> delete,
            string objectType)
        {
            if (handle == 0)
                return;
            try
            {
                delete(handle);
            }
            catch (Exception exception)
            {
                failures!.Add(new AggregateException(
                    $"The partial sun-shadow {objectType} could not be deleted.",
                    exception));
            }
        }
    }

    private void EnsureUsable()
    {
        _scope.EnsureUsable(this);
        EnsureApiIdentity();
    }

    private void EnsureApiIdentity()
    {
        _scope.EnsureContextIdentity(
            _api.ContextIdentity,
            "OpenGL sun-shadow atlas API context identity changed after allocation.");
    }
}
