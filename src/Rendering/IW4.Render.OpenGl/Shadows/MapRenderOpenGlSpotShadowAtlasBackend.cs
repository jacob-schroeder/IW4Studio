using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl.Shadows;

/// <summary>One native 512x512 tile inside the normal PS3 spot-shadow atlas.</summary>
internal sealed record MapRenderOpenGlSpotShadowAtlasTile
{
    internal MapRenderOpenGlSpotShadowAtlasTile(int index)
    {
        if (index is < 0 or >= MapRenderOpenGlSpotShadowAtlasBackend.TileCount)
            throw new ArgumentOutOfRangeException(nameof(index));

        Index = index;
        X = 0;
        Y = checked(index * MapRenderOpenGlSpotShadowAtlasBackend.TileSize);
        Width = MapRenderOpenGlSpotShadowAtlasBackend.TileSize;
        Height = MapRenderOpenGlSpotShadowAtlasBackend.TileSize;
    }

    internal int Index { get; }

    internal int X { get; }

    internal int Y { get; }

    internal int Width { get; }

    internal int Height { get; }
}

/// <summary>
/// Caller-owned semantic identity for one selected spot light. The backend
/// snapshots these descriptors when a frame begins and publishes them only
/// after every declared tile completes.
/// </summary>
internal sealed record MapRenderOpenGlSpotShadowEntryDescriptor
{
    internal MapRenderOpenGlSpotShadowEntryDescriptor(
        int sceneLightIndex,
        int tileIndex,
        Matrix4x4 lookupMatrix,
        float fade)
    {
        if (sceneLightIndex <= 0)
            throw new ArgumentOutOfRangeException(nameof(sceneLightIndex));
        if (tileIndex is < 0 or >= MapRenderOpenGlSpotShadowAtlasBackend.TileCount)
            throw new ArgumentOutOfRangeException(nameof(tileIndex));
        if (!IsFinite(lookupMatrix))
        {
            throw new ArgumentException(
                "The spot-shadow lookup matrix must be finite.",
                nameof(lookupMatrix));
        }
        if (!float.IsFinite(fade) || fade is < 0f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(fade));

        SceneLightIndex = sceneLightIndex;
        TileIndex = tileIndex;
        LookupMatrix = lookupMatrix;
        Fade = fade;
    }

    internal int SceneLightIndex { get; }

    internal int TileIndex { get; }

    internal Matrix4x4 LookupMatrix { get; }

    internal float Fade { get; }

    private static bool IsFinite(in Matrix4x4 value) =>
        float.IsFinite(value.M11) &&
        float.IsFinite(value.M12) &&
        float.IsFinite(value.M13) &&
        float.IsFinite(value.M14) &&
        float.IsFinite(value.M21) &&
        float.IsFinite(value.M22) &&
        float.IsFinite(value.M23) &&
        float.IsFinite(value.M24) &&
        float.IsFinite(value.M31) &&
        float.IsFinite(value.M32) &&
        float.IsFinite(value.M33) &&
        float.IsFinite(value.M34) &&
        float.IsFinite(value.M41) &&
        float.IsFinite(value.M42) &&
        float.IsFinite(value.M43) &&
        float.IsFinite(value.M44);
}

/// <summary>
/// Write-only target for one active tile. Its texture handle is not a
/// receiver-readiness publication.
/// </summary>
internal sealed record MapRenderOpenGlSpotShadowTileWriteTarget
{
    internal MapRenderOpenGlSpotShadowTileWriteTarget(
        long frameRevision,
        MapRenderOpenGlSpotShadowEntryDescriptor descriptor,
        MapRenderOpenGlSpotShadowAtlasTile tile,
        uint framebufferHandle,
        uint depthTextureHandle)
    {
        if (frameRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(frameRevision));
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(tile);
        if (descriptor.TileIndex != tile.Index)
        {
            throw new ArgumentException(
                "The spot-shadow descriptor and physical tile must agree.",
                nameof(tile));
        }
        if (framebufferHandle == 0)
            throw new ArgumentOutOfRangeException(nameof(framebufferHandle));
        if (depthTextureHandle == 0)
            throw new ArgumentOutOfRangeException(nameof(depthTextureHandle));

        FrameRevision = frameRevision;
        Descriptor = descriptor;
        Tile = tile;
        FramebufferHandle = framebufferHandle;
        DepthTextureHandle = depthTextureHandle;
    }

    internal long FrameRevision { get; }

    internal MapRenderOpenGlSpotShadowEntryDescriptor Descriptor { get; }

    internal MapRenderOpenGlSpotShadowAtlasTile Tile { get; }

    internal uint FramebufferHandle { get; }

    internal uint DepthTextureHandle { get; }
}

/// <summary>One completed same-revision receiver entry.</summary>
internal sealed record MapRenderOpenGlSpotShadowReadyEntry
{
    internal MapRenderOpenGlSpotShadowReadyEntry(
        long frameRevision,
        MapRenderOpenGlSpotShadowEntryDescriptor descriptor,
        uint depthTextureHandle,
        uint comparisonSamplerHandle)
    {
        if (frameRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(frameRevision));
        ArgumentNullException.ThrowIfNull(descriptor);
        if (depthTextureHandle == 0)
            throw new ArgumentOutOfRangeException(nameof(depthTextureHandle));
        if (comparisonSamplerHandle == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(comparisonSamplerHandle));
        }

        FrameRevision = frameRevision;
        SceneLightIndex = descriptor.SceneLightIndex;
        TileIndex = descriptor.TileIndex;
        LookupMatrix = descriptor.LookupMatrix;
        Fade = descriptor.Fade;
        DepthTextureHandle = depthTextureHandle;
        ComparisonSamplerHandle = comparisonSamplerHandle;
    }

    internal long FrameRevision { get; }

    internal int SceneLightIndex { get; }

    internal int TileIndex { get; }

    internal Matrix4x4 LookupMatrix { get; }

    internal float Fade { get; }

    internal uint DepthTextureHandle { get; }

    internal uint ComparisonSamplerHandle { get; }
}

/// <summary>
/// All-or-nothing same-revision publication for the selected spot lights.
/// </summary>
internal sealed class MapRenderOpenGlSpotShadowAtlasReadyFrame
{
    private readonly IReadOnlyDictionary<int,
        MapRenderOpenGlSpotShadowReadyEntry> _entriesBySceneLightIndex;

    internal MapRenderOpenGlSpotShadowAtlasReadyFrame(
        long frameRevision,
        IReadOnlyList<MapRenderOpenGlSpotShadowEntryDescriptor> descriptors,
        uint depthTextureHandle,
        uint comparisonSamplerHandle)
    {
        if (frameRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(frameRevision));
        ArgumentNullException.ThrowIfNull(descriptors);
        if (descriptors.Count is < 1 or > MapRenderOpenGlSpotShadowAtlasBackend.TileCount)
            throw new ArgumentOutOfRangeException(nameof(descriptors));
        if (depthTextureHandle == 0)
            throw new ArgumentOutOfRangeException(nameof(depthTextureHandle));
        if (comparisonSamplerHandle == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(comparisonSamplerHandle));
        }

        var entries = new SortedDictionary<int,
            MapRenderOpenGlSpotShadowReadyEntry>();
        foreach (MapRenderOpenGlSpotShadowEntryDescriptor descriptor in descriptors)
        {
            if (!entries.TryAdd(
                    descriptor.SceneLightIndex,
                    new MapRenderOpenGlSpotShadowReadyEntry(
                        frameRevision,
                        descriptor,
                        depthTextureHandle,
                        comparisonSamplerHandle)))
            {
                throw new ArgumentException(
                    $"Scene light {descriptor.SceneLightIndex} is duplicated in the spot-shadow frame.",
                    nameof(descriptors));
            }
        }

        FrameRevision = frameRevision;
        DepthTextureHandle = depthTextureHandle;
        ComparisonSamplerHandle = comparisonSamplerHandle;
        _entriesBySceneLightIndex = new ReadOnlyDictionary<int,
            MapRenderOpenGlSpotShadowReadyEntry>(entries);
    }

    internal long FrameRevision { get; }

    internal uint DepthTextureHandle { get; }

    internal uint ComparisonSamplerHandle { get; }

    internal IReadOnlyDictionary<int, MapRenderOpenGlSpotShadowReadyEntry>
        EntriesBySceneLightIndex => _entriesBySceneLightIndex;

    internal bool TryGetEntry(
        int sceneLightIndex,
        [NotNullWhen(true)] out MapRenderOpenGlSpotShadowReadyEntry? entry) =>
        _entriesBySceneLightIndex.TryGetValue(sceneLightIndex, out entry);
}

/// <summary>
/// One active depth-write tile. Dispose aborts that tile; Complete is the only
/// operation that contributes it to frame readiness.
/// </summary>
internal sealed class MapRenderOpenGlSpotShadowAtlasTileScope : IDisposable
{
    private MapRenderOpenGlSpotShadowAtlasBackend? _owner;
    private bool _completed;

    internal MapRenderOpenGlSpotShadowAtlasTileScope(
        MapRenderOpenGlSpotShadowAtlasBackend owner,
        long sequence,
        MapRenderOpenGlSpotShadowTileWriteTarget writeTarget)
    {
        _owner = owner;
        Sequence = sequence;
        WriteTarget = writeTarget;
    }

    internal long Sequence { get; }

    internal MapRenderOpenGlSpotShadowTileWriteTarget WriteTarget { get; }

    internal bool IsEnded => _owner is null;

    internal bool CompletedSuccessfully => _completed;

    internal void Complete()
    {
        MapRenderOpenGlSpotShadowAtlasBackend owner = _owner ??
            throw new InvalidOperationException(
                "The spot-shadow tile scope has already ended.");
        owner.EndTile(this, completed: true);
        _completed = true;
        _owner = null;
    }

    public void Dispose()
    {
        MapRenderOpenGlSpotShadowAtlasBackend? owner = _owner;
        if (owner is null)
            return;

        owner.EndTile(this, completed: false);
        _owner = null;
    }

    internal void DetachFromDisposedOwner() => _owner = null;
}

/// <summary>
/// Context/thread-owned OpenGL backend for the normal four-tile PS3
/// spot-shadow atlas. Selection, caster admission, matrices, fade, and caster
/// bias remain renderer responsibilities.
/// </summary>
internal sealed class MapRenderOpenGlSpotShadowAtlasBackend : IDisposable
{
    internal const int Width = 512;
    internal const int Height = 2048;
    internal const int TileSize = 512;
    internal const int TileCount = 4;

    private readonly IMapRenderOpenGlSunShadowAtlasApi _api;
    private readonly GlResourceCacheScope _scope;
    private readonly uint _depthTextureHandle;
    private readonly uint _framebufferHandle;
    private readonly uint _comparisonSamplerHandle;
    private IReadOnlyList<MapRenderOpenGlSpotShadowEntryDescriptor>
        _activeDescriptors = Array.Empty<MapRenderOpenGlSpotShadowEntryDescriptor>();
    private long _lastStartedFrameRevision = -1;
    private long _activeFrameRevision = -1;
    private long _nextScopeSequence;
    private int _expectedTileMask;
    private int _completedTileMask;
    private MapRenderOpenGlSpotShadowAtlasTileScope? _activeTile;
    private MapRenderOpenGlSpotShadowAtlasReadyFrame? _readyFrame;

    internal MapRenderOpenGlSpotShadowAtlasBackend(
        GL gl,
        SilkOpenGlStateShadow state,
        string contextIdentity)
        : this(
            new SilkMapRenderOpenGlSunShadowAtlasApi(
                gl,
                contextIdentity,
                state))
    {
    }

    internal MapRenderOpenGlSpotShadowAtlasBackend(
        IMapRenderOpenGlSunShadowAtlasApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentException.ThrowIfNullOrWhiteSpace(api.ContextIdentity);
        ArgumentNullException.ThrowIfNull(api.Capabilities);
        if (!api.Capabilities.SupportsComparisonDepthStencilAtlas ||
            Width > api.Capabilities.MaximumTextureSize ||
            Height > api.Capabilities.MaximumTextureSize)
        {
            throw new NotSupportedException(
                $"The current GL context cannot allocate the {Width}x{Height} D24S8 comparison-depth spot-shadow atlas (GL_MAX_TEXTURE_SIZE={api.Capabilities.MaximumTextureSize}).");
        }

        _api = api;
        _scope = new GlResourceCacheScope(
            api.ContextIdentity,
            "OpenGL spot-shadow atlas may only be used and disposed on its owning render thread.");
        (_depthTextureHandle, _framebufferHandle, _comparisonSamplerHandle) =
            Allocate(api);
    }

    internal string ContextIdentity
    {
        get
        {
            EnsureUsable();
            return _scope.ContextIdentity;
        }
    }

    internal long? ActiveFrameRevision
    {
        get
        {
            EnsureUsable();
            return _activeFrameRevision >= 0 ? _activeFrameRevision : null;
        }
    }

    internal bool HasActiveTile
    {
        get
        {
            EnsureUsable();
            return _activeTile is not null;
        }
    }

    internal bool IsCurrentFrameReady
    {
        get
        {
            EnsureUsable();
            return _readyFrame is not null;
        }
    }

    /// <summary>
    /// Begins a monotonically increasing frame and invalidates any older
    /// publication. Descriptors must be in native tile order 0..count-1.
    /// An empty selection deliberately produces no ready frame.
    /// </summary>
    internal void BeginFrame(
        long frameRevision,
        IReadOnlyList<MapRenderOpenGlSpotShadowEntryDescriptor> descriptors)
    {
        BeginFrame(
            frameRevision,
            descriptors,
            reuseCompletedContents: false);
    }

    /// <summary>
    /// Publishes the already-complete depth contents of this persistent atlas
    /// for a newer renderer frame without binding, clearing, or writing any
    /// tile. The caller must prove that every selected light, caster input,
    /// and resource is identical to the frame that produced those contents.
    /// </summary>
    internal void BeginReusedFrame(
        long frameRevision,
        IReadOnlyList<MapRenderOpenGlSpotShadowEntryDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        if (descriptors.Count == 0)
        {
            throw new ArgumentException(
                "A reused spot-shadow frame must publish at least one completed tile.",
                nameof(descriptors));
        }
        BeginFrame(
            frameRevision,
            descriptors,
            reuseCompletedContents: true);
    }

    private void BeginFrame(
        long frameRevision,
        IReadOnlyList<MapRenderOpenGlSpotShadowEntryDescriptor> descriptors,
        bool reuseCompletedContents)
    {
        EnsureUsable();
        if (frameRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(frameRevision));
        ArgumentNullException.ThrowIfNull(descriptors);
        if (descriptors.Count > TileCount)
            throw new ArgumentOutOfRangeException(nameof(descriptors));
        if (_activeTile is not null)
        {
            throw new InvalidOperationException(
                "An active spot-shadow tile must end before a new frame begins.");
        }
        if (frameRevision <= _lastStartedFrameRevision)
        {
            throw new InvalidOperationException(
                $"Spot-shadow frame revision {frameRevision} is not newer than {_lastStartedFrameRevision}.");
        }

        var snapshot = new MapRenderOpenGlSpotShadowEntryDescriptor[
            descriptors.Count];
        var sceneLights = new HashSet<int>();
        for (int index = 0; index < descriptors.Count; index++)
        {
            MapRenderOpenGlSpotShadowEntryDescriptor descriptor =
                descriptors[index] ?? throw new ArgumentException(
                    "Spot-shadow descriptors cannot contain null entries.",
                    nameof(descriptors));
            if (descriptor.TileIndex != index)
            {
                throw new ArgumentException(
                    "Spot-shadow descriptors must occupy contiguous native tiles 0 through count-1 in exact order.",
                    nameof(descriptors));
            }
            if (!sceneLights.Add(descriptor.SceneLightIndex))
            {
                throw new ArgumentException(
                    $"Scene light {descriptor.SceneLightIndex} is duplicated in the spot-shadow frame.",
                    nameof(descriptors));
            }

            snapshot[index] = descriptor;
        }

        _lastStartedFrameRevision = frameRevision;
        _activeFrameRevision = frameRevision;
        _activeDescriptors = Array.AsReadOnly(snapshot);
        _expectedTileMask = snapshot.Length == 0
            ? 0
            : (1 << snapshot.Length) - 1;
        _completedTileMask = reuseCompletedContents
            ? _expectedTileMask
            : 0;
        _readyFrame = reuseCompletedContents
            ? new MapRenderOpenGlSpotShadowAtlasReadyFrame(
                frameRevision,
                _activeDescriptors,
                _depthTextureHandle,
                _comparisonSamplerHandle)
            : null;
    }

    /// <summary>
    /// Binds and clears one exact 512x512 vertical tile. Draw-time state goes
    /// through the renderer state shadow supplied at construction.
    /// </summary>
    internal MapRenderOpenGlSpotShadowAtlasTileScope BeginTile(int tileIndex)
    {
        EnsureUsable();
        if (_activeFrameRevision < 0)
            throw new InvalidOperationException(
                "BeginFrame must precede a spot-shadow tile.");
        if (_activeTile is not null)
        {
            throw new InvalidOperationException(
                "Only one spot-shadow tile may be active at a time.");
        }
        if (tileIndex < 0 || tileIndex >= _activeDescriptors.Count)
            throw new ArgumentOutOfRangeException(nameof(tileIndex));

        int bit = 1 << tileIndex;
        if ((_completedTileMask & bit) != 0)
        {
            throw new InvalidOperationException(
                $"Spot-shadow tile {tileIndex} already completed for frame {_activeFrameRevision}.");
        }

        var tile = new MapRenderOpenGlSpotShadowAtlasTile(tileIndex);
        _api.BindDrawFramebufferForPartition(_framebufferHandle);
        _api.Viewport(tile.X, tile.Y, tile.Width, tile.Height);
        _api.Scissor(tile.X, tile.Y, tile.Width, tile.Height);
        _api.SetScissorTestEnabled(true);
        _api.DepthMask(true);
        _api.StencilMask(uint.MaxValue);
        _api.ClearDepth(1.0);
        _api.ClearStencil(0);
        _api.ClearDepthStencilBuffer();

        var writeTarget = new MapRenderOpenGlSpotShadowTileWriteTarget(
            _activeFrameRevision,
            _activeDescriptors[tileIndex],
            tile,
            _framebufferHandle,
            _depthTextureHandle);
        var scope = new MapRenderOpenGlSpotShadowAtlasTileScope(
            this,
            checked(++_nextScopeSequence),
            writeTarget);
        _activeTile = scope;
        return scope;
    }

    internal bool TryGetReadyFrame(
        long frameRevision,
        [NotNullWhen(true)]
        out MapRenderOpenGlSpotShadowAtlasReadyFrame? readyFrame)
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

    internal void BindReadyReceiver(
        MapRenderOpenGlSpotShadowAtlasReadyFrame readyFrame,
        int sceneLightIndex,
        int textureUnit)
    {
        EnsureUsable();
        ArgumentNullException.ThrowIfNull(readyFrame);
        if (sceneLightIndex <= 0)
            throw new ArgumentOutOfRangeException(nameof(sceneLightIndex));
        if (textureUnit < 0)
            throw new ArgumentOutOfRangeException(nameof(textureUnit));
        if (!ReferenceEquals(readyFrame, _readyFrame) ||
            readyFrame.FrameRevision != _activeFrameRevision ||
            _expectedTileMask == 0 ||
            _completedTileMask != _expectedTileMask ||
            _activeTile is not null ||
            !readyFrame.TryGetEntry(sceneLightIndex, out _))
        {
            throw new InvalidOperationException(
                "Only a selected light in the complete current spot-shadow frame may be bound to a receiver.");
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

        _activeTile?.DetachFromDisposedOwner();
        _activeTile = null;
        _activeDescriptors = Array.Empty<MapRenderOpenGlSpotShadowEntryDescriptor>();
        _readyFrame = null;
        _activeFrameRevision = -1;

        List<Exception>? failures = null;
        TryDelete(() => _api.DeleteFramebuffer(_framebufferHandle));
        TryDelete(() => _api.DeleteSampler(_comparisonSamplerHandle));
        TryDelete(() => _api.DeleteTexture(_depthTextureHandle));
        if (failures is not null)
        {
            throw new AggregateException(
                "One or more OpenGL spot-shadow atlas objects could not be deleted.",
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

    internal void EndTile(
        MapRenderOpenGlSpotShadowAtlasTileScope scope,
        bool completed)
    {
        EnsureUsable();
        ArgumentNullException.ThrowIfNull(scope);
        if (!ReferenceEquals(scope, _activeTile) ||
            scope.WriteTarget.FrameRevision != _activeFrameRevision)
        {
            throw new InvalidOperationException(
                "The supplied spot-shadow tile scope is not the active current-frame scope.");
        }

        _activeTile = null;
        if (!completed)
            return;

        _completedTileMask |= 1 << scope.WriteTarget.Tile.Index;
        if (_expectedTileMask == 0 ||
            _completedTileMask != _expectedTileMask)
        {
            return;
        }

        _readyFrame = new MapRenderOpenGlSpotShadowAtlasReadyFrame(
            _activeFrameRevision,
            _activeDescriptors,
            _depthTextureHandle,
            _comparisonSamplerHandle);
    }

    private static (uint Texture, uint Framebuffer, uint Sampler) Allocate(
        IMapRenderOpenGlSunShadowAtlasApi api)
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
                    "OpenGL returned spot-shadow depth texture handle zero.");
            }
            api.BindTexture2DForAllocation(texture);
            api.AllocateDepth24Stencil8LevelZero(Width, Height);
            api.SetTextureMipLevelRange(0, 0);

            framebuffer = api.CreateFramebuffer();
            if (framebuffer == 0)
            {
                throw new InvalidOperationException(
                    "OpenGL returned spot-shadow framebuffer handle zero.");
            }
            api.BindDrawFramebufferForAllocation(framebuffer);
            api.BindReadFramebufferForAllocation(framebuffer);
            api.AttachDepthStencilTexture2D(texture);
            api.SelectDrawNone();
            api.SelectReadNone();
            if (!api.IsDrawFramebufferComplete())
            {
                throw new InvalidOperationException(
                    "The OpenGL spot-shadow depth-stencil framebuffer is incomplete.");
            }

            sampler = api.CreateSampler();
            if (sampler == 0)
            {
                throw new InvalidOperationException(
                    "OpenGL returned spot-shadow comparison sampler handle zero.");
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
            DeletePartial(sampler, api.DeleteSampler, "sampler");
            DeletePartial(framebuffer, api.DeleteFramebuffer, "framebuffer");
            DeletePartial(texture, api.DeleteTexture, "texture");
            throw new AggregateException(
                "OpenGL spot-shadow atlas allocation failed; partial owned objects were deleted when possible.",
                failures);
        }

        return (texture, framebuffer, sampler);

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
                    $"The partial spot-shadow {objectType} could not be deleted.",
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
            "OpenGL spot-shadow atlas API context identity changed after allocation.");
    }
}
