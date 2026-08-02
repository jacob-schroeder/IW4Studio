using System.Numerics;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;

namespace IW4.Render.Textures;

public sealed record MapRenderTexture(
    string Name,
    int Width,
    int Height,
    string Format,
    byte SamplerState,
    MapRenderSamplerState DecodedSamplerState,
    MapRenderRsxTextureCommandState RsxTextureCommandState,
    bool HasTransparency,
    byte[] RgbaBytes,
    IReadOnlyList<MapRenderTextureMip> MipLevels,
    MapRenderTextureTarget Target = MapRenderTextureTarget.Texture2D,
    IReadOnlyList<MapRenderTextureCubeFace>? CubeFaces = null,
    IReadOnlyList<MapRenderTextureAuthoredSubresource>? AuthoredSubresources = null)
{
    public IReadOnlyList<MapRenderTextureAuthoredSubresource>
        EffectiveAuthoredSubresources => AuthoredSubresources ?? [];

    /// <summary>
    /// True only when the legacy RGBA compatibility representation is a
    /// complete, contiguous set for its own declared faces and mips.
    /// Backend-neutral resource snapshots may retain additional authored mips,
    /// or an entirely authored-only texture, independently. Compatibility
    /// backends must check this property before attempting an RGBA upload.
    /// </summary>
    public bool HasCompleteDecodedRgbaPayload => Target switch
    {
        MapRenderTextureTarget.Texture2D =>
            HasCompleteDecodedTwoDimensionalPayload(),
        MapRenderTextureTarget.TextureCube =>
            HasCompleteDecodedCubePayload(),
        _ => false,
    };

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
            $"rgbaBytes={RgbaBytes.Length}",
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
        Add(RgbaBytes);
        foreach (MapRenderTextureMip? mip in MipLevels)
        {
            if (mip is not null)
                Add(mip.RgbaBytes);
        }
        if (CubeFaces is not null)
        {
            foreach (MapRenderTextureCubeFace? face in CubeFaces)
            {
                if (face is null)
                    continue;
                Add(face.RgbaBytes);
                foreach (MapRenderTextureMip? mip in face.MipLevels)
                {
                    if (mip is not null)
                        Add(mip.RgbaBytes);
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
        if (!HasRgbaLength(RgbaBytes, Width, Height) ||
            MipLevels is null)
        {
            return false;
        }

        int width = Width;
        int height = Height;
        foreach (MapRenderTextureMip? mip in MipLevels)
        {
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
            if (mip is null ||
                mip.Width != width ||
                mip.Height != height ||
                !HasRgbaLength(mip.RgbaBytes, width, height))
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
            MapRenderTextureCubeFace face = faces[layer];
            if (!HasRgbaLength(face.RgbaBytes, Width, Height) ||
                face.MipLevels is null ||
                face.MipLevels.Count != expectedMipCount)
            {
                return false;
            }

            int width = Width;
            int height = Height;
            foreach (MapRenderTextureMip? mip in face.MipLevels)
            {
                width = Math.Max(1, width / 2);
                height = Math.Max(1, height / 2);
                if (mip is null ||
                    mip.Width != width ||
                    mip.Height != height ||
                    !HasRgbaLength(mip.RgbaBytes, width, height))
                {
                    return false;
                }
            }
        }

        MapRenderTextureCubeFace firstFace = faces[0];
        if (RgbaBytes is null ||
            !RgbaBytes.AsSpan().SequenceEqual(firstFace.RgbaBytes))
        {
            return false;
        }
        for (int mipIndex = 0; mipIndex < expectedMipCount; mipIndex++)
        {
            MapRenderTextureMip topMip = MipLevels[mipIndex];
            MapRenderTextureMip faceMip = firstFace.MipLevels[mipIndex];
            if (topMip is null ||
                topMip.Width != faceMip.Width ||
                topMip.Height != faceMip.Height ||
                topMip.RgbaBytes is null ||
                !topMip.RgbaBytes.AsSpan().SequenceEqual(faceMip.RgbaBytes))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasRgbaLength(
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
