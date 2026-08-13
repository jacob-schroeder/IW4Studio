using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;
using IW4.Render.Assets;
using IW4.Render.Materials;
using IW4.Render.Shaders;

namespace IW4.Render.SceneBuilding;

/// <summary>
/// Selects the representative material-owned sampler for one authored pass.
/// Geometry projection and texture decoding remain caller-owned.
/// </summary>
internal static class AuthoredMaterialSamplerResolver
{
    internal static bool TrySelectPrimary(
        MaterialAsset material,
        MaterialPassAsset sourcePass,
        IReadOnlyList<MaterialShaderArgumentAsset> arguments,
        RenderAssetLookup lookup,
        byte defaultTexCoordSource,
        out AuthoredMaterialPrimarySamplerSelection? selection)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(sourcePass);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(lookup);

        selection = null;
        MaterialVertexDeclarationAsset? vertexDeclaration =
            sourcePass.VertexDeclaration ??
            lookup.ResolveVertexDeclaration(sourcePass.VertexDeclPointer);
        (int SemanticRank, int RouteRank, int DestinationRank, int ArgumentIndex)
            bestRank = (int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue);

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

            uint samplerHash = unchecked((uint)argument.ArgumentRaw);
            if (!MaterialTextureResolver.TryResolve(
                    material,
                    lookup,
                    samplerHash,
                    requireColor: false,
                    out MaterialTextureDef? texture,
                    out GfxImageAsset? image) ||
                texture is null ||
                image is null)
            {
                continue;
            }

            bool engineRouted = RsxShaderInputRouter.TrySelectSamplerSource(
                sourcePass,
                argument,
                vertexDeclaration,
                texture.Semantic,
                out byte routedSource);
            var rank = (
                SemanticRank: texture.Semantic == 0x02 ? 0 : 1,
                RouteRank: engineRouted ? 0 : 1,
                DestinationRank: argument.Dest == 0 ? 0 : 1,
                ArgumentIndex: argumentIndex);
            if (rank.CompareTo(bestRank) >= 0)
                continue;

            bestRank = rank;
            selection = new AuthoredMaterialPrimarySamplerSelection(
                new MaterialSamplerIdentity(
                    argumentIndex,
                    argument.Dest,
                    samplerHash,
                    texture.Semantic),
                texture,
                image,
                engineRouted ? routedSource : defaultTexCoordSource,
                engineRouted);
        }

        return selection is not null;
    }
}

internal sealed record AuthoredMaterialPrimarySamplerSelection(
    MaterialSamplerIdentity Identity,
    MaterialTextureDef Texture,
    GfxImageAsset Image,
    byte TexCoordSource,
    bool TexCoordSourceIsEngineRouted);
