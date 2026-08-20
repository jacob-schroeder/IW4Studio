using System.Runtime.Versioning;

using IW4.Render.Resources;
using IW4.Render.Textures;

using SharpMetal.Metal;

namespace IW4.Render.Metal.Resources;

/// <summary>
/// Validates one complete texture before any native allocation and chooses a
/// single Metal pixel format for every face, layer, depth slice, and mip.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MetalTextureUploadPlan
{
    private readonly MetalTextureSubresourceUpload[] _subresources;

    private MetalTextureUploadPlan(
        RenderTextureDescriptor source,
        MTLPixelFormat linearPixelFormat,
        MTLPixelFormat srgbPixelFormat,
        RenderTexturePayloadKind uploadKind,
        MetalTextureSubresourceUpload[] subresources)
    {
        Source = source;
        LinearPixelFormat = linearPixelFormat;
        SrgbPixelFormat = srgbPixelFormat;
        UploadKind = uploadKind;
        _subresources = subresources;
        UploadedByteCount = subresources.Sum(
            subresource => (long)subresource.PayloadByteCount);
    }

    internal RenderTextureDescriptor Source { get; }

    internal MTLPixelFormat LinearPixelFormat { get; }

    internal MTLPixelFormat SrgbPixelFormat { get; }

    internal RenderTexturePayloadKind UploadKind { get; }

    internal long UploadedByteCount { get; }

    internal MTLTextureType TextureType => Source.Dimension switch
    {
        RenderTextureDimension.Texture2D when Source.ArrayLayerCount == 1 =>
            MTLTextureType.Type2D,
        RenderTextureDimension.Texture2D => MTLTextureType.Type2DArray,
        RenderTextureDimension.TextureCube when Source.LayerCount == 1 =>
            MTLTextureType.Cube,
        RenderTextureDimension.TextureCube => MTLTextureType.CubeArray,
        RenderTextureDimension.Texture3D => MTLTextureType.Type3D,
        _ => throw new ArgumentOutOfRangeException(
            nameof(Source),
            Source.Dimension,
            "Unsupported Metal texture dimension.")
    };

    internal ulong NativeArrayLength => Source.Dimension switch
    {
        RenderTextureDimension.Texture2D =>
            checked((ulong)Source.ArrayLayerCount),
        RenderTextureDimension.TextureCube =>
            checked((ulong)Source.LayerCount),
        RenderTextureDimension.Texture3D => 1,
        _ => throw new ArgumentOutOfRangeException(
            nameof(Source),
            Source.Dimension,
            "Unsupported Metal texture dimension.")
    };

    /// <summary>
    /// Texture-view slice ranges use flat faces for cubes and ordinary array
    /// layers for 2D arrays. Three-dimensional textures have no array slices.
    /// </summary>
    internal ulong ViewSliceCount => Source.Dimension ==
        RenderTextureDimension.Texture3D
            ? 1
            : checked((ulong)Source.ArrayLayerCount);

    internal RsxTextureSwizzle Swizzle => RsxTextureSwizzleDecoder.Decode(
        new RsxTextureCommandState(
            Source.Source.TexOffsetPayload,
            Source.Source.TexFormatPayload,
            Source.Source.TexNpotSizePayload,
            Source.Source.TexSize1Payload,
            Source.Source.TexSwizzlePayload));

    internal IReadOnlyList<MetalTextureSubresourceUpload> Subresources =>
        _subresources;

    internal static MetalTextureUploadPlan Create(
        RenderTextureDescriptor source,
        bool supportsBcTextureCompression)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (TryCreateAuthoredBcPlan(
                source,
                out AuthoredBlockCompression blockCompression,
                out RenderTexturePayloadDescriptor[] authoredPayloads))
        {
            if (supportsBcTextureCompression)
            {
                (MTLPixelFormat linear, MTLPixelFormat srgb) =
                    ToMetalBcFormats(blockCompression);
                return new MetalTextureUploadPlan(
                    source,
                    linear,
                    srgb,
                    RenderTexturePayloadKind.Authored,
                    CreateDirectUploads(source, authoredPayloads));
            }

            if (TryCreateDecodedPlan(
                    source,
                    RenderTexturePayloadKind.DecodedRgba8,
                    out RenderTexturePayloadDescriptor[] decodedFallbacks))
            {
                return new MetalTextureUploadPlan(
                    source,
                    MTLPixelFormat.RGBA8Unorm,
                    MTLPixelFormat.RGBA8UnormsRGB,
                    RenderTexturePayloadKind.DecodedRgba8,
                    CreateDirectUploads(source, decodedFallbacks));
            }

            return new MetalTextureUploadPlan(
                source,
                MTLPixelFormat.RGBA8Unorm,
                MTLPixelFormat.RGBA8UnormsRGB,
                RenderTexturePayloadKind.DecodedRgba8,
                CreateDecodedBcFallbacks(
                    source,
                    authoredPayloads,
                    blockCompression));
        }

        if (TryCreateDecodedPlan(
                source,
                RenderTexturePayloadKind.DecodedRg16Float,
                out RenderTexturePayloadDescriptor[] rg16Payloads))
        {
            return new MetalTextureUploadPlan(
                source,
                MTLPixelFormat.RG16Float,
                MTLPixelFormat.Invalid,
                RenderTexturePayloadKind.DecodedRg16Float,
                CreateDirectUploads(source, rg16Payloads));
        }

        if (TryCreateDecodedPlan(
                source,
                RenderTexturePayloadKind.DecodedRgba8,
                out RenderTexturePayloadDescriptor[] rgbaPayloads))
        {
            return new MetalTextureUploadPlan(
                source,
                MTLPixelFormat.RGBA8Unorm,
                MTLPixelFormat.RGBA8UnormsRGB,
                RenderTexturePayloadKind.DecodedRgba8,
                CreateDirectUploads(source, rgbaPayloads));
        }

        throw new InvalidDataException(
            $"Texture {source.Identity} does not contain a complete Metal-" +
            "compatible authored BC or decoded pixel chain.");
    }

    internal MTLTextureDescriptor CreateNativeDescriptor(
        MTLStorageMode storageMode)
    {
        var descriptor = new MTLTextureDescriptor
        {
            TextureType = TextureType,
            PixelFormat = LinearPixelFormat,
            Width = checked((ulong)Source.Width),
            Height = checked((ulong)Source.Height),
            Depth = checked((ulong)Source.Depth),
            MipmapLevelCount = checked((ulong)Source.MipCount),
            ArrayLength = NativeArrayLength,
            SampleCount = 1,
            StorageMode = storageMode,
            CpuCacheMode = MTLCPUCacheMode.DefaultCache,
            Usage = MTLTextureUsage.ShaderRead |
                    MTLTextureUsage.PixelFormatView,
            AllowGPUOptimizedContents = true
        };
        return descriptor;
    }

    private static bool TryCreateDecodedPlan(
        RenderTextureDescriptor source,
        RenderTexturePayloadKind kind,
        out RenderTexturePayloadDescriptor[] payloads)
    {
        payloads = new RenderTexturePayloadDescriptor[
            source.Subresources.Length];
        for (int index = 0; index < source.Subresources.Length; index++)
        {
            RenderTextureSubresourceDescriptor subresource =
                source.Subresources[index];
            RenderTexturePayloadDescriptor? payload = subresource.Payloads
                .FirstOrDefault(candidate => candidate.Kind == kind);
            if (payload is null ||
                !payload.IsDirectUploadLayoutProven ||
                payload.RowPitchBytes != checked(subresource.Width * 4) ||
                payload.SlicePitchBytes != checked(
                    subresource.Width * subresource.Height * 4) ||
                payload.DepthSliceCount != subresource.Depth)
            {
                payloads = [];
                return false;
            }

            payloads[index] = payload;
        }

        return payloads.Length > 0;
    }

    private static bool TryCreateAuthoredBcPlan(
        RenderTextureDescriptor source,
        out AuthoredBlockCompression blockCompression,
        out RenderTexturePayloadDescriptor[] payloads)
    {
        blockCompression = AuthoredBlockCompression.Unknown;
        payloads = [];
        if (source.Dimension == RenderTextureDimension.Texture3D)
            return false;

        var result = new RenderTexturePayloadDescriptor[
            source.Subresources.Length];
        for (int index = 0; index < source.Subresources.Length; index++)
        {
            RenderTextureSubresourceDescriptor subresource =
                source.Subresources[index];
            RenderTexturePayloadDescriptor? payload = subresource.Payloads
                .FirstOrDefault(candidate =>
                    candidate.Kind == RenderTexturePayloadKind.Authored);
            if (payload is null ||
                !payload.IsDirectUploadLayoutProven ||
                payload.DepthSliceCount != 1 ||
                !TryParseBlockCompression(
                    payload.Format,
                    out AuthoredBlockCompression candidateCompression))
            {
                return false;
            }

            if (blockCompression == AuthoredBlockCompression.Unknown)
                blockCompression = candidateCompression;
            else if (blockCompression != candidateCompression)
                return false;

            int blockByteCount = blockCompression ==
                AuthoredBlockCompression.Bc1
                    ? 8
                    : 16;
            int expectedRowPitch = checked(
                Math.Max(1, (subresource.Width + 3) >> 2) *
                blockByteCount);
            int expectedSlicePitch = checked(
                expectedRowPitch *
                Math.Max(1, (subresource.Height + 3) >> 2));
            if (payload.RowPitchBytes != expectedRowPitch ||
                payload.SlicePitchBytes != expectedSlicePitch ||
                payload.Payload.Length != expectedSlicePitch)
            {
                return false;
            }

            result[index] = payload;
        }

        payloads = result;
        return result.Length > 0 &&
            blockCompression != AuthoredBlockCompression.Unknown;
    }

    private static bool TryParseBlockCompression(
        string format,
        out AuthoredBlockCompression compression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        if (format.Contains("DXT23", StringComparison.OrdinalIgnoreCase) ||
            format.Contains("DXT3", StringComparison.OrdinalIgnoreCase) ||
            format.Contains("BC2", StringComparison.OrdinalIgnoreCase))
        {
            compression = AuthoredBlockCompression.Bc2;
            return true;
        }
        if (format.Contains("DXT45", StringComparison.OrdinalIgnoreCase) ||
            format.Contains("DXT5", StringComparison.OrdinalIgnoreCase) ||
            format.Contains("BC3", StringComparison.OrdinalIgnoreCase))
        {
            compression = AuthoredBlockCompression.Bc3;
            return true;
        }
        if (format.Contains("DXT1", StringComparison.OrdinalIgnoreCase) ||
            format.Contains("BC1", StringComparison.OrdinalIgnoreCase))
        {
            compression = AuthoredBlockCompression.Bc1;
            return true;
        }

        compression = AuthoredBlockCompression.Unknown;
        return false;
    }

    private static MetalTextureSubresourceUpload[] CreateDirectUploads(
        RenderTextureDescriptor source,
        IReadOnlyList<RenderTexturePayloadDescriptor> payloads)
    {
        var uploads = new MetalTextureSubresourceUpload[payloads.Count];
        for (int index = 0; index < uploads.Length; index++)
        {
            uploads[index] = new MetalTextureSubresourceUpload(
                source.Subresources[index],
                payloads[index],
                decodedFallback: null);
        }
        return uploads;
    }

    private static MetalTextureSubresourceUpload[] CreateDecodedBcFallbacks(
        RenderTextureDescriptor source,
        IReadOnlyList<RenderTexturePayloadDescriptor> payloads,
        AuthoredBlockCompression compression)
    {
        var uploads = new MetalTextureSubresourceUpload[payloads.Count];
        for (int index = 0; index < uploads.Length; index++)
        {
            RenderTextureSubresourceDescriptor subresource =
                source.Subresources[index];
            byte[] decoded = GfxImageDecoder.DecodeProvenAuthoredBc(
                compression,
                payloads[index].Payload,
                subresource.Width,
                subresource.Height);
            uploads[index] = new MetalTextureSubresourceUpload(
                subresource,
                sourcePayload: null,
                decoded);
        }
        return uploads;
    }

    private static (MTLPixelFormat Linear, MTLPixelFormat Srgb)
        ToMetalBcFormats(AuthoredBlockCompression compression) => compression
            switch
            {
                AuthoredBlockCompression.Bc1 =>
                    (MTLPixelFormat.BC1RGBA, MTLPixelFormat.BC1RGBAsRGB),
                AuthoredBlockCompression.Bc2 =>
                    (MTLPixelFormat.BC2RGBA, MTLPixelFormat.BC2RGBAsRGB),
                AuthoredBlockCompression.Bc3 =>
                    (MTLPixelFormat.BC3RGBA, MTLPixelFormat.BC3RGBAsRGB),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(compression),
                    compression,
                    "A proven BC format is required.")
            };
}

[SupportedOSPlatform("macos")]
internal sealed class MetalTextureSubresourceUpload
{
    private readonly RenderTexturePayloadDescriptor? _sourcePayload;
    private readonly byte[]? _decodedFallback;

    internal MetalTextureSubresourceUpload(
        RenderTextureSubresourceDescriptor descriptor,
        RenderTexturePayloadDescriptor? sourcePayload,
        byte[]? decodedFallback)
    {
        Descriptor = descriptor ??
            throw new ArgumentNullException(nameof(descriptor));
        if ((sourcePayload is null) == (decodedFallback is null))
        {
            throw new ArgumentException(
                "Exactly one texture upload payload is required.");
        }

        _sourcePayload = sourcePayload;
        _decodedFallback = decodedFallback;
        if (sourcePayload is not null)
        {
            BytesPerRow = checked((ulong)sourcePayload.RowPitchBytes);
            BytesPerImage = checked((ulong)sourcePayload.SlicePitchBytes);
            PayloadByteCount = sourcePayload.Payload.Length;
        }
        else
        {
            int rowPitch = checked(descriptor.Width * 4);
            int slicePitch = checked(rowPitch * descriptor.Height);
            int expectedBytes = checked(slicePitch * descriptor.Depth);
            if (decodedFallback!.Length != expectedBytes)
            {
                throw new InvalidDataException(
                    "Decoded BC fallback length does not match its " +
                    "texture subresource.");
            }

            BytesPerRow = checked((ulong)rowPitch);
            BytesPerImage = checked((ulong)slicePitch);
            PayloadByteCount = decodedFallback.Length;
        }
    }

    internal RenderTextureSubresourceDescriptor Descriptor { get; }

    internal ulong BytesPerRow { get; }

    internal ulong BytesPerImage { get; }

    internal int PayloadByteCount { get; }

    internal unsafe void ReplaceStagingTexture(MTLTexture stagingTexture)
    {
        if (stagingTexture.NativePtr == 0)
        {
            throw new ArgumentException(
                "A staging texture is required.",
                nameof(stagingTexture));
        }

        var region = new MTLRegion
        {
            origin = new MTLOrigin(),
            size = new MTLSize
            {
                width = checked((ulong)Descriptor.Width),
                height = checked((ulong)Descriptor.Height),
                depth = checked((ulong)Descriptor.Depth)
            }
        };
        if (_sourcePayload is not null)
        {
            fixed (byte* payload = _sourcePayload.Payload.AsSpan())
            {
                stagingTexture.ReplaceRegion(
                    region,
                    checked((ulong)Descriptor.MipLevel),
                    checked((ulong)Descriptor.ArrayLayer),
                    (nint)payload,
                    BytesPerRow,
                    BytesPerImage);
            }
            return;
        }

        fixed (byte* payload = _decodedFallback)
        {
            stagingTexture.ReplaceRegion(
                region,
                checked((ulong)Descriptor.MipLevel),
                checked((ulong)Descriptor.ArrayLayer),
                (nint)payload,
                BytesPerRow,
                BytesPerImage);
        }
    }
}
