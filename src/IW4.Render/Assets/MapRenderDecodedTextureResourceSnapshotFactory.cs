using IW4.Assets.Assets.Image;
using IW4.Render.Textures;
using IW4.Runtime.Assets.Images;

namespace IW4.Render.Assets;

internal static class DecodedTextureResourceSnapshotFactory
{
    internal static bool TryDecode(
        GfxImageAsset image,
        TextureSamplerShape shape,
        IGfxImagePayloadResolver? imageStreams,
        out DecodedTextureResourceSnapshot? resource,
        out string reason) => shape switch
        {
            TextureSamplerShape.TwoDimensional =>
                TryDecodeTwoDimensional(image, imageStreams, out resource, out reason),
            TextureSamplerShape.Cube =>
                TryDecodeCube(image, imageStreams, out resource, out reason),
            _ => ThrowUnknownShape(out resource, out reason)
        };

    private static bool TryDecodeTwoDimensional(
        GfxImageAsset image,
        IGfxImagePayloadResolver? imageStreams,
        out DecodedTextureResourceSnapshot? resource,
        out string reason)
    {
        resource = null;
        IReadOnlyList<GfxImagePayload> streamMips = [];
        bool hasStream = imageStreams?.TryResolveMipPayloads(
            image,
            out streamMips,
            out _) == true && streamMips.Count != 0;
        var subresources = new List<DecodedTextureSubresourceSnapshot>();
        string? format = null;
        DecodedTexturePixelFormat? pixelFormat = null;
        bool hasTransparency = false;
        if (hasStream)
        {
            for (int mipLevel = 0; mipLevel < streamMips.Count; mipLevel++)
            {
                GfxImagePayload mip = streamMips[mipLevel];
                if (!GfxImageDecoder.TryDecodeTexture(
                        image,
                        mip.Payload,
                        mip.Width,
                        mip.Height,
                        out DecodedTextureImage decodedMip,
                        out reason))
                {
                    return false;
                }
                format ??= decodedMip.Format;
                pixelFormat ??= decodedMip.PixelFormat;
                if (pixelFormat != decodedMip.PixelFormat)
                {
                    reason = "Decoded mip chain changed pixel format.";
                    return false;
                }
                hasTransparency |= decodedMip.HasTransparency;
                subresources.Add(new DecodedTextureSubresourceSnapshot(
                    0,
                    mipLevel,
                    decodedMip.Width,
                    decodedMip.Height,
                    decodedMip.PixelBytes));
            }
        }
        else
        {
            if (!GfxImageDecoder.TryDecodeTexture(
                    image,
                    image.PayloadBytes,
                    image.Width,
                    image.Height,
                    out DecodedTextureImage decoded,
                    out reason))
            {
                return false;
            }
            format = decoded.Format;
            pixelFormat = decoded.PixelFormat;
            hasTransparency = decoded.HasTransparency;
            subresources.Add(new DecodedTextureSubresourceSnapshot(
                0,
                0,
                decoded.Width,
                decoded.Height,
                decoded.PixelBytes));
        }

        resource = new DecodedTextureResourceSnapshot(
            image.Name ?? "unnamed_image",
            TextureSamplerShape.TwoDimensional,
            format ?? throw new InvalidOperationException(
                "A decoded 2D texture lost its format identity."),
            pixelFormat ?? throw new InvalidOperationException(
                "A decoded 2D texture lost its pixel-format identity."),
            hasTransparency,
            subresources);
        reason = string.Empty;
        return true;
    }

    private static bool TryDecodeCube(
        GfxImageAsset image,
        IGfxImagePayloadResolver? imageStreams,
        out DecodedTextureResourceSnapshot? resource,
        out string reason)
    {
        resource = null;
        var decodedLevels = new List<DecodedCubeTexture>();
        if (imageStreams?.TryResolveMipPayloads(
                image,
                out IReadOnlyList<GfxImagePayload> streamMips,
                out _) == true && streamMips.Count != 0)
        {
            foreach (GfxImagePayload mip in streamMips)
            {
                if (!CubeTextureDecoder.TryDecode(
                        image,
                        mip.Payload,
                        mip.Width,
                        mip.Height,
                        1,
                        out DecodedCubeTexture decoded,
                        out reason))
                {
                    return false;
                }
                decodedLevels.Add(decoded);
            }
        }
        else
        {
            if (!CubeTextureDecoder.TryDecode(
                    image,
                    image.PayloadBytes,
                    image.Width,
                    image.Height,
                    Math.Max(1, (int)image.LevelCount),
                    out DecodedCubeTexture decoded,
                    out reason))
            {
                return false;
            }
            decodedLevels.Add(decoded);
        }

        var subresources = new List<DecodedTextureSubresourceSnapshot>();
        if (decodedLevels.Count == 1 && decodedLevels[0].Faces[0].Count > 1)
        {
            for (int face = 0; face < 6; face++)
            {
                for (int mip = 0; mip < decodedLevels[0].Faces[face].Count; mip++)
                {
                    TextureMip source = decodedLevels[0].Faces[face][mip];
                    subresources.Add(new DecodedTextureSubresourceSnapshot(
                        face,
                        mip,
                        source.Width,
                        source.Height,
                        source.PixelBytes));
                }
            }
        }
        else
        {
            for (int face = 0; face < 6; face++)
            {
                for (int mip = 0; mip < decodedLevels.Count; mip++)
                {
                    TextureMip source = decodedLevels[mip].Faces[face][0];
                    subresources.Add(new DecodedTextureSubresourceSnapshot(
                        face,
                        mip,
                        source.Width,
                        source.Height,
                        source.PixelBytes));
                }
            }
        }

        DecodedCubeTexture top = decodedLevels[0];
        resource = new DecodedTextureResourceSnapshot(
            image.Name ?? "unnamed_cube",
            TextureSamplerShape.Cube,
            top.Format,
            DecodedTexturePixelFormat.Rgba8Unorm,
            decodedLevels.Any(level => level.HasTransparency),
            subresources);
        reason = string.Empty;
        return true;
    }

    private static bool ThrowUnknownShape(
        out DecodedTextureResourceSnapshot? resource,
        out string reason)
    {
        resource = null;
        reason = "Sampler shape is not a supported 2D or cube tuple.";
        return false;
    }
}
