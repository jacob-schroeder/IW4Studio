using System.Buffers;
using System.Numerics;
using IW4.Assets.Assets.GfxMap;
using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Runtime.Assets.Images;

using IW4.Render.Assets;
using IW4.Render.Geometry;
using IW4.Render.Textures;

namespace IW4.Render.SceneBuilding;

public sealed partial class MapSceneBuilder
{
    internal static IReadOnlyList<SkySourceCandidate> SelectSkySources(GfxWorldAsset gfxMap)
    {
        var candidates = new List<SkySourceCandidate>(gfxMap.Skies.Count);
        for (int skyIndex = 0; skyIndex < gfxMap.Skies.Count; skyIndex++)
        {
            GfxSky sky = gfxMap.Skies[skyIndex];
            if (sky.SkyImage is null)
                continue;

            candidates.Add(new SkySourceCandidate(
                skyIndex,
                MapRenderSkySource.GfxSky,
                sky.SkyStartSurfs.ToArray(),
                sky.SkyImage,
                sky.SamplerState));
        }

        return candidates;
    }

    internal static bool TryBuildSkyGeometry(
        GfxWorldAsset gfxMap,
        ReadOnlySpan<byte> vertexBytes,
        IReadOnlyList<ushort> sourceIndices,
        IReadOnlyList<int> skyStartSurfPositions,
        out int[] validSkyStartSurfPositions,
        out int[] resolvedSurfaceIndices,
        out float[] vertices,
        out uint[] indices)
    {
        var preparedBySurface = new Dictionary<int, PreparedWorldSurfaceGeometry>();
        foreach (int skyStartSurfPosition in skyStartSurfPositions.Distinct())
        {
            if (skyStartSurfPosition < 0 ||
                skyStartSurfPosition >= gfxMap.Dpvs.SortedSurfIndex.Count)
            {
                continue;
            }

            int surfaceIndex = gfxMap.Dpvs.SortedSurfIndex[skyStartSurfPosition];
            if (surfaceIndex < 0 ||
                surfaceIndex >= gfxMap.Dpvs.Surfaces.Count ||
                preparedBySurface.ContainsKey(surfaceIndex))
            {
                continue;
            }

            preparedBySurface.Add(
                surfaceIndex,
                PreparedWorldSurfaceGeometryFactory.Create(
                    surfaceIndex,
                    gfxMap.Dpvs.Surfaces[surfaceIndex],
                    vertexBytes,
                    sourceIndices));
        }

        return TryBuildSkyGeometry(
            gfxMap,
            skyStartSurfPositions,
            surfaceIndex => preparedBySurface[surfaceIndex],
            out validSkyStartSurfPositions,
            out resolvedSurfaceIndices,
            out vertices,
            out indices);
    }

    private static bool TryBuildSkyGeometry(
        GfxWorldAsset gfxMap,
        IReadOnlyList<PreparedWorldSurfaceGeometry> preparedWorldSurfaces,
        IReadOnlyList<int> skyStartSurfPositions,
        out int[] validSkyStartSurfPositions,
        out int[] resolvedSurfaceIndices,
        out float[] vertices,
        out uint[] indices)
    {
        ArgumentNullException.ThrowIfNull(preparedWorldSurfaces);
        if (preparedWorldSurfaces.Count != gfxMap.Dpvs.Surfaces.Count)
        {
            throw new ArgumentException(
                "Prepared world surface geometry count does not match the world.",
                nameof(preparedWorldSurfaces));
        }

        return TryBuildSkyGeometry(
            gfxMap,
            skyStartSurfPositions,
            surfaceIndex => preparedWorldSurfaces[surfaceIndex],
            out validSkyStartSurfPositions,
            out resolvedSurfaceIndices,
            out vertices,
            out indices);
    }

    private static bool TryBuildSkyGeometry(
        GfxWorldAsset gfxMap,
        IReadOnlyList<int> skyStartSurfPositions,
        Func<int, PreparedWorldSurfaceGeometry> resolvePreparedSurface,
        out int[] validSkyStartSurfPositions,
        out int[] resolvedSurfaceIndices,
        out float[] vertices,
        out uint[] indices)
    {
        var validStarts = new List<int>();
        var resolvedSurfaces = new List<int>();
        var vertexBuffer = new List<float>();
        var indexBuffer = new List<uint>();
        RenderBounds ignoredBounds = RenderBounds.Empty;

        foreach (int skyStartSurfPosition in skyStartSurfPositions.Distinct())
        {
            if (skyStartSurfPosition < 0 || skyStartSurfPosition >= gfxMap.Dpvs.SortedSurfIndex.Count)
                continue;

            int surfaceIndex = gfxMap.Dpvs.SortedSurfIndex[skyStartSurfPosition];
            if (surfaceIndex < 0 || surfaceIndex >= gfxMap.Dpvs.Surfaces.Count)
                continue;

            int triangleCount = AddSolidSurface(
                resolvePreparedSurface(surfaceIndex),
                vertexBuffer,
                indexBuffer,
                Vector3.Zero,
                includeInBounds: false,
                ref ignoredBounds,
                out _,
                out _,
                out _);
            if (triangleCount == 0)
                continue;

            validStarts.Add(skyStartSurfPosition);
            resolvedSurfaces.Add(surfaceIndex);
        }

        validSkyStartSurfPositions = validStarts.ToArray();
        resolvedSurfaceIndices = resolvedSurfaces.ToArray();
        vertices = vertexBuffer.ToArray();
        indices = indexBuffer.ToArray();
        return indices.Length > 0;
    }

    private static IReadOnlyList<MapRenderSky> BuildSkySubmissions(
        GfxWorldAsset gfxMap,
        IReadOnlyList<PreparedWorldSurfaceGeometry> preparedWorldSurfaces,
        RenderAssetLookup lookup,
        IGfxImagePayloadResolver imageStreams,
        RenderTextureCache textureCache,
        HashSet<RenderTextureCacheKey> failedTextureCacheKeys,
        ref int decodedTextureCount,
        ref int skippedTextureCount)
    {
        var submissions = new List<MapRenderSky>();
        foreach (SkySourceCandidate candidate in SelectSkySources(gfxMap))
        {
            int[] requestedSkyStartSurfPositions = candidate.SkyStartSurfPositions.Count > 0
                ? candidate.SkyStartSurfPositions.Distinct().ToArray()
                : gfxMap.Dpvs.SortedSurfIndex
                    .Select((surfaceIndex, sortedIndex) => new { surfaceIndex = (int)surfaceIndex, sortedIndex })
                    .Where(entry =>
                        entry.surfaceIndex >= 0 &&
                        entry.surfaceIndex < gfxMap.Dpvs.Surfaces.Count &&
                        IsSkyMaterial(
                            gfxMap.Dpvs.Surfaces[entry.surfaceIndex].Material ??
                            lookup.ResolveMaterial(gfxMap.Dpvs.Surfaces[entry.surfaceIndex].MaterialPointer),
                            lookup))
                    .Select(entry => entry.sortedIndex)
                    .ToArray();
            if (!TryBuildSkyGeometry(
                    gfxMap,
                    preparedWorldSurfaces,
                    requestedSkyStartSurfPositions,
                    out int[] validSkyStartSurfPositions,
                    out int[] resolvedSurfaceIndices,
                    out float[] vertices,
                    out uint[] indices) ||
                !TryDecodeSkyTexture(
                    candidate,
                    imageStreams,
                    textureCache,
                    failedTextureCacheKeys,
                    ref decodedTextureCount,
                    ref skippedTextureCount,
                    out Texture? texture) ||
                texture is null)
            {
                continue;
            }

            submissions.Add(new MapRenderSky(
                candidate.WorldSkyIndex,
                candidate.Source,
                validSkyStartSurfPositions,
                resolvedSurfaceIndices,
                texture,
                vertices,
                indices));
        }

        return submissions;
    }

    private static bool TryDecodeSkyTexture(
        SkySourceCandidate candidate,
        IGfxImagePayloadResolver imageStreams,
        RenderTextureCache textureCache,
        HashSet<RenderTextureCacheKey> failedTextureCacheKeys,
        ref int decodedTextureCount,
        ref int skippedTextureCount,
        out Texture? texture)
    {
        GfxImageAsset image = candidate.Image;
        RenderTextureCacheKey cacheKey =
            RenderTextureCacheKey.SkyCube(image, candidate.SamplerState);
        if (textureCache.TryGetValue(cacheKey, out texture))
            return true;
        if (failedTextureCacheKeys.Contains(cacheKey))
        {
            texture = null;
            return false;
        }

        DecodedCubeTexture? decoded = null;
        var authoredSubresources =
            new List<TextureAuthoredSubresource>();
        string authoredFormat = GfxImageDecoder.DescribeFormat(image.Format);
        if (imageStreams.TryResolveMipPayloads(
                image,
                out IReadOnlyList<GfxImagePayload> streamMips,
                out _))
        {
            for (int streamMipIndex = 0;
                 streamMipIndex < streamMips.Count;
                 streamMipIndex++)
            {
                GfxImagePayload streamMip = streamMips[streamMipIndex];
                // Each PS3 image-package part stores one complete cubemap level
                // in layer order. Retain every complete authored level even
                // when the RGBA compatibility decoder cannot consume it.
                if (AuthoredTexturePayloadCapture.TryCaptureCube(
                        image,
                        streamMip.Payload,
                        streamMip.Width,
                        streamMip.Height,
                        mipCount: 1,
                        firstMipLevel: streamMipIndex,
                        authoredFormat,
                        out IReadOnlyList<TextureAuthoredSubresource>
                            capturedMip))
                {
                    authoredSubresources.AddRange(capturedMip);
                }
            }

            bool completeProvenAuthored =
                streamMips.Count > 0 &&
                authoredSubresources.Count ==
                    checked(streamMips.Count * 6) &&
                AuthoredTexturePayloadCapture
                    .IsCompleteProvenChain(
                        authoredSubresources,
                        TextureTarget.TextureCube,
                        streamMips[0].Width,
                        streamMips[0].Height);
            if (!(textureCache.PreferProvenAuthoredPayloads &&
                  completeProvenAuthored))
            {
                var decodedStreamMips =
                    new List<DecodedCubeTexture>(
                        streamMips.Count);
                bool decodedMipChainOpen = true;
                for (int streamMipIndex = 0;
                     streamMipIndex < streamMips.Count;
                     streamMipIndex++)
                {
                    GfxImagePayload streamMip =
                        streamMips[streamMipIndex];
                    if (decodedMipChainOpen &&
                    CubeTextureDecoder.TryDecode(
                        image,
                        streamMip.Payload,
                        streamMip.Width,
                        streamMip.Height,
                        mipCount: 1,
                        out DecodedCubeTexture decodedStreamMip,
                        out _))
                    {
                        decodedStreamMips.Add(decodedStreamMip);
                    }
                    else
                    {
                        decodedMipChainOpen = false;
                    }
                }

                if (decodedStreamMips.Count > 0)
                {
                    DecodedCubeTexture topStreamMip =
                        decodedStreamMips[0];
                    decoded = new DecodedCubeTexture(
                        topStreamMip.Name,
                        topStreamMip.Format,
                        decodedStreamMips.Any(
                            mip => mip.HasTransparency),
                        Enumerable.Range(0, 6)
                            .Select(faceIndex =>
                                (IReadOnlyList<TextureMip>)
                                decodedStreamMips
                                    .SelectMany(
                                        mip =>
                                            mip.Faces[faceIndex])
                                    .ToArray())
                            .ToArray());
                }
            }
        }
        else if (image.PayloadBytes.Count > 0)
        {
            if (AuthoredTexturePayloadCapture.TryCaptureCube(
                    image,
                    image.PayloadBytes,
                    image.Width,
                    image.Height,
                    Math.Max(1, (int)image.LevelCount),
                    firstMipLevel: 0,
                    authoredFormat,
                    out IReadOnlyList<TextureAuthoredSubresource>
                        capturedInline))
            {
                authoredSubresources.AddRange(capturedInline);
            }
            bool completeProvenAuthored =
                AuthoredTexturePayloadCapture
                    .IsCompleteProvenChain(
                        authoredSubresources,
                        TextureTarget.TextureCube,
                        image.Width,
                        image.Height);
            if (!(textureCache.PreferProvenAuthoredPayloads &&
                  completeProvenAuthored) &&
                CubeTextureDecoder.TryDecode(
                    image,
                    image.PayloadBytes,
                    image.Width,
                    image.Height,
                    Math.Max(1, (int)image.LevelCount),
                    out DecodedCubeTexture inlineDecoded,
                    out _))
            {
                decoded = inlineDecoded;
            }
        }

        TextureAuthoredSubresource? authoredTop =
            authoredSubresources.FirstOrDefault(value =>
                value.FaceOrdinal == 0 && value.MipLevel == 0);
        bool canPublishAuthoredOnly =
            textureCache.PreferProvenAuthoredPayloads &&
            authoredTop is not null &&
            AuthoredTexturePayloadCapture
                .IsCompleteProvenChain(
                    authoredSubresources,
                    TextureTarget.TextureCube,
                    authoredTop.Width,
                    authoredTop.Height);
        if (decoded is null && !canPublishAuthoredOnly)
        {
            failedTextureCacheKeys.Add(cacheKey);
            skippedTextureCount++;
            texture = null;
            return false;
        }

        IReadOnlyList<TextureCubeFace>? faces = decoded?.Faces
            .Select(face => new TextureCubeFace(
                face[0].PixelBytes,
                face.Skip(1).ToArray()))
            .ToArray();
        TextureMip? top = decoded?.Faces[0][0];
        texture = new Texture(
            decoded?.Name ?? image.Name ?? "unnamed_cube",
            top?.Width ?? authoredTop!.Width,
            top?.Height ?? authoredTop!.Height,
            decoded?.Format ?? authoredFormat,
            (byte)candidate.SamplerState,
            RsxSamplerDecoder.Decode(
                candidate.SamplerState,
                image.MinLodControl,
                image.UseSrgbReads),
            RsxTextureCommandBuilder.FromImage(image),
            decoded?.HasTransparency ?? true,
            top?.PixelBytes ?? [],
            faces is null ? [] : faces[0].MipLevels,
            TextureTarget.TextureCube,
            faces,
            authoredSubresources);
        textureCache.Add(cacheKey, texture);
        if (decoded is null)
            skippedTextureCount++;
        else
            decodedTextureCount++;
        return true;
    }

    private static bool TryDecodeTexture(
        GfxImageAsset image,
        byte samplerState,
        IGfxImagePayloadResolver imageStreams,
        RenderTextureCache textureCache,
        HashSet<RenderTextureCacheKey> failedTextureCacheKeys,
        bool includeAuthoredMipChain,
        ref int decodedTextureCount,
        ref int skippedTextureCount,
        out Texture? texture)
    {
        RenderTextureDecodeRequest request =
            RenderTextureDecodeRequest.Create(
                image,
                samplerState,
                includeAuthoredMipChain);
        RenderTextureCacheKey key = request.Key;
        if (textureCache.TryGetValue(key, out Texture? cachedTexture))
        {
            texture = cachedTexture;
            return true;
        }

        if (failedTextureCacheKeys.Contains(key))
        {
            texture = null;
            return false;
        }

        RenderTextureDecodeResult result =
            RenderTextureDecodeBatch.Decode(
                request,
                imageStreams,
                textureCache.PreferProvenAuthoredPayloads);
        result.Exception?.Throw();
        if (result.Texture is not { } decodedTexture)
        {
            failedTextureCacheKeys.Add(key);
            skippedTextureCount++;
            texture = null;
            return false;
        }

        texture = decodedTexture;
        textureCache.Add(key, decodedTexture);
        if (decodedTexture.HasCompleteDecodedRgbaPayload)
            decodedTextureCount++;
        else
            skippedTextureCount++;
        return true;
    }

    private static bool TryDecodeTexture(
        GfxImageAsset image,
        MaterialSamplerState samplerState,
        IGfxImagePayloadResolver imageStreams,
        RenderTextureCache textureCache,
        HashSet<RenderTextureCacheKey> failedTextureCacheKeys,
        bool includeAuthoredMipChain,
        ref int decodedTextureCount,
        ref int skippedTextureCount,
        out Texture? texture) =>
        TryDecodeTexture(
            image,
            (byte)samplerState,
            imageStreams,
            textureCache,
            failedTextureCacheKeys,
            includeAuthoredMipChain,
            ref decodedTextureCount,
            ref skippedTextureCount,
            out texture);

}
