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
        MapRenderBounds ignoredBounds = MapRenderBounds.Empty;

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
        MapRenderTextureCache textureCache,
        HashSet<MapRenderTextureCacheKey> failedTextureCacheKeys,
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
                    out MapRenderTexture? texture) ||
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
        MapRenderTextureCache textureCache,
        HashSet<MapRenderTextureCacheKey> failedTextureCacheKeys,
        ref int decodedTextureCount,
        ref int skippedTextureCount,
        out MapRenderTexture? texture)
    {
        GfxImageAsset image = candidate.Image;
        MapRenderTextureCacheKey cacheKey =
            MapRenderTextureCacheKey.Sky(image, candidate.SamplerState);
        if (textureCache.TryGetValue(cacheKey, out texture))
            return true;
        if (failedTextureCacheKeys.Contains(cacheKey))
        {
            texture = null;
            return false;
        }

        MapRenderDecodedCubeTexture? decoded = null;
        var authoredSubresources =
            new List<MapRenderTextureAuthoredSubresource>();
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
                if (MapRenderAuthoredTexturePayloadCapture.TryCaptureCube(
                        image,
                        streamMip.Payload,
                        streamMip.Width,
                        streamMip.Height,
                        mipCount: 1,
                        firstMipLevel: streamMipIndex,
                        authoredFormat,
                        out IReadOnlyList<MapRenderTextureAuthoredSubresource>
                            capturedMip))
                {
                    authoredSubresources.AddRange(capturedMip);
                }
            }

            bool completeProvenAuthored =
                streamMips.Count > 0 &&
                authoredSubresources.Count ==
                    checked(streamMips.Count * 6) &&
                MapRenderAuthoredTexturePayloadCapture
                    .IsCompleteProvenChain(
                        authoredSubresources,
                        MapRenderTextureTarget.TextureCube,
                        streamMips[0].Width,
                        streamMips[0].Height);
            if (!(textureCache.PreferProvenAuthoredPayloads &&
                  completeProvenAuthored))
            {
                var decodedStreamMips =
                    new List<MapRenderDecodedCubeTexture>(
                        streamMips.Count);
                bool decodedMipChainOpen = true;
                for (int streamMipIndex = 0;
                     streamMipIndex < streamMips.Count;
                     streamMipIndex++)
                {
                    GfxImagePayload streamMip =
                        streamMips[streamMipIndex];
                    if (decodedMipChainOpen &&
                    MapRenderCubeTextureDecoder.TryDecode(
                        image,
                        streamMip.Payload,
                        streamMip.Width,
                        streamMip.Height,
                        mipCount: 1,
                        out MapRenderDecodedCubeTexture decodedStreamMip,
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
                    MapRenderDecodedCubeTexture topStreamMip =
                        decodedStreamMips[0];
                    decoded = new MapRenderDecodedCubeTexture(
                        topStreamMip.Name,
                        topStreamMip.Format,
                        decodedStreamMips.Any(
                            mip => mip.HasTransparency),
                        Enumerable.Range(0, 6)
                            .Select(faceIndex =>
                                (IReadOnlyList<MapRenderTextureMip>)
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
            if (MapRenderAuthoredTexturePayloadCapture.TryCaptureCube(
                    image,
                    image.PayloadBytes,
                    image.Width,
                    image.Height,
                    Math.Max(1, (int)image.LevelCount),
                    firstMipLevel: 0,
                    authoredFormat,
                    out IReadOnlyList<MapRenderTextureAuthoredSubresource>
                        capturedInline))
            {
                authoredSubresources.AddRange(capturedInline);
            }
            bool completeProvenAuthored =
                MapRenderAuthoredTexturePayloadCapture
                    .IsCompleteProvenChain(
                        authoredSubresources,
                        MapRenderTextureTarget.TextureCube,
                        image.Width,
                        image.Height);
            if (!(textureCache.PreferProvenAuthoredPayloads &&
                  completeProvenAuthored) &&
                MapRenderCubeTextureDecoder.TryDecode(
                    image,
                    image.PayloadBytes,
                    image.Width,
                    image.Height,
                    Math.Max(1, (int)image.LevelCount),
                    out MapRenderDecodedCubeTexture inlineDecoded,
                    out _))
            {
                decoded = inlineDecoded;
            }
        }

        MapRenderTextureAuthoredSubresource? authoredTop =
            authoredSubresources.FirstOrDefault(value =>
                value.FaceOrdinal == 0 && value.MipLevel == 0);
        bool canPublishAuthoredOnly =
            textureCache.PreferProvenAuthoredPayloads &&
            authoredTop is not null &&
            MapRenderAuthoredTexturePayloadCapture
                .IsCompleteProvenChain(
                    authoredSubresources,
                    MapRenderTextureTarget.TextureCube,
                    authoredTop.Width,
                    authoredTop.Height);
        if (decoded is null && !canPublishAuthoredOnly)
        {
            failedTextureCacheKeys.Add(cacheKey);
            skippedTextureCount++;
            texture = null;
            return false;
        }

        IReadOnlyList<MapRenderTextureCubeFace>? faces = decoded?.Faces
            .Select(face => new MapRenderTextureCubeFace(
                face[0].RgbaBytes,
                face.Skip(1).ToArray()))
            .ToArray();
        MapRenderTextureMip? top = decoded?.Faces[0][0];
        texture = new MapRenderTexture(
            decoded?.Name ?? image.Name ?? "unnamed_cube",
            top?.Width ?? authoredTop!.Width,
            top?.Height ?? authoredTop!.Height,
            decoded?.Format ?? authoredFormat,
            candidate.SamplerState,
            MapRenderSamplerDecoder.Decode(candidate.SamplerState, image.Pad0F, image.Pad1B),
            MapRenderRsxTextureCommandBuilder.FromImage(image),
            decoded?.HasTransparency ?? true,
            top?.RgbaBytes ?? [],
            faces is null ? [] : faces[0].MipLevels,
            MapRenderTextureTarget.TextureCube,
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
        MaterialTextureDef materialTexture,
        GfxImageAsset image,
        IGfxImagePayloadResolver imageStreams,
        MapRenderTextureCache textureCache,
        HashSet<MapRenderTextureCacheKey> failedTextureCacheKeys,
        bool includeAuthoredMipChain,
        ref int decodedTextureCount,
        ref int skippedTextureCount,
        out MapRenderTexture? texture)
    {
        MapRenderTextureDecodeRequest request =
            MapRenderTextureDecodeRequest.Create(
                materialTexture,
                image,
                includeAuthoredMipChain);
        MapRenderTextureCacheKey key = request.Key;
        if (textureCache.TryGetValue(key, out MapRenderTexture? cachedTexture))
        {
            texture = cachedTexture;
            return true;
        }

        if (failedTextureCacheKeys.Contains(key))
        {
            texture = null;
            return false;
        }

        MapRenderTextureDecodeResult result =
            MapRenderTextureDecodeBatch.Decode(
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

}
