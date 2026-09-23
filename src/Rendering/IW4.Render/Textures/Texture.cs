
namespace IW4.Render.Textures;

public sealed record Texture(
    string Name,
    int Width,
    int Height,
    string Format,
    byte SamplerState,
    RsxSamplerState DecodedSamplerState,
    RsxTextureCommandState RsxTextureCommandState,
    bool HasTransparency,
    byte[] PixelBytes,
    IReadOnlyList<TextureMip> MipLevels,
    TextureTarget Target = TextureTarget.Texture2D,
    IReadOnlyList<TextureCubeFace>? CubeFaces = null,
    IReadOnlyList<TextureAuthoredSubresource>? AuthoredSubresources = null,
    DecodedTexturePixelFormat PixelFormat =
        DecodedTexturePixelFormat.Rgba8Unorm)
{
    public IReadOnlyList<TextureAuthoredSubresource>
        EffectiveAuthoredSubresources => AuthoredSubresources ?? [];

    /// <summary>
    /// True only when the legacy RGBA compatibility representation is a
    /// complete, contiguous set for its own declared faces and mips.
    /// Backend-neutral resource snapshots may retain additional authored mips,
    /// or an entirely authored-only texture, independently. Compatibility
    /// backends must check this property before attempting an RGBA upload.
    /// </summary>
    public bool HasCompleteDecodedPayload => Target switch
    {
        TextureTarget.Texture2D =>
            HasCompleteDecodedTwoDimensionalPayload(),
        TextureTarget.TextureCube =>
            HasCompleteDecodedCubePayload(),
        _ => false,
    };

    public bool HasCompleteDecodedRgbaPayload =>
        PixelFormat == DecodedTexturePixelFormat.Rgba8Unorm &&
        HasCompleteDecodedPayload;

    public string BindingIdentity => string.Join(
        ':',
        [
            Name,
            Target.ToString(),
            Format,
            $"{Width}x{Height}",
            $"sampler={SamplerState:X2}",
            $"mips={MipLevels.Count}",
            $"cubeFaces={CubeFaces?.Count ?? 0}",
            $"pixelFormat={PixelFormat}",
            $"pixelBytes={PixelBytes.Length}",
            $"authoredSubresources={EffectiveAuthoredSubresources.Count}",
            $"authoredBytes={EffectiveAuthoredSubresources.Sum(value => (long)value.SlicePitchBytes)}",
            $"filter={DecodedSamplerState.RsxTexFilterPayload:X8}",
            $"wrap={DecodedSamplerState.RsxTexWrapPayload:X8}",
            $"enable={DecodedSamplerState.RsxTexEnablePayload:X8}",
            $"cache={DecodedSamplerState.RsxSamplerCachePayload:X8}"
        ]);

    internal long DecodedFallbackByteCount
    {
        get
        {
            var payloads = new HashSet<byte[]>(
                ReferenceEqualityComparer.Instance);
            VisitDecodedFallbackPayloads(
                payload =>
                {
                    payloads.Add(payload);
                });
            return payloads.Sum(payload => (long)payload.Length);
        }
    }

    internal void VisitDecodedFallbackPayloads(
        Action<byte[]> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        Add(PixelBytes);
        foreach (TextureMip? mip in MipLevels)
        {
            if (mip is not null)
                Add(mip.PixelBytes);
        }
        if (CubeFaces is not null)
        {
            foreach (TextureCubeFace? face in CubeFaces)
            {
                if (face is null)
                    continue;
                Add(face.RgbaBytes);
                foreach (TextureMip? mip in face.MipLevels)
                {
                    if (mip is not null)
                        Add(mip.PixelBytes);
                }
            }
        }

        void Add(byte[]? payload)
        {
            if (payload is { Length: > 0 })
                visitor(payload);
        }
    }

    private bool HasCompleteDecodedTwoDimensionalPayload()
    {
        if (!HasPixelLength(PixelBytes, Width, Height) ||
            MipLevels is null)
        {
            return false;
        }

        int width = Width;
        int height = Height;
        foreach (TextureMip? mip in MipLevels)
        {
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
            if (mip is null ||
                mip.Width != width ||
                mip.Height != height ||
                !HasPixelLength(mip.PixelBytes, width, height))
            {
                return false;
            }
        }

        return true;
    }

    private bool HasCompleteDecodedCubePayload()
    {
        if (Width != Height ||
            CubeFaces is not { Count: 6 } faces ||
            faces.Any(face => face is null) ||
            MipLevels is null)
        {
            return false;
        }

        int expectedMipCount = faces[0].MipLevels?.Count ?? -1;
        if (expectedMipCount < 0 ||
            MipLevels.Count != expectedMipCount)
        {
            return false;
        }

        for (int layer = 0; layer < faces.Count; layer++)
        {
            TextureCubeFace face = faces[layer];
            if (!HasPixelLength(face.RgbaBytes, Width, Height) ||
                face.MipLevels is null ||
                face.MipLevels.Count != expectedMipCount)
            {
                return false;
            }

            int width = Width;
            int height = Height;
            foreach (TextureMip? mip in face.MipLevels)
            {
                width = Math.Max(1, width / 2);
                height = Math.Max(1, height / 2);
                if (mip is null ||
                    mip.Width != width ||
                    mip.Height != height ||
                    !HasPixelLength(mip.PixelBytes, width, height))
                {
                    return false;
                }
            }
        }

        TextureCubeFace firstFace = faces[0];
        if (PixelFormat != DecodedTexturePixelFormat.Rgba8Unorm ||
            PixelBytes is null ||
            !PixelBytes.AsSpan().SequenceEqual(firstFace.RgbaBytes))
        {
            return false;
        }
        for (int mipIndex = 0; mipIndex < expectedMipCount; mipIndex++)
        {
            TextureMip topMip = MipLevels[mipIndex];
            TextureMip faceMip = firstFace.MipLevels[mipIndex];
            if (topMip is null ||
                topMip.Width != faceMip.Width ||
                topMip.Height != faceMip.Height ||
                topMip.PixelBytes is null ||
                !topMip.PixelBytes.AsSpan().SequenceEqual(faceMip.PixelBytes))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasPixelLength(
        byte[]? payload,
        int width,
        int height)
    {
        if (payload is null || width <= 0 || height <= 0)
            return false;
        try
        {
            return payload.Length == checked(width * height * 4);
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
