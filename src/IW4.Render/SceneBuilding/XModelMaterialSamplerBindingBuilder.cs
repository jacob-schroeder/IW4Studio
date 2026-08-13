using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.Assets;
using IW4.Render.Geometry;
using IW4.Render.Materials;
using IW4.Render.Shaders;
using IW4.Render.Textures;
using IW4.Runtime.Assets.Images;

namespace IW4.Render.SceneBuilding;

/// <summary>
/// Projects material-owned authored samplers onto XSurface UV routes and
/// injects XModel-viewer custom resources.
/// </summary>
internal static class XModelMaterialSamplerBindingBuilder
{
    internal static IReadOnlyList<MaterialSamplerBinding> Build(
        MaterialAsset material,
        MaterialPassAsset sourcePass,
        IReadOnlyList<MaterialShaderArgumentAsset> arguments,
        RenderAssetLookup lookup,
        IGfxImagePayloadResolver imagePayloads,
        RenderTextureCache textureCache,
        HashSet<RenderTextureCacheKey> failedTextureCacheKeys)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(sourcePass);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(imagePayloads);
        ArgumentNullException.ThrowIfNull(textureCache);
        ArgumentNullException.ThrowIfNull(failedTextureCacheKeys);

        var bindings = new List<MaterialSamplerBinding>();
        var seen = new HashSet<(ushort Destination, uint Hash)>();
        MaterialVertexDeclarationAsset? vertexDeclaration =
            sourcePass.VertexDeclaration ??
            lookup.ResolveVertexDeclaration(sourcePass.VertexDeclPointer);
        for (int argumentIndex = 0;
             argumentIndex < arguments.Count;
             argumentIndex++)
        {
            MaterialShaderArgumentAsset argument = arguments[argumentIndex];
            if (argument.Type !=
                MaterialShaderArgumentType.MaterialPixelSampler)
            {
                continue;
            }

            uint samplerHash = argument.MaterialNameHash;
            if (!seen.Add((argument.Dest, samplerHash)))
                continue;

            MaterialTextureResolver.TryResolve(
                material,
                lookup,
                samplerHash,
                requireColor: false,
                out MaterialTextureDef? materialTexture,
                out GfxImageAsset? image);
            MaterialStreamSource textureCoordinateSource =
                XSurfaceVertexDecoder.DefaultTexCoordSource;
            if (materialTexture is not null &&
                RsxShaderInputRouter.TrySelectSamplerSource(
                    sourcePass,
                    argument,
                    vertexDeclaration,
                    materialTexture.Semantic,
                    out MaterialStreamSource routedSource))
            {
                textureCoordinateSource = routedSource;
            }
            UvRoute uvRoute =
                XSurfaceVertexDecoder.CreateUvRoute(
                    textureCoordinateSource);
            Texture? decodedTexture =
                materialTexture is not null && image is not null
                    ? Decode(
                        materialTexture,
                        image,
                        imagePayloads,
                        textureCache,
                        failedTextureCacheKeys)
                    : null;
            bindings.Add(new MaterialSamplerBinding(
                new MaterialSamplerIdentity(
                    argumentIndex,
                    argument.Dest,
                    samplerHash,
                    materialTexture?.Semantic ?? 0),
                decodedTexture?.Name ?? image?.Name ?? string.Empty,
                decodedTexture,
                uvRoute,
                EditorTextureRole: materialTexture is null
                    ? EditorMaterialTextureRole.Unknown
                    : EditorMaterialTextureRoleClassifier
                        .Classify(materialTexture).Role,
                TextureTableOrdinal: MaterialTextureResolver.FindOrdinal(
                    material,
                    materialTexture)));
        }

        var customSamplers = new MaterialCustomSamplerSelection(
            sourcePass.CustomSamplerFlags);
        if (customSamplers.UnknownFlags != 0)
            return bindings;
        if (customSamplers.BindsReflectionProbe)
        {
            bindings.Add(new MaterialSamplerBinding(
                Identity: new MaterialSamplerIdentity(
                    SamplerArgIndex: -1,
                    SamplerDest: 1,
                    SamplerHash: 0,
                    TextureSemantic: 0),
                TextureName:
                    XModelRenderAuthoredPass
                        .ViewerReflectionProbeResourceIdentity,
                Texture: null,
                UvRoute: null,
                ExternalResourceIdentity:
                    XModelRenderAuthoredPass
                        .ViewerReflectionProbeResourceIdentity));
        }

        return bindings;
    }

    private static Texture? Decode(
        MaterialTextureDef texture,
        GfxImageAsset image,
        IGfxImagePayloadResolver imagePayloads,
        RenderTextureCache textureCache,
        HashSet<RenderTextureCacheKey> failedTextureCacheKeys)
    {
        RenderTextureDecodeRequest request =
            RenderTextureDecodeRequest.Create(
                image,
                texture.SamplerState,
                includeAuthoredMipChain: true);
        if (textureCache.TryGetValue(request.Key, out Texture cached))
            return cached;
        if (failedTextureCacheKeys.Contains(request.Key))
            return null;

        RenderTextureDecodeResult result = RenderTextureDecodeBatch.Decode(
            request,
            imagePayloads,
            textureCache.PreferProvenAuthoredPayloads);
        if (result.Texture is not { } decoded ||
            !decoded.HasCompleteDecodedPayload)
        {
            failedTextureCacheKeys.Add(request.Key);
            return null;
        }

        textureCache.Add(request.Key, decoded);
        return decoded;
    }
}
