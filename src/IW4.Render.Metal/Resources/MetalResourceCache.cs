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
    private MetalResourceSet? _resources;
    private bool _disposed;

    internal MetalResourceCache(
        MTLDevice device,
        MTLCommandQueue commandQueue)
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
    }

    internal bool IsLoaded => _resources is not null;

    internal string? ContentDigest => _resources?.ContentDigest;

    internal int GeometryCount => _resources?.GeometryCount ?? 0;

    internal int InstanceResourceCount => _resources?.InstanceCount ?? 0;

    internal int TextureCount => _resources?.TextureCount ?? 0;

    internal int SamplerCount => _resources?.SamplerCount ?? 0;

    internal long StaticBufferByteCount =>
        _resources?.StaticBufferByteCount ?? 0;

    internal long UploadedTextureByteCount =>
        _resources?.UploadedTextureByteCount ?? 0;

    internal int NativeSamplerStateCount =>
        _resources?.NativeSamplerStateCount ?? 0;

    /// <summary>
    /// Uploads a replacement snapshot once. An identical content digest is a
    /// no-op, avoiding duplicate work when a scene is rebound to a host.
    /// </summary>
    internal void Load(RenderResourceSnapshot snapshot)
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
            snapshot);
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
    private readonly Dictionary<RenderSemanticIdentity, MetalGeometryResource>
        _geometries;
    private readonly Dictionary<RenderSemanticIdentity, MetalInstanceResource>
        _instances;
    private readonly Dictionary<RenderSemanticIdentity, MetalTextureResource>
        _textures;
    private readonly Dictionary<RenderSemanticIdentity, MetalSamplerResource>
        _samplers;
    private readonly MetalTextureResource[] _ownedTextures;
    private readonly MTLSamplerState[] _ownedSamplerStates;
    private MTLBuffer _staticBuffer;
    private bool _disposed;

    private MetalResourceSet(
        string contentDigest,
        MTLBuffer staticBuffer,
        long staticBufferByteCount,
        Dictionary<RenderSemanticIdentity, MetalGeometryResource> geometries,
        Dictionary<RenderSemanticIdentity, MetalInstanceResource> instances,
        Dictionary<RenderSemanticIdentity, MetalTextureResource> textures,
        Dictionary<RenderSemanticIdentity, MetalSamplerResource> samplers,
        MetalTextureResource[] ownedTextures,
        MTLSamplerState[] ownedSamplerStates)
    {
        ContentDigest = contentDigest;
        _staticBuffer = staticBuffer;
        StaticBufferByteCount = staticBufferByteCount;
        _geometries = geometries;
        _instances = instances;
        _textures = textures;
        _samplers = samplers;
        _ownedTextures = ownedTextures;
        _ownedSamplerStates = ownedSamplerStates;
        UploadedTextureByteCount = ownedTextures.Sum(
            texture => texture.UploadedByteCount);
    }

    internal string ContentDigest { get; }

    internal int GeometryCount => _geometries.Count;

    internal int InstanceCount => _instances.Count;

    internal int TextureCount => _textures.Count;

    internal int SamplerCount => _samplers.Count;

    internal int NativeSamplerStateCount => _ownedSamplerStates.Length;

    internal long StaticBufferByteCount { get; }

    internal long UploadedTextureByteCount { get; }

    internal static MetalResourceSet Create(
        MTLDevice device,
        MTLCommandQueue commandQueue,
        RenderResourceSnapshot snapshot)
    {
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
            CreateGeometryPlacements(geometryDescriptors, out ulong cursor);
        InstancePlacement[] instancePlacements =
            CreateInstancePlacements(instanceDescriptors, ref cursor);
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
                texturePlans.Length > 0;
            if (!requiresUpload)
            {
                return new MetalResourceSet(
                    snapshot.ContentDigest,
                    staticBuffer,
                    staticBufferByteCount: 0,
                    geometries,
                    instances,
                    textures,
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

            foreach (MetalTextureUploadPlan plan in texturePlans)
            {
                MetalTextureResource texture = CreateTextureResource(
                    device,
                    blit,
                    plan,
                    hasUnifiedMemory,
                    stagingTextures);
                ownedTextures.Add(texture);
                textures.Add(plan.Source.Identity, texture);
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
                snapshot.ContentDigest,
                staticBuffer,
                checked((long)staticBufferBytes),
                geometries,
                instances,
                textures,
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

    public void Dispose()
    {
        if (_disposed)
            return;

        foreach (MetalTextureResource texture in _ownedTextures)
            texture.Dispose();
        foreach (MTLSamplerState sampler in _ownedSamplerStates)
            sampler.Dispose();
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

    private static MetalTextureResource CreateTextureResource(
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
            return new MetalTextureResource(
                plan.Source,
                storage,
                linearView,
                srgbView,
                plan.LinearPixelFormat,
                plan.SrgbPixelFormat,
                plan.UploadKind,
                plan.UploadedByteCount);
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
}
