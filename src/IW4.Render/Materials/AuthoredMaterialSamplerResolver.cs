using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.Assets;
using IW4.Render.Execution;
using IW4.Render.Geometry;
using IW4.Render.SceneBuilding;
using IW4.Render.Shaders;
using IW4.Render.Textures;
using IW4.Runtime.Assets.Images;

namespace IW4.Render.Materials;

/// <summary>
/// Resolves and decodes the material-owned samplers for one authored pass.
/// World/custom/code resources remain explicit runtime requirements.
/// </summary>
internal static class AuthoredMaterialSamplerResolver
{
    internal const string XModelViewerReflectionProbeResourceIdentity =
        "XMODEL_VIEWER_REFLECTION_PROBE";

    internal static IReadOnlyList<MapRenderMaterialSamplerBinding> Resolve(
        MaterialAsset material,
        AuthoredCameraColorPassSelection selection,
        RenderAssetLookup lookup,
        IGfxImagePayloadResolver imagePayloads,
        MapRenderTextureCache textureCache,
        HashSet<MapRenderTextureCacheKey> failedTextureCacheKeys)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(imagePayloads);
        ArgumentNullException.ThrowIfNull(textureCache);
        ArgumentNullException.ThrowIfNull(failedTextureCacheKeys);

        var bindings = new List<MapRenderMaterialSamplerBinding>();
        var seen = new HashSet<(ushort Destination, uint Hash)>();
        MaterialVertexDeclarationAsset? vertexDeclaration =
            selection.SourcePass.VertexDeclaration ??
            lookup.ResolveVertexDeclaration(
                selection.SourcePass.VertexDeclPointer);
        for (int argumentIndex = 0;
             argumentIndex < selection.Arguments.Count;
             argumentIndex++)
        {
            MaterialShaderArgumentAsset argument =
                selection.Arguments[argumentIndex];
            if (argument.Type !=
                MaterialShaderArgumentType.MaterialPixelSampler)
            {
                continue;
            }

            uint samplerHash = unchecked((uint)argument.ArgumentRaw);
            if (!seen.Add((argument.Dest, samplerHash)))
                continue;

            AuthoredCameraColorTechniqueSelector.TryResolveMaterialTexture(
                material,
                lookup,
                samplerHash,
                requireColor: false,
                out MaterialTextureDef? materialTexture,
                out GfxImageAsset? image);
            byte textureCoordinateSource =
                XSurfaceVertexDecoder.DefaultTexCoordSourceIndex;
            if (materialTexture is not null &&
                RsxShaderInputRouter.TrySelectSamplerSource(
                    selection.SourcePass,
                    argument,
                    vertexDeclaration,
                    materialTexture.Semantic,
                    out byte routedSource))
            {
                textureCoordinateSource = routedSource;
            }
            MapRenderUvRoute uvRoute =
                XSurfaceVertexDecoder.CreateUvRoute(
                    textureCoordinateSource);
            MapRenderTexture? decodedTexture =
                materialTexture is not null && image is not null
                    ? Decode(
                        materialTexture,
                        image,
                        imagePayloads,
                        textureCache,
                        failedTextureCacheKeys)
                    : null;
            bindings.Add(new MapRenderMaterialSamplerBinding(
                argumentIndex,
                argument.Dest,
                samplerHash,
                materialTexture?.Semantic ?? 0,
                decodedTexture?.Name ?? image?.Name ?? string.Empty,
                decodedTexture,
                uvRoute,
                EditorTextureRole: materialTexture is null
                    ? MapRenderEditorMaterialTextureRole.Unknown
                    : MapRenderEditorMaterialTextureRoleClassifier
                        .Classify(materialTexture).Role,
                TextureTableOrdinal:
                    AuthoredCameraColorTechniqueSelector
                        .FindMaterialTextureOrdinal(
                            material,
                            materialTexture)));
        }

        var customSamplers = new MapRenderWorldCustomSamplerSelection(
            selection.Pass.CustomSamplerFlags);
        if (customSamplers.UnknownFlags != 0)
        {
            return bindings;
        }
        if (customSamplers.BindsReflectionProbe)
        {
            bindings.Add(new MapRenderMaterialSamplerBinding(
                SamplerArgIndex: -1,
                SamplerDest: 1,
                SamplerHash: 0,
                TextureSemantic: 0,
                TextureName:
                    XModelViewerReflectionProbeResourceIdentity,
                Texture: null,
                UvRoute: null,
                ExternalResourceIdentity:
                    XModelViewerReflectionProbeResourceIdentity));
        }

        return bindings;
    }

    private static MapRenderTexture? Decode(
        MaterialTextureDef texture,
        GfxImageAsset image,
        IGfxImagePayloadResolver imagePayloads,
        MapRenderTextureCache textureCache,
        HashSet<MapRenderTextureCacheKey> failedTextureCacheKeys)
    {
        MapRenderTextureDecodeRequest request =
            MapRenderTextureDecodeRequest.Create(
                texture,
                image,
                includeAuthoredMipChain: true);
        if (textureCache.TryGetValue(request.Key, out MapRenderTexture cached))
            return cached;
        if (failedTextureCacheKeys.Contains(request.Key))
            return null;

        MapRenderTextureDecodeResult result = MapRenderTextureDecodeBatch.Decode(
            request,
            imagePayloads,
            textureCache.PreferProvenAuthoredPayloads);
        if (result.Texture is not { } decoded ||
            !decoded.HasCompleteDecodedRgbaPayload)
        {
            failedTextureCacheKeys.Add(request.Key);
            return null;
        }

        textureCache.Add(request.Key, decoded);
        return decoded;
    }
}
