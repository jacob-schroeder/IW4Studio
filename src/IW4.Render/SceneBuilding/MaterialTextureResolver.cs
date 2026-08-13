using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Render.Assets;
using IW4.Render.Materials;

namespace IW4.Render.SceneBuilding;

/// <summary>
/// Resolves canonical material-owned texture rows and images.
/// </summary>
internal static class MaterialTextureResolver
{
    internal static bool TryResolve(
        MaterialAsset material,
        RenderAssetLookup lookup,
        uint? preferredHash,
        bool requireColor,
        out MaterialTextureDef? texture,
        out GfxImageAsset? image)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(lookup);
        return MaterialTextureSelector.TryResolveFirst(
            material.Textures,
            preferredHash,
            requireColor ? (byte)0x02 : null,
            candidate => ResolveCanonicalImage(candidate, lookup),
            out texture,
            out image);
    }

    internal static int FindOrdinal(
        MaterialAsset material,
        MaterialTextureDef? texture)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (texture is null)
            return -1;

        for (int ordinal = 0; ordinal < material.Textures.Count; ordinal++)
        {
            if (ReferenceEquals(material.Textures[ordinal], texture))
                return ordinal;
        }

        return -1;
    }

    private static GfxImageAsset? ResolveCanonicalImage(
        MaterialTextureDef candidate,
        RenderAssetLookup lookup)
    {
        GfxImageAsset? loaded = candidate.Water?.Image ?? candidate.Image;
        if (loaded is not null &&
            lookup.TryResolveCanonicalImage(
                loaded,
                out GfxImageAsset? canonicalLoaded))
        {
            return canonicalLoaded;
        }

        GfxImageAsset? resolved = candidate.Water is { } water
            ? lookup.ResolveImage(water.ImagePointer.Untyped)
            : lookup.ResolveImage(candidate.DataPointer);
        return resolved is not null &&
            lookup.TryResolveCanonicalImage(
                resolved,
                out GfxImageAsset? canonicalResolved)
                ? canonicalResolved
                : null;
    }
}
