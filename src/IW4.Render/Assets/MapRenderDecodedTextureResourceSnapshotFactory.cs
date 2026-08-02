using IW4.Assets.Assets.Image;
using IW4.Render.Shaders;
using IW4.Render.Textures;
using IW4.Runtime.Assets.Images;

namespace IW4.Render.Assets;

internal static class MapRenderDecodedTextureResourceSnapshotFactory
{
    internal static bool TryDecode(
        GfxImageAsset image,
        MapRenderSelectedPassSamplerShape shape,
        IGfxImagePayloadResolver? imageStreams,
        out MapRenderDecodedTextureResourceSnapshot? resource,
        out string reason) => shape switch
        {
            MapRenderSelectedPassSamplerShape.TwoDimensional =>
                TryDecodeTwoDimensional(image, imageStreams, out resource, out reason),
            MapRenderSelectedPassSamplerShape.Cube =>
                TryDecodeCube(image, imageStreams, out resource, out reason),
            _ => ThrowUnknownShape(out resource, out reason)
        };

    private static bool TryDecodeTwoDimensional(
        GfxImageAsset image,
        IGfxImagePayloadResolver? imageStreams,
        out MapRenderDecodedTextureResourceSnapshot? resource,
        out string reason)
    {
        resource = null;
        IReadOnlyList<GfxImagePayload> streamMips = [];
        bool hasStream = imageStreams?.TryResolveMipPayloads(
            image,
            out streamMips,
            out _) == true && streamMips.Count != 0;
        var subresources = new List<MapRenderDecodedTextureSubresourceSnapshot>();
        string? format = null;
        if (hasStream)
        {
            for (int mipLevel = 0; mipLevel < streamMips.Count; mipLevel++)
            {
                GfxImagePayload mip = streamMips[mipLevel];
                if (!GfxImageDecoder.TryDecodeRgba(
                        image,
                        mip.Payload,
                        mip.Width,
                        mip.Height,
                        out DecodedRgbaGfxImage decodedMip,
                        out reason))
                {
                    return false;
                }
                format ??= decodedMip.Format;
                subresources.Add(new MapRenderDecodedTextureSubresourceSnapshot(
                    0,
                    mipLevel,
                    decodedMip.Width,
                    decodedMip.Height,
                    decodedMip.RgbaBytes));
            }
        }
        else
        {
            if (!GfxImageDecoder.TryDecodeRgba(
                    image,
                    image.PayloadBytes,
                    image.Width,
                    image.Height,
                    out DecodedRgbaGfxImage decoded,
                    out reason))
            {
                return false;
            }
            format = decoded.Format;
            subresources.Add(new MapRenderDecodedTextureSubresourceSnapshot(
                0,
                0,
                decoded.Width,
                decoded.Height,
                decoded.RgbaBytes));
        }

        resource = new MapRenderDecodedTextureResourceSnapshot(
            image.Name ?? "unnamed_image",
            MapRenderSelectedPassSamplerShape.TwoDimensional,
            format ?? throw new InvalidOperationException(
                "A decoded 2D texture lost its format identity."),
            subresources.Any(subresource => HasTransparency(subresource.RgbaBytes)),
            subresources);
        reason = string.Empty;
        return true;
    }

    private static bool TryDecodeCube(
        GfxImageAsset image,
        IGfxImagePayloadResolver? imageStreams,
        out MapRenderDecodedTextureResourceSnapshot? resource,
        out string reason)
    {
        resource = null;
        var decodedLevels = new List<MapRenderDecodedCubeTexture>();
        if (imageStreams?.TryResolveMipPayloads(
                image,
                out IReadOnlyList<GfxImagePayload> streamMips,
                out _) == true && streamMips.Count != 0)
        {
            foreach (GfxImagePayload mip in streamMips)
            {
                if (!MapRenderCubeTextureDecoder.TryDecode(
                        image,
                        mip.Payload,
                        mip.Width,
                        mip.Height,
                        1,
                        out MapRenderDecodedCubeTexture decoded,
                        out reason))
                {
                    return false;
                }
                decodedLevels.Add(decoded);
            }
        }
        else
        {
            if (!MapRenderCubeTextureDecoder.TryDecode(
                    image,
                    image.PayloadBytes,
                    image.Width,
                    image.Height,
                    Math.Max(1, (int)image.LevelCount),
                    out MapRenderDecodedCubeTexture decoded,
                    out reason))
            {
                return false;
            }
            decodedLevels.Add(decoded);
        }

        var subresources = new List<MapRenderDecodedTextureSubresourceSnapshot>();
        if (decodedLevels.Count == 1 && decodedLevels[0].Faces[0].Count > 1)
        {
            for (int face = 0; face < 6; face++)
            {
                for (int mip = 0; mip < decodedLevels[0].Faces[face].Count; mip++)
                {
                    MapRenderTextureMip source = decodedLevels[0].Faces[face][mip];
                    subresources.Add(new MapRenderDecodedTextureSubresourceSnapshot(
                        face,
                        mip,
                        source.Width,
                        source.Height,
                        source.RgbaBytes));
                }
            }
        }
        else
        {
            for (int face = 0; face < 6; face++)
            {
                for (int mip = 0; mip < decodedLevels.Count; mip++)
                {
                    MapRenderTextureMip source = decodedLevels[mip].Faces[face][0];
                    subresources.Add(new MapRenderDecodedTextureSubresourceSnapshot(
                        face,
                        mip,
                        source.Width,
                        source.Height,
                        source.RgbaBytes));
                }
            }
        }

        MapRenderDecodedCubeTexture top = decodedLevels[0];
        resource = new MapRenderDecodedTextureResourceSnapshot(
            image.Name ?? "unnamed_cube",
            MapRenderSelectedPassSamplerShape.Cube,
            top.Format,
            decodedLevels.Any(level => level.HasTransparency),
            subresources);
        reason = string.Empty;
        return true;
    }

    private static bool HasTransparency(IReadOnlyList<byte> rgba)
    {
        for (int index = 3; index < rgba.Count; index += 4)
        {
            if (rgba[index] != byte.MaxValue)
                return true;
        }
        return false;
    }

    private static bool ThrowUnknownShape(
        out MapRenderDecodedTextureResourceSnapshot? resource,
        out string reason)
    {
        resource = null;
        reason = "Sampler shape is not a supported 2D or cube tuple.";
        return false;
    }
}
