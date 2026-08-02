using IW4.Assets.Assets.Image;

namespace IW4.Render.Textures;

public static class MapRenderCubeTextureDecoder
{
    public static bool TryDecode(
        GfxImageAsset image,
        out MapRenderDecodedCubeTexture decoded,
        out string reason) =>
        TryDecode(
            image,
            image.PayloadBytes,
            image.Width,
            image.Height,
            Math.Max(1, (int)image.LevelCount),
            out decoded,
            out reason);

    public static bool TryDecode(
        GfxImageAsset image,
        IReadOnlyList<byte> payloadBytes,
        int width,
        int height,
        int mipCount,
        out MapRenderDecodedCubeTexture decoded,
        out string reason)
    {
        decoded = default!;
        if (!GfxImageDecoder.TryDecodeCubeRgba(
                image,
                payloadBytes,
                width,
                height,
                mipCount,
                out DecodedRgbaGfxCube cube,
                out reason))
            return false;

        DecodedRgbaGfxImage top = cube.Faces[0][0];
        decoded = new MapRenderDecodedCubeTexture(
            image.Name ?? "unnamed_cube",
            top.Format,
            cube.Faces.SelectMany(face => face).Any(mip => mip.HasTransparency),
            cube.Faces
                .Select(face => (IReadOnlyList<MapRenderTextureMip>)face
                    .Select(mip => new MapRenderTextureMip(mip.Width, mip.Height, mip.RgbaBytes))
                    .ToArray())
                .ToArray());
        return true;
    }
}
