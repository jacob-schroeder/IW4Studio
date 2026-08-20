using System.Runtime.Versioning;

using IW4.Render.Resources;
using IW4.Render.Scheduling.FramePlans;
using IW4.Render.Textures;

using SharpMetal.Foundation;
using SharpMetal.Metal;

namespace IW4.Render.Metal.Resources;

/// <summary>
/// Owns the scene-lifetime Metal projection of a backend-neutral resource
/// snapshot. Loading is transactional: the previous scene remains valid until
/// every replacement upload has completed successfully.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MetalResourceCache : IDisposable
{
    private readonly MTLDevice _device;
    private readonly MTLCommandQueue _commandQueue;
    private readonly bool _deferTextureResidencyByDefault;
    private readonly HashSet<RenderSemanticIdentity>
        _deferredGeometryIdentities = [];
    private readonly HashSet<RenderSemanticIdentity>
        _deferredInstanceIdentities = [];
    private MetalResourceSet? _resources;
    private bool _disposed;

    internal MetalResourceCache(
        MTLDevice device,
        MTLCommandQueue commandQueue,
        bool deferTextureResidencyByDefault = false)
    {
        if (device.NativePtr == 0)
            throw new ArgumentException("A Metal device is required.", nameof(device));
        if (commandQueue.NativePtr == 0)
        {
            throw new ArgumentException(
                "A Metal command queue is required.",
                nameof(commandQueue));
        }

        _device = device;
        _commandQueue = commandQueue;
        _deferTextureResidencyByDefault =
            deferTextureResidencyByDefault;
    }

    internal bool IsLoaded => _resources is not null;

    internal string? ContentDigest => _resources?.ContentDigest;

    internal int GeometryCount => _resources?.GeometryCount ?? 0;

    internal int InstanceResourceCount => _resources?.InstanceCount ?? 0;

    internal int TextureCount => _resources?.TextureCount ?? 0;

    internal int ResidentTextureCount =>
        _resources?.ResidentTextureCount ?? 0;

    internal int SamplerCount => _resources?.SamplerCount ?? 0;

    internal long StaticBufferByteCount =>
        _resources?.StaticBufferByteCount ?? 0;

    internal long UploadedTextureByteCount =>
        _resources?.ResidentTextureByteCount ?? 0;

    internal long ResidentTextureByteCount =>
        _resources?.ResidentTextureByteCount ?? 0;

    internal long TextureAuthoredSourceByteCount =>
        _resources?.TextureAuthoredSourceByteCount ?? 0;

    internal long TextureDecodedFallbackRetainedByteCount =>
        _resources?.TextureDecodedFallbackRetainedByteCount ?? 0;

    internal int NativeSamplerStateCount =>
        _resources?.NativeSamplerStateCount ?? 0;

    /// <summary>
    /// Uploads a replacement snapshot once. An identical content digest is a
    /// no-op, avoiding duplicate work when a scene is rebound to a host.
    /// </summary>
    internal void Load(RenderResourceSnapshot snapshot) =>
        Load(snapshot, _deferTextureResidencyByDefault);

    internal void Load(
        RenderResourceSnapshot snapshot,
        bool deferTextureResidency)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.Equals(
                _resources?.ContentDigest,
                snapshot.ContentDigest,
                StringComparison.Ordinal))
        {
            return;
        }

        MetalResourceSet replacement = MetalResourceSet.Create(
            _device,
            _commandQueue,
            snapshot,
            deferTextureResidency,
            _deferredGeometryIdentities,
            _deferredInstanceIdentities);
        MetalResourceSet? previous = _resources;
        _resources = replacement;
        previous?.Dispose();
    }

    internal MetalGeometryResource RequireGeometry(
        RenderSemanticIdentity identity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return RequireLoaded().RequireGeometry(identity);
    }

    internal MetalInstanceResource RequireInstances(
        RenderSemanticIdentity identity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return RequireLoaded().RequireInstances(identity);
    }

    internal MetalTextureResource RequireTexture(
        RenderSemanticIdentity identity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return RequireLoaded().RequireTexture(identity);
    }

    internal MetalSamplerResource RequireSampler(
        RenderSemanticIdentity identity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return RequireLoaded().RequireSampler(identity);
    }

    internal MetalTextureResidencyFrameResult PrepareTextureResidency(
        IReadOnlyList<RenderSemanticIdentity> visibleTextures,
        long frameIndex,
        long residencyBudgetBytes,
        long uploadBudgetBytes,
        int evictionGraceFrames)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return RequireLoaded().PrepareTextureResidency(
            visibleTextures,
            frameIndex,
            residencyBudgetBytes,
            uploadBudgetBytes,
            evictionGraceFrames);
    }

    internal void ConfigureDeferredStaticResources(
        IEnumerable<RenderSemanticIdentity> geometries,
        IEnumerable<RenderSemanticIdentity> instances)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_resources is not null)
        {
            throw new InvalidOperationException(
                "Deferred Metal static resources must be configured before load.");
        }
        ArgumentNullException.ThrowIfNull(geometries);
        ArgumentNullException.ThrowIfNull(instances);
        _deferredGeometryIdentities.Clear();
        _deferredInstanceIdentities.Clear();
        foreach (RenderSemanticIdentity identity in geometries)
        {
            RenderVertexLayoutDescriptor.RequireIdentity(
                identity,
                RenderSemanticResourceKind.Geometry);
            _deferredGeometryIdentities.Add(identity);
        }
        foreach (RenderSemanticIdentity identity in instances)
        {
            RenderVertexLayoutDescriptor.RequireIdentity(
                identity,
                RenderSemanticResourceKind.Instances);
            _deferredInstanceIdentities.Add(identity);
        }
    }

    internal bool AdmitStaticResources(
        IReadOnlyList<RenderSemanticIdentity> geometries,
        IReadOnlyList<RenderSemanticIdentity> instances)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return RequireLoaded().AdmitStaticResources(geometries, instances);
    }

    /// <summary>
    /// Releases the current scene while preserving the device-bound cache for
    /// a later map load.
    /// </summary>
    internal void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        MetalResourceSet? resources = _resources;
        _resources = null;
        resources?.Dispose();
        _deferredGeometryIdentities.Clear();
        _deferredInstanceIdentities.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _resources?.Dispose();
        _resources = null;
        _disposed = true;
    }

    private MetalResourceSet RequireLoaded() => _resources ??
        throw new InvalidOperationException(
            "Metal scene resources have not been loaded.");
}

[SupportedOSPlatform("macos")]
internal sealed class MetalResourceSet : IDisposable
{
    private readonly MTLDevice _device;
    private readonly MTLCommandQueue _commandQueue;
    private readonly Dictionary<RenderSemanticIdentity, MetalGeometryResource>
        _geometries;
    private readonly Dictionary<RenderSemanticIdentity, MetalInstanceResource>
        _instances;
    private readonly Dictionary<RenderSemanticIdentity, MetalTextureResource>
        _textures;
    private readonly Dictionary<RenderSemanticIdentity, MetalTextureUploadPlan>
        _texturePlans;
    private readonly Dictionary<RenderSemanticIdentity, MetalSamplerResource>
        _samplers;
    private readonly MetalTextureResource[] _ownedTextures;
    private readonly MTLSamplerState[] _ownedSamplerStates;
    private readonly List<MTLBuffer> _progressiveStaticBuffers = [];
    private readonly HashSet<RenderSemanticIdentity>
        _visibleTextureIdentities = [];
    private readonly List<MetalTextureResource>
        _textureAdmissionScratch = [];
    private readonly List<MetalTextureResource>
        _textureEvictionScratch = [];
    private readonly List<MetalTextureResource>
        _selectedTextureEvictions = [];
    private readonly List<MetalTextureResource>
        _selectedTextureUploads = [];
    private readonly List<MetalTextureAllocation>
        _textureAllocationScratch = [];
    private readonly List<MTLTexture> _textureStagingScratch = [];
    private MTLBuffer _staticBuffer;
    private int _residentTextureCount;
    private long _residentTextureByteCount;
    private bool _disposed;

    private MetalResourceSet(
        MTLDevice device,
        MTLCommandQueue commandQueue,
        string contentDigest,
        MTLBuffer staticBuffer,
        long staticBufferByteCount,
        Dictionary<RenderSemanticIdentity, MetalGeometryResource> geometries,
        Dictionary<RenderSemanticIdentity, MetalInstanceResource> instances,
        Dictionary<RenderSemanticIdentity, MetalTextureResource> textures,
        Dictionary<RenderSemanticIdentity, MetalTextureUploadPlan>
            texturePlans,
        Dictionary<RenderSemanticIdentity, MetalSamplerResource> samplers,
        MetalTextureResource[] ownedTextures,
        MTLSamplerState[] ownedSamplerStates)
    {
        _device = device;
        _commandQueue = commandQueue;
        ContentDigest = contentDigest;
        _staticBuffer = staticBuffer;
        StaticBufferByteCount = staticBufferByteCount;
        _geometries = geometries;
        _instances = instances;
        _textures = textures;
        _texturePlans = texturePlans;
        _samplers = samplers;
        _ownedTextures = ownedTextures;
        _ownedSamplerStates = ownedSamplerStates;
        for (int index = 0; index < ownedTextures.Length; index++)
        {
            MetalTextureResource texture = ownedTextures[index];
            if (!texture.IsResident)
                continue;
            _residentTextureCount++;
            _residentTextureByteCount = checked(
                _residentTextureByteCount + texture.UploadedByteCount);
        }
        TextureAuthoredSourceByteCount = CountAuthoredSourceBytes(
            texturePlans.Values);
        TextureDecodedFallbackRetainedByteCount =
            CountDecodedFallbackRetainedBytes(texturePlans.Values);
    }

    internal string ContentDigest { get; }

    internal int GeometryCount => _geometries.Count;

    internal int InstanceCount => _instances.Count;

    internal int TextureCount => _textures.Count;

    internal int ResidentTextureCount => _residentTextureCount;

    internal int SamplerCount => _samplers.Count;

    internal int NativeSamplerStateCount => _ownedSamplerStates.Length;

    internal long StaticBufferByteCount { get; }

    internal long ResidentTextureByteCount => _residentTextureByteCount;

    internal long TextureAuthoredSourceByteCount { get; }

    internal long TextureDecodedFallbackRetainedByteCount { get; }

    internal static MetalResourceSet Create(
        MTLDevice device,
        MTLCommandQueue commandQueue,
        RenderResourceSnapshot snapshot,
        bool deferTextureResidency,
        IReadOnlySet<RenderSemanticIdentity> deferredGeometries,
        IReadOnlySet<RenderSemanticIdentity> deferredInstances)
    {
        ArgumentNullException.ThrowIfNull(deferredGeometries);
        ArgumentNullException.ThrowIfNull(deferredInstances);
        RenderGeometryDescriptor[] geometryDescriptors = snapshot.Geometries
            .OrderBy(descriptor => descriptor.Identity.Value, StringComparer.Ordinal)
            .ToArray();
        RenderInstanceDescriptor[] instanceDescriptors = snapshot.Instances
            .OrderBy(descriptor => descriptor.Identity.Value, StringComparer.Ordinal)
            .ToArray();
        RenderTextureDescriptor[] textureDescriptors = snapshot.Textures
            .OrderBy(descriptor => descriptor.Identity.Value, StringComparer.Ordinal)
            .ToArray();
        RenderSamplerDescriptor[] samplerDescriptors = snapshot.Samplers
            .OrderBy(descriptor => descriptor.Identity.Value, StringComparer.Ordinal)
            .ToArray();

        GeometryPlacement[] geometryPlacements =
            CreateGeometryPlacements(
                geometryDescriptors
                    .Where(descriptor =>
                        !deferredGeometries.Contains(descriptor.Identity))
                    .ToArray(),
                out ulong cursor);
        InstancePlacement[] instancePlacements =
            CreateInstancePlacements(
                instanceDescriptors
                    .Where(descriptor =>
                        !deferredInstances.Contains(descriptor.Identity))
                    .ToArray(),
                ref cursor);
        ulong staticBufferBytes = cursor;
        bool supportsBcTextureCompression =
            device.SupportsBCTextureCompression;
        bool hasUnifiedMemory = device.HasUnifiedMemory;
        MetalTextureUploadPlan[] texturePlans = textureDescriptors
            .Select(descriptor => MetalTextureUploadPlan.Create(
                descriptor,
                supportsBcTextureCompression))
            .ToArray();

        var geometries = new Dictionary<
            RenderSemanticIdentity,
            MetalGeometryResource>(geometryDescriptors.Length);
        var instances = new Dictionary<
            RenderSemanticIdentity,
            MetalInstanceResource>(instanceDescriptors.Length);
        var textures = new Dictionary<
            RenderSemanticIdentity,
            MetalTextureResource>(textureDescriptors.Length);
        var texturePlansByIdentity = texturePlans.ToDictionary(
            plan => plan.Source.Identity);
        var samplers = new Dictionary<
            RenderSemanticIdentity,
            MetalSamplerResource>(samplerDescriptors.Length);
        var ownedTextures = new List<MetalTextureResource>(
            textureDescriptors.Length);
        var ownedSamplerStates = new List<MTLSamplerState>();
        var stagingTextures = new List<MTLTexture>(textureDescriptors.Length);
        MTLBuffer stagingBuffer = default;
        MTLBuffer staticBuffer = default;

        using var autoreleasePool = new NSAutoreleasePool();
        try
        {
            CreateSamplerResources(
                device,
                samplerDescriptors,
                samplers,
                ownedSamplerStates);

            bool requiresUpload = staticBufferBytes > 0 ||
                texturePlans.Length > 0 ||
                geometryDescriptors.Length > 0 ||
                instanceDescriptors.Length > 0;
            if (!requiresUpload)
            {
                return new MetalResourceSet(
                    device,
                    commandQueue,
                    snapshot.ContentDigest,
                    staticBuffer,
                    staticBufferByteCount: 0,
                    geometries,
                    instances,
                    textures,
                    texturePlansByIdentity,
                    samplers,
                    ownedTextures.ToArray(),
                    ownedSamplerStates.ToArray());
            }

            MTLCommandBuffer commandBuffer = commandQueue.CommandBuffer();
            if (commandBuffer.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    "Metal could not allocate a resource upload command buffer.");
            }
            MTLBlitCommandEncoder blit = commandBuffer.BlitCommandEncoder();
            if (blit.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    "Metal could not allocate a resource upload blit encoder.");
            }

            if (staticBufferBytes > 0)
            {
                stagingBuffer = device.NewBuffer(
                    staticBufferBytes,
                    (hasUnifiedMemory
                        ? MTLResourceOptions.ResourceStorageModeShared
                        : MTLResourceOptions.ResourceStorageModeManaged) |
                    MTLResourceOptions.ResourceCPUCacheModeWriteCombined);
                RequireBuffer(stagingBuffer, "scene staging");
                staticBuffer = device.NewBuffer(
                    staticBufferBytes,
                    MTLResourceOptions.ResourceStorageModePrivate);
                RequireBuffer(staticBuffer, "private scene");
                CopyStaticPayloads(
                    stagingBuffer,
                    geometryPlacements,
                    instancePlacements);
                if (!hasUnifiedMemory)
                {
                    stagingBuffer.DidModifyRange(new NSRange
                    {
                        location = 0,
                        length = staticBufferBytes
                    });
                }
                blit.CopyFromBuffer(
                    stagingBuffer,
                    sourceOffset: 0,
                    staticBuffer,
                    destinationOffset: 0,
                    staticBufferBytes);
                CreateStaticBindings(
                    staticBuffer,
                    geometryPlacements,
                    instancePlacements,
                    geometries,
                    instances);
            }

            foreach (RenderGeometryDescriptor descriptor in
                     geometryDescriptors)
            {
                if (!geometries.ContainsKey(descriptor.Identity))
                {
                    geometries.Add(
                        descriptor.Identity,
                        new MetalGeometryResource(descriptor));
                }
            }
            foreach (RenderInstanceDescriptor descriptor in
                     instanceDescriptors)
            {
                if (!instances.ContainsKey(descriptor.Identity))
                {
                    instances.Add(
                        descriptor.Identity,
                        new MetalInstanceResource(descriptor));
                }
            }

            long creationOrdinal = 0;
            foreach (MetalTextureUploadPlan plan in texturePlans)
            {
                MetalTextureResource texture = CreateTextureResourceShell(
                    device,
                    plan,
                    creationOrdinal++);
                ownedTextures.Add(texture);
                textures.Add(plan.Source.Identity, texture);
            }

            if (!deferTextureResidency && texturePlans.Length != 0)
            {
                UploadTextureAllocations(
                    device,
                    blit,
                    texturePlans,
                    textures,
                    hasUnifiedMemory,
                    stagingTextures,
                    frameIndex: -1,
                    pendingAllocations: null);
            }

            blit.EndEncoding();
            commandBuffer.Commit();
            commandBuffer.WaitUntilCompleted();
            if (commandBuffer.Status != MTLCommandBufferStatus.Completed)
            {
                throw new InvalidOperationException(
                    $"Metal resource upload failed with command-buffer " +
                    $"status {commandBuffer.Status}.");
            }

            DisposeStagingResources(ref stagingBuffer, stagingTextures);
            return new MetalResourceSet(
                device,
                commandQueue,
                snapshot.ContentDigest,
                staticBuffer,
                checked((long)staticBufferBytes),
                geometries,
                instances,
                textures,
                texturePlansByIdentity,
                samplers,
                ownedTextures.ToArray(),
                ownedSamplerStates.ToArray());
        }
        catch
        {
            DisposeStagingResources(ref stagingBuffer, stagingTextures);
            foreach (MetalTextureResource texture in ownedTextures)
                texture.Dispose();
            foreach (MTLSamplerState sampler in ownedSamplerStates)
                sampler.Dispose();
            if (staticBuffer.NativePtr != 0)
                staticBuffer.Dispose();
            throw;
        }
    }

    internal MetalGeometryResource RequireGeometry(
        RenderSemanticIdentity identity) => Require(
            _geometries,
            identity,
            RenderSemanticResourceKind.Geometry);

    internal MetalInstanceResource RequireInstances(
        RenderSemanticIdentity identity) => Require(
            _instances,
            identity,
            RenderSemanticResourceKind.Instances);

    internal MetalTextureResource RequireTexture(
        RenderSemanticIdentity identity) => Require(
            _textures,
            identity,
            RenderSemanticResourceKind.Texture);

    internal MetalSamplerResource RequireSampler(
        RenderSemanticIdentity identity) => Require(
            _samplers,
            identity,
            RenderSemanticResourceKind.Sampler);

    internal bool AdmitStaticResources(
        IReadOnlyList<RenderSemanticIdentity> geometryIdentities,
        IReadOnlyList<RenderSemanticIdentity> instanceIdentities)
    {
        ArgumentNullException.ThrowIfNull(geometryIdentities);
        ArgumentNullException.ThrowIfNull(instanceIdentities);
        RenderGeometryDescriptor[] geometryDescriptors = geometryIdentities
            .Distinct()
            .Select(identity => Require(
                _geometries,
                identity,
                RenderSemanticResourceKind.Geometry))
            .Where(resource => !resource.IsResident)
            .Select(resource => resource.Descriptor)
            .ToArray();
        RenderInstanceDescriptor[] instanceDescriptors = instanceIdentities
            .Distinct()
            .Select(identity => Require(
                _instances,
                identity,
                RenderSemanticResourceKind.Instances))
            .Where(resource => !resource.IsResident)
            .Select(resource => resource.Descriptor)
            .ToArray();
        if (geometryDescriptors.Length == 0 &&
            instanceDescriptors.Length == 0)
        {
            return false;
        }

        GeometryPlacement[] geometryPlacements =
            CreateGeometryPlacements(geometryDescriptors, out ulong cursor);
        InstancePlacement[] instancePlacements =
            CreateInstancePlacements(instanceDescriptors, ref cursor);
        if (cursor == 0)
            return false;

        // Reserve the ownership slot before any stable resource binding is
        // published. Once Install runs, this buffer must remain retained for
        // the complete scene lifetime.
        _progressiveStaticBuffers.EnsureCapacity(
            checked(_progressiveStaticBuffers.Count + 1));
        bool unifiedMemory = _device.HasUnifiedMemory;
        MTLBuffer staging = default;
        MTLBuffer storage = default;
        using var pool = new NSAutoreleasePool();
        try
        {
            staging = _device.NewBuffer(
                cursor,
                (unifiedMemory
                    ? MTLResourceOptions.ResourceStorageModeShared
                    : MTLResourceOptions.ResourceStorageModeManaged) |
                MTLResourceOptions.ResourceCPUCacheModeWriteCombined);
            RequireBuffer(staging, "progressive static staging");
            storage = _device.NewBuffer(
                cursor,
                MTLResourceOptions.ResourceStorageModePrivate);
            RequireBuffer(storage, "progressive static private");
            CopyStaticPayloads(
                staging,
                geometryPlacements,
                instancePlacements);
            if (!unifiedMemory)
            {
                staging.DidModifyRange(new NSRange
                {
                    location = 0,
                    length = cursor
                });
            }

            MTLCommandBuffer commandBuffer = _commandQueue.CommandBuffer();
            if (commandBuffer.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    "Metal could not allocate a progressive static upload command buffer.");
            }
            MTLBlitCommandEncoder blit = commandBuffer.BlitCommandEncoder();
            if (blit.NativePtr == 0)
            {
                throw new InvalidOperationException(
                    "Metal could not allocate a progressive static upload encoder.");
            }
            blit.CopyFromBuffer(
                staging,
                sourceOffset: 0,
                storage,
                destinationOffset: 0,
                cursor);
            blit.EndEncoding();
            commandBuffer.Commit();
            commandBuffer.WaitUntilCompleted();
            if (commandBuffer.Status != MTLCommandBufferStatus.Completed)
            {
                throw new InvalidOperationException(
                    $"Metal progressive static upload failed with " +
                    $"command-buffer status {commandBuffer.Status}.");
            }

            foreach (GeometryPlacement placement in geometryPlacements)
            {
                _geometries[placement.Descriptor.Identity].Install(
                    storage,
                    placement.VertexOffset,
                    placement.IndexOffset);
            }
            foreach (InstancePlacement placement in instancePlacements)
            {
                _instances[placement.Descriptor.Identity].Install(
                    storage,
                    placement.Offset);
            }
            _progressiveStaticBuffers.Add(storage);
            storage = default;
            return true;
        }
        finally
        {
            if (staging.NativePtr != 0)
                staging.Dispose();
            if (storage.NativePtr != 0)
                storage.Dispose();
        }
    }

    internal MetalTextureResidencyFrameResult PrepareTextureResidency(
        IReadOnlyList<RenderSemanticIdentity> visibleTextures,
        long frameIndex,
        long residencyBudgetBytes,
        long uploadBudgetBytes,
        int evictionGraceFrames)
    {
        ArgumentNullException.ThrowIfNull(visibleTextures);
        if (frameIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
        if (evictionGraceFrames < 0)
            throw new ArgumentOutOfRangeException(nameof(evictionGraceFrames));

        long residencyBudget = Math.Max(0, residencyBudgetBytes);
        long uploadBudget = Math.Max(0, uploadBudgetBytes);
        _visibleTextureIdentities.Clear();
        _textureAdmissionScratch.Clear();
        _textureEvictionScratch.Clear();
        _selectedTextureEvictions.Clear();
        _selectedTextureUploads.Clear();
        _textureAllocationScratch.Clear();
        for (int index = 0; index < visibleTextures.Count; index++)
        {
            RenderSemanticIdentity identity = visibleTextures[index];
            if (!_visibleTextureIdentities.Add(identity))
                continue;
            MetalTextureResource texture = Require(
                _textures,
                identity,
                RenderSemanticResourceKind.Texture);
            texture.MarkVisible(frameIndex);
            if (!texture.IsResident)
                _textureAdmissionScratch.Add(texture);
        }

        long residentBytes = ResidentTextureByteCount;
        long oldestEligibleFrame = frameIndex - evictionGraceFrames;
        for (int index = 0; index < _ownedTextures.Length; index++)
        {
            MetalTextureResource texture = _ownedTextures[index];
            if (texture.IsResident &&
                !_visibleTextureIdentities.Contains(
                    texture.Descriptor.Identity) &&
                texture.LastVisibleFrame <= oldestEligibleFrame)
            {
                _textureEvictionScratch.Add(texture);
            }
        }
        _textureEvictionScratch.Sort(
            MetalTextureEvictionComparer.Instance);
        int evictionIndex = 0;
        long uploadBytes = 0;
        long authoredUploadBytes = 0;
        int deferredCount = 0;

        for (int index = 0;
             index < _textureAdmissionScratch.Count;
             index++)
        {
            MetalTextureResource texture = _textureAdmissionScratch[index];
            long textureBytes = texture.UploadedByteCount;
            if (textureBytes > residencyBudget || uploadBudget == 0)
            {
                deferredCount++;
                continue;
            }
            while (residentBytes + textureBytes > residencyBudget &&
                   evictionIndex < _textureEvictionScratch.Count)
            {
                MetalTextureResource eviction =
                    _textureEvictionScratch[evictionIndex++];
                _selectedTextureEvictions.Add(eviction);
                residentBytes = checked(
                    residentBytes - eviction.UploadedByteCount);
            }
            if (residentBytes + textureBytes > residencyBudget)
            {
                deferredCount++;
                continue;
            }
            if (_selectedTextureUploads.Count != 0 &&
                uploadBytes + textureBytes > uploadBudget)
            {
                deferredCount++;
                continue;
            }

            _selectedTextureUploads.Add(texture);
            uploadBytes = checked(uploadBytes + textureBytes);
            if (_texturePlans[texture.Descriptor.Identity].UploadKind ==
                RenderTexturePayloadKind.Authored)
            {
                authoredUploadBytes = checked(
                    authoredUploadBytes + textureBytes);
            }
            residentBytes = checked(residentBytes + textureBytes);
        }

        while (residentBytes > residencyBudget &&
               evictionIndex < _textureEvictionScratch.Count)
        {
            MetalTextureResource eviction =
                _textureEvictionScratch[evictionIndex++];
            _selectedTextureEvictions.Add(eviction);
            residentBytes = checked(
                residentBytes - eviction.UploadedByteCount);
        }

        if (_selectedTextureUploads.Count != 0)
        {
            UploadSelectedTextures(
                _selectedTextureUploads,
                _textureAllocationScratch);
        }

        long evictionBytes = 0;
        int evictionCount = 0;
        for (int index = 0;
             index < _selectedTextureEvictions.Count;
             index++)
        {
            MetalTextureResource eviction =
                _selectedTextureEvictions[index];
            if (!eviction.IsResident)
                continue;
            evictionBytes = checked(
                evictionBytes + eviction.UploadedByteCount);
            eviction.Evict();
            _residentTextureCount--;
            _residentTextureByteCount = checked(
                _residentTextureByteCount - eviction.UploadedByteCount);
            evictionCount++;
        }
        for (int index = 0;
             index < _selectedTextureUploads.Count;
             index++)
        {
            MetalTextureAllocation allocation =
                _textureAllocationScratch[index];
            MetalTextureResource texture = _selectedTextureUploads[index];
            texture.InstallResidentAllocation(
                allocation.Storage,
                allocation.LinearView,
                allocation.SrgbView,
                frameIndex);
            _residentTextureCount++;
            _residentTextureByteCount = checked(
                _residentTextureByteCount + texture.UploadedByteCount);
        }
        _textureAllocationScratch.Clear();

        return new MetalTextureResidencyFrameResult(
            ResidentTextureCount,
            ResidentTextureByteCount,
            _selectedTextureUploads.Count,
            uploadBytes,
            authoredUploadBytes,
            evictionCount,
            evictionBytes,
            deferredCount);
    }

    private void UploadSelectedTextures(
        IReadOnlyList<MetalTextureResource> textures,
        ICollection<MetalTextureAllocation> pending)
    {
        using var pool = new NSAutoreleasePool();
        MTLCommandBuffer commandBuffer = _commandQueue.CommandBuffer();
        if (commandBuffer.NativePtr == 0)
        {
            throw new InvalidOperationException(
                "Metal could not allocate a texture-residency command buffer.");
        }
        MTLBlitCommandEncoder blit = commandBuffer.BlitCommandEncoder();
        if (blit.NativePtr == 0)
        {
            throw new InvalidOperationException(
                "Metal could not allocate a texture-residency blit encoder.");
        }
        _textureStagingScratch.Clear();
        try
        {
            for (int index = 0; index < textures.Count; index++)
            {
                MetalTextureResource texture = textures[index];
                MetalTextureUploadPlan plan = _texturePlans[
                    texture.Descriptor.Identity];
                pending.Add(CreateTextureAllocation(
                    _device,
                    blit,
                    plan,
                    _device.HasUnifiedMemory,
                    _textureStagingScratch));
            }
            blit.EndEncoding();
            commandBuffer.Commit();
            commandBuffer.WaitUntilCompleted();
            if (commandBuffer.Status != MTLCommandBufferStatus.Completed)
            {
                throw new InvalidOperationException(
                    $"Metal texture residency upload failed with " +
                    $"command-buffer status {commandBuffer.Status}.");
            }
        }
        catch
        {
            foreach (MetalTextureAllocation allocation in pending)
                DisposeTextureAllocation(allocation);
            pending.Clear();
            throw;
        }
        finally
        {
            for (int index = 0;
                 index < _textureStagingScratch.Count;
                 index++)
            {
                _textureStagingScratch[index].Dispose();
            }
            _textureStagingScratch.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        foreach (MetalTextureResource texture in _ownedTextures)
            texture.Dispose();
        foreach (MTLSamplerState sampler in _ownedSamplerStates)
            sampler.Dispose();
        foreach (MTLBuffer buffer in _progressiveStaticBuffers)
            buffer.Dispose();
        _progressiveStaticBuffers.Clear();
        if (_staticBuffer.NativePtr != 0)
            _staticBuffer.Dispose();
        _staticBuffer = default;
        _disposed = true;
    }

    private static GeometryPlacement[] CreateGeometryPlacements(
        IReadOnlyList<RenderGeometryDescriptor> descriptors,
        out ulong cursor)
    {
        cursor = 0;
        var placements = new GeometryPlacement[descriptors.Count];
        for (int index = 0; index < placements.Length; index++)
        {
            RenderGeometryDescriptor descriptor = descriptors[index];
            ulong vertexOffset = AppendPayload(
                ref cursor,
                descriptor.VertexPayload.Length);
            ulong indexOffset = AppendPayload(
                ref cursor,
                descriptor.IndexPayload.Length);
            placements[index] = new GeometryPlacement(
                descriptor,
                vertexOffset,
                indexOffset);
        }
        return placements;
    }

    private static InstancePlacement[] CreateInstancePlacements(
        IReadOnlyList<RenderInstanceDescriptor> descriptors,
        ref ulong cursor)
    {
        var placements = new InstancePlacement[descriptors.Count];
        for (int index = 0; index < placements.Length; index++)
        {
            RenderInstanceDescriptor descriptor = descriptors[index];
            placements[index] = new InstancePlacement(
                descriptor,
                AppendPayload(ref cursor, descriptor.Payload.Length));
        }
        return placements;
    }

    private static ulong AppendPayload(ref ulong cursor, int byteCount)
    {
        const ulong alignment = 16;
        if (byteCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(byteCount));

        cursor = checked((cursor + alignment - 1) & ~(alignment - 1));
        ulong offset = cursor;
        cursor = checked(cursor + (ulong)byteCount);
        return offset;
    }

    private static unsafe void CopyStaticPayloads(
        MTLBuffer stagingBuffer,
        IReadOnlyList<GeometryPlacement> geometries,
        IReadOnlyList<InstancePlacement> instances)
    {
        if (stagingBuffer.Contents == 0)
        {
            throw new InvalidOperationException(
                "The shared Metal staging buffer is not CPU-accessible.");
        }

        foreach (GeometryPlacement placement in geometries)
        {
            CopyToBuffer(
                placement.Descriptor.VertexPayload.AsSpan(),
                stagingBuffer.Contents,
                placement.VertexOffset);
            CopyToBuffer(
                placement.Descriptor.IndexPayload.AsSpan(),
                stagingBuffer.Contents,
                placement.IndexOffset);
        }
        foreach (InstancePlacement placement in instances)
        {
            CopyToBuffer(
                placement.Descriptor.Payload.AsSpan(),
                stagingBuffer.Contents,
                placement.Offset);
        }
    }

    private static unsafe void CopyToBuffer(
        ReadOnlySpan<byte> source,
        nint destination,
        ulong offset)
    {
        void* target = (void*)(checked((nuint)destination + offset));
        source.CopyTo(new Span<byte>(target, source.Length));
    }

    private static void CreateStaticBindings(
        MTLBuffer buffer,
        IReadOnlyList<GeometryPlacement> geometryPlacements,
        IReadOnlyList<InstancePlacement> instancePlacements,
        IDictionary<RenderSemanticIdentity, MetalGeometryResource> geometries,
        IDictionary<RenderSemanticIdentity, MetalInstanceResource> instances)
    {
        foreach (GeometryPlacement placement in geometryPlacements)
        {
            geometries.Add(
                placement.Descriptor.Identity,
                new MetalGeometryResource(
                    placement.Descriptor,
                    buffer,
                    placement.VertexOffset,
                    placement.IndexOffset));
        }
        foreach (InstancePlacement placement in instancePlacements)
        {
            instances.Add(
                placement.Descriptor.Identity,
                new MetalInstanceResource(
                    placement.Descriptor,
                    buffer,
                    placement.Offset));
        }
    }

    private static MetalTextureResource CreateTextureResourceShell(
        MTLDevice device,
        MetalTextureUploadPlan plan,
        long creationOrdinal)
    {
        MetalTextureAllocation fallback = CreateFallbackTexture(
            device,
            plan);
        try
        {
            return new MetalTextureResource(
                plan.Source,
                fallback.Storage,
                fallback.LinearView,
                fallback.SrgbView,
                plan.LinearPixelFormat,
                plan.SrgbPixelFormat,
                plan.UploadKind,
                plan.UploadedByteCount,
                creationOrdinal);
        }
        catch
        {
            DisposeTextureAllocation(fallback);
            throw;
        }
    }

    private static unsafe MetalTextureAllocation CreateFallbackTexture(
        MTLDevice device,
        MetalTextureUploadPlan plan)
    {
        MTLTexture storage = default;
        MTLTexture linearView = default;
        MTLTexture srgbView = default;
        try
        {
            using var descriptor = new MTLTextureDescriptor
            {
                TextureType = plan.TextureType,
                PixelFormat = MTLPixelFormat.RGBA8Unorm,
                Width = 1,
                Height = 1,
                Depth = 1,
                MipmapLevelCount = 1,
                ArrayLength = plan.NativeArrayLength,
                SampleCount = 1,
                StorageMode = MTLStorageMode.Shared,
                CpuCacheMode = MTLCPUCacheMode.DefaultCache,
                Usage = MTLTextureUsage.ShaderRead |
                    MTLTextureUsage.PixelFormatView,
                AllowGPUOptimizedContents = false
            };
            storage = device.NewTexture(descriptor);
            RequireTexture(storage, plan.Source.Identity, "fallback");

            byte* clear = stackalloc byte[4];
            clear[0] = byte.MaxValue;
            clear[1] = byte.MaxValue;
            clear[2] = byte.MaxValue;
            clear[3] = byte.MaxValue;
            ulong flatSliceCount = plan.Source.Dimension ==
                RenderTextureDimension.Texture3D
                    ? 1
                    : checked((ulong)plan.Source.ArrayLayerCount);
            var region = new MTLRegion
            {
                origin = new MTLOrigin(),
                size = new MTLSize
                {
                    width = 1,
                    height = 1,
                    depth = 1
                }
            };
            for (ulong slice = 0; slice < flatSliceCount; slice++)
            {
                storage.ReplaceRegion(
                    region,
                    level: 0,
                    slice,
                    (nint)clear,
                    bytesPerRow: 4,
                    bytesPerImage: 4);
            }

            MTLTextureSwizzleChannels swizzle = ToMetalSwizzle(plan.Swizzle);
            var levelRange = new NSRange { location = 0, length = 1 };
            var sliceRange = new NSRange
            {
                location = 0,
                length = plan.ViewSliceCount
            };
            linearView = storage.NewTextureView(
                MTLPixelFormat.RGBA8Unorm,
                plan.TextureType,
                levelRange,
                sliceRange,
                swizzle);
            RequireTexture(
                linearView,
                plan.Source.Identity,
                "fallback linear view");
            srgbView = storage.NewTextureView(
                MTLPixelFormat.RGBA8UnormsRGB,
                plan.TextureType,
                levelRange,
                sliceRange,
                swizzle);
            RequireTexture(
                srgbView,
                plan.Source.Identity,
                "fallback sRGB view");
            return new(storage, linearView, srgbView);
        }
        catch
        {
            DisposeTextureAllocation(new(storage, linearView, srgbView));
            throw;
        }
    }

    private static MetalTextureAllocation CreateTextureAllocation(
        MTLDevice device,
        MTLBlitCommandEncoder blit,
        MetalTextureUploadPlan plan,
        bool unifiedMemory,
        ICollection<MTLTexture> stagingTextures)
    {
        MTLTexture staging = default;
        MTLTexture storage = default;
        MTLTexture linearView = default;
        MTLTexture srgbView = default;
        try
        {
            using MTLTextureDescriptor stagingDescriptor =
                plan.CreateNativeDescriptor(
                    unifiedMemory
                        ? MTLStorageMode.Shared
                        : MTLStorageMode.Managed);
            staging = device.NewTexture(stagingDescriptor);
            RequireTexture(staging, plan.Source.Identity, "staging");
            foreach (MetalTextureSubresourceUpload subresource in
                     plan.Subresources)
            {
                subresource.ReplaceStagingTexture(staging);
            }

            using MTLTextureDescriptor privateDescriptor =
                plan.CreateNativeDescriptor(MTLStorageMode.Private);
            storage = device.NewTexture(privateDescriptor);
            RequireTexture(storage, plan.Source.Identity, "private");
            foreach (MetalTextureSubresourceUpload subresource in
                     plan.Subresources)
            {
                RenderTextureSubresourceDescriptor source =
                    subresource.Descriptor;
                var origin = new MTLOrigin();
                var size = new MTLSize
                {
                    width = checked((ulong)source.Width),
                    height = checked((ulong)source.Height),
                    depth = checked((ulong)source.Depth)
                };
                blit.CopyFromTexture(
                    staging,
                    checked((ulong)source.ArrayLayer),
                    checked((ulong)source.MipLevel),
                    origin,
                    size,
                    storage,
                    checked((ulong)source.ArrayLayer),
                    checked((ulong)source.MipLevel),
                    origin);
            }

            MTLTextureSwizzleChannels swizzle = ToMetalSwizzle(plan.Swizzle);
            var levelRange = new NSRange
            {
                location = 0,
                length = checked((ulong)plan.Source.MipCount)
            };
            var sliceRange = new NSRange
            {
                location = 0,
                length = plan.ViewSliceCount
            };
            linearView = storage.NewTextureView(
                plan.LinearPixelFormat,
                plan.TextureType,
                levelRange,
                sliceRange,
                swizzle);
            RequireTexture(linearView, plan.Source.Identity, "linear view");
            if (plan.SrgbPixelFormat != MTLPixelFormat.Invalid)
            {
                srgbView = storage.NewTextureView(
                    plan.SrgbPixelFormat,
                    plan.TextureType,
                    levelRange,
                    sliceRange,
                    swizzle);
                RequireTexture(srgbView, plan.Source.Identity, "sRGB view");
            }

            stagingTextures.Add(staging);
            staging = default;
            return new(storage, linearView, srgbView);
        }
        catch
        {
            if (srgbView.NativePtr != 0)
                srgbView.Dispose();
            if (linearView.NativePtr != 0)
                linearView.Dispose();
            if (storage.NativePtr != 0)
                storage.Dispose();
            if (staging.NativePtr != 0)
                staging.Dispose();
            throw;
        }
    }

    private static void UploadTextureAllocations(
        MTLDevice device,
        MTLBlitCommandEncoder blit,
        IReadOnlyList<MetalTextureUploadPlan> plans,
        IReadOnlyDictionary<RenderSemanticIdentity, MetalTextureResource>
            textures,
        bool unifiedMemory,
        ICollection<MTLTexture> stagingTextures,
        long frameIndex,
        ICollection<MetalTextureAllocation>? pendingAllocations)
    {
        foreach (MetalTextureUploadPlan plan in plans)
        {
            MetalTextureAllocation allocation = CreateTextureAllocation(
                device,
                blit,
                plan,
                unifiedMemory,
                stagingTextures);
            if (pendingAllocations is not null)
            {
                pendingAllocations.Add(allocation);
                continue;
            }
            textures[plan.Source.Identity].InstallResidentAllocation(
                allocation.Storage,
                allocation.LinearView,
                allocation.SrgbView,
                frameIndex);
        }
    }

    private static void DisposeTextureAllocation(
        MetalTextureAllocation allocation)
    {
        if (allocation.SrgbView.NativePtr != 0)
            allocation.SrgbView.Dispose();
        if (allocation.LinearView.NativePtr != 0)
            allocation.LinearView.Dispose();
        if (allocation.Storage.NativePtr != 0)
            allocation.Storage.Dispose();
    }

    private static long CountAuthoredSourceBytes(
        IEnumerable<MetalTextureUploadPlan> plans)
    {
        var payloads = new HashSet<string>(StringComparer.Ordinal);
        long bytes = 0;
        foreach (MetalTextureUploadPlan plan in plans)
        foreach (RenderTextureSubresourceDescriptor subresource in
                 plan.Source.Subresources)
        foreach (RenderTexturePayloadDescriptor payload in
                 subresource.Payloads)
        {
            if (payload.Kind != RenderTexturePayloadKind.Authored ||
                !payloads.Add(payload.ContentDigest))
            {
                continue;
            }
            bytes = checked(bytes + payload.Payload.Length);
        }
        return bytes;
    }

    private static long CountDecodedFallbackRetainedBytes(
        IEnumerable<MetalTextureUploadPlan> plans)
    {
        long bytes = 0;
        foreach (MetalTextureUploadPlan plan in plans)
        {
            bool hasSourceDecodedPayload = false;
            foreach (RenderTextureSubresourceDescriptor subresource in
                     plan.Source.Subresources)
            foreach (RenderTexturePayloadDescriptor payload in
                     subresource.Payloads)
            {
                if (payload.Kind == RenderTexturePayloadKind.Authored)
                    continue;
                hasSourceDecodedPayload = true;
                bytes = checked(bytes + payload.Payload.Length);
            }

            // When authored BC is the only retained source representation on
            // hardware without BC sampling, the Metal plan owns a decoded
            // replacement chain which is not present in the source snapshot.
            if (plan.UploadKind != RenderTexturePayloadKind.Authored &&
                !hasSourceDecodedPayload)
            {
                bytes = checked(bytes + plan.UploadedByteCount);
            }
        }
        return bytes;
    }

    private static MTLTextureSwizzleChannels ToMetalSwizzle(
        RsxTextureSwizzle swizzle) => new()
        {
            red = ToMetalSwizzle(swizzle.Red),
            green = ToMetalSwizzle(swizzle.Green),
            blue = ToMetalSwizzle(swizzle.Blue),
            alpha = ToMetalSwizzle(swizzle.Alpha)
        };

    private static MTLTextureSwizzle ToMetalSwizzle(
        RsxTextureSwizzleSource source) => source switch
    {
        RsxTextureSwizzleSource.Zero => MTLTextureSwizzle.Zero,
        RsxTextureSwizzleSource.One => MTLTextureSwizzle.One,
        RsxTextureSwizzleSource.Red => MTLTextureSwizzle.Red,
        RsxTextureSwizzleSource.Green => MTLTextureSwizzle.Green,
        RsxTextureSwizzleSource.Blue => MTLTextureSwizzle.Blue,
        RsxTextureSwizzleSource.Alpha => MTLTextureSwizzle.Alpha,
        _ => throw new ArgumentOutOfRangeException(
            nameof(source),
            source,
            "Unsupported RSX texture swizzle.")
    };

    private static void CreateSamplerResources(
        MTLDevice device,
        IEnumerable<RenderSamplerDescriptor> descriptors,
        IDictionary<RenderSemanticIdentity, MetalSamplerResource> resources,
        ICollection<MTLSamplerState> ownedStates)
    {
        var interned = new Dictionary<MetalSamplerKey, MTLSamplerState>();
        foreach (RenderSamplerDescriptor descriptor in descriptors)
        {
            MetalSamplerKey key = MetalSamplerKey.From(descriptor);
            if (!interned.TryGetValue(key, out MTLSamplerState state))
            {
                using var nativeDescriptor = new MTLSamplerDescriptor
                {
                    MinFilter = ToMetalMinMagFilter(descriptor.MinFilter),
                    MagFilter = ToMetalMinMagFilter(descriptor.MagFilter),
                    MipFilter = ToMetalMipFilter(descriptor.MipFilter),
                    MaxAnisotropy = checked((ulong)Math.Clamp(
                        descriptor.MaxAnisotropy,
                        1,
                        16)),
                    LodBias = descriptor.MipLodBias,
                    SAddressMode = ToMetalAddressMode(descriptor.AddressU),
                    TAddressMode = ToMetalAddressMode(descriptor.AddressV),
                    RAddressMode = ToMetalAddressMode(descriptor.AddressW),
                    NormalizedCoordinates = true
                };
                state = device.NewSamplerState(nativeDescriptor);
                if (state.NativePtr == 0)
                {
                    throw new InvalidOperationException(
                        $"Metal could not create sampler {descriptor.Identity}.");
                }
                ownedStates.Add(state);
                interned.Add(key, state);
            }

            resources.Add(
                descriptor.Identity,
                new MetalSamplerResource(descriptor, state));
        }
    }

    private static MTLSamplerMinMagFilter ToMetalMinMagFilter(
        TextureFilter filter) => filter == TextureFilter.Point
            ? MTLSamplerMinMagFilter.Nearest
            : MTLSamplerMinMagFilter.Linear;

    private static MTLSamplerMipFilter ToMetalMipFilter(
        TextureFilter filter) => filter switch
    {
        TextureFilter.None => MTLSamplerMipFilter.NotMipmapped,
        TextureFilter.Point => MTLSamplerMipFilter.Nearest,
        TextureFilter.Linear or TextureFilter.Anisotropic =>
            MTLSamplerMipFilter.Linear,
        _ => throw new ArgumentOutOfRangeException(
            nameof(filter),
            filter,
            "Unsupported sampler mip filter.")
    };

    private static MTLSamplerAddressMode ToMetalAddressMode(
        TextureAddressMode mode) => mode switch
    {
        TextureAddressMode.Wrap => MTLSamplerAddressMode.Repeat,
        TextureAddressMode.Clamp => MTLSamplerAddressMode.ClampToEdge,
        _ => throw new ArgumentOutOfRangeException(
            nameof(mode),
            mode,
            "Unsupported sampler address mode.")
    };

    private static void RequireBuffer(MTLBuffer buffer, string role)
    {
        if (buffer.NativePtr == 0)
        {
            throw new InvalidOperationException(
                $"Metal could not allocate the {role} buffer.");
        }
    }

    private static void RequireTexture(
        MTLTexture texture,
        RenderSemanticIdentity identity,
        string role)
    {
        if (texture.NativePtr == 0)
        {
            throw new InvalidOperationException(
                $"Metal could not allocate the {role} texture for " +
                $"{identity}.");
        }
    }

    private static void DisposeStagingResources(
        ref MTLBuffer stagingBuffer,
        ICollection<MTLTexture> stagingTextures)
    {
        foreach (MTLTexture texture in stagingTextures)
            texture.Dispose();
        stagingTextures.Clear();
        if (stagingBuffer.NativePtr != 0)
            stagingBuffer.Dispose();
        stagingBuffer = default;
    }

    private static T Require<T>(
        IReadOnlyDictionary<RenderSemanticIdentity, T> resources,
        RenderSemanticIdentity identity,
        RenderSemanticResourceKind kind)
        where T : class
    {
        if (identity.Kind != kind || string.IsNullOrWhiteSpace(identity.Value))
        {
            throw new ArgumentException(
                $"Expected a valid {kind} identity.",
                nameof(identity));
        }
        if (resources.TryGetValue(identity, out T? resource))
            return resource;

        throw new KeyNotFoundException(
            $"No Metal {kind} resource exists for {identity}.");
    }

    private readonly record struct GeometryPlacement(
        RenderGeometryDescriptor Descriptor,
        ulong VertexOffset,
        ulong IndexOffset);

    private readonly record struct InstancePlacement(
        RenderInstanceDescriptor Descriptor,
        ulong Offset);

    private readonly record struct MetalSamplerKey(
        TextureFilter MinFilter,
        TextureFilter MagFilter,
        TextureFilter MipFilter,
        int MaxAnisotropy,
        float MipLodBias,
        TextureAddressMode AddressU,
        TextureAddressMode AddressV,
        TextureAddressMode AddressW)
    {
        internal static MetalSamplerKey From(
            RenderSamplerDescriptor descriptor) => new(
                descriptor.MinFilter,
                descriptor.MagFilter,
                descriptor.MipFilter,
                descriptor.MaxAnisotropy,
                descriptor.MipLodBias,
                descriptor.AddressU,
                descriptor.AddressV,
                descriptor.AddressW);
    }

    private sealed class MetalTextureEvictionComparer :
        IComparer<MetalTextureResource>
    {
        internal static MetalTextureEvictionComparer Instance { get; } =
            new();

        public int Compare(
            MetalTextureResource? left,
            MetalTextureResource? right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left is null)
                return -1;
            if (right is null)
                return 1;

            int order = left.LastVisibleFrame.CompareTo(
                right.LastVisibleFrame);
            if (order != 0)
                return order;
            order = left.LastResidentFrame.CompareTo(
                right.LastResidentFrame);
            return order != 0
                ? order
                : left.CreationOrdinal.CompareTo(right.CreationOrdinal);
        }
    }

    private readonly record struct MetalTextureAllocation(
        MTLTexture Storage,
        MTLTexture LinearView,
        MTLTexture SrgbView);
}

internal readonly record struct MetalTextureResidencyFrameResult(
    int ResidentCount,
    long ResidentBytes,
    int UploadCount,
    long UploadBytes,
    long AuthoredUploadBytes,
    int EvictionCount,
    long EvictionBytes,
    int DeferredCount);
