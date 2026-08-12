using IW4.Assets.Assets.Image;
using IW4.Assets.Assets.Material;
using IW4.Render.Textures;

namespace IW4.Render.Materials;

/// <summary>
/// Pure, table-order-independent EditorPreview material texture planner.
/// Classification is limited to exact raw tuples; resource resolution and
/// filenames cannot promote an unknown row into a known role.
/// </summary>
public static class MapRenderEditorMaterialTexturePlanner
{
    public static MapRenderEditorMaterialTexturePlan Plan(
        IReadOnlyList<MaterialTextureDef> textures,
        Func<int, MaterialTextureDef, MapRenderEditorMaterialTextureResolution?>? resolve = null)
    {
        ArgumentNullException.ThrowIfNull(textures);

        var bindings = new List<MapRenderEditorMaterialTextureBinding>(textures.Count);
        for (int ordinal = 0; ordinal < textures.Count; ordinal++)
        {
            MaterialTextureDef row = textures[ordinal] ??
                throw new ArgumentException(
                    $"Material texture row {ordinal} is null.",
                    nameof(textures));
            MapRenderEditorMaterialTextureResolution? resolution =
                resolve?.Invoke(ordinal, row);
            GfxImageAsset? image = resolution is null
                ? row.Image
                : resolution.Image;
            MapRenderTexture? resolvedTexture = resolution?.Texture;
            MapRenderEditorMaterialTextureClassification classification =
                MapRenderEditorMaterialTextureRoleClassifier.Classify(row);
            MapRenderSamplerState decodedSampler = MapRenderSamplerDecoder.Decode(
                row.SamplerState,
                image?.Pad0F ?? 0,
                image?.Pad1B ?? 0);

            var binding = new MapRenderEditorMaterialTextureBinding(
                ordinal,
                classification.Role,
                row.NameHash,
                row.NameStart,
                row.NameEnd,
                row.Semantic,
                row.SamplerState,
                image,
                resolvedTexture,
                decodedSampler);
            bindings.Add(binding);
        }

        bindings.Sort(CompareBindings);
        return new MapRenderEditorMaterialTexturePlan(bindings);
    }

    private static int CompareBindings(
        MapRenderEditorMaterialTextureBinding left,
        MapRenderEditorMaterialTextureBinding right)
    {
        int compare = MapRenderEditorMaterialTextureRoleClassifier
            .DeterministicRoleOrder(left.Role)
            .CompareTo(MapRenderEditorMaterialTextureRoleClassifier
                .DeterministicRoleOrder(right.Role));
        if (compare != 0)
            return compare;
        compare = left.NameHash.CompareTo(right.NameHash);
        if (compare != 0)
            return compare;
        compare = left.TextureSemantic.CompareTo(right.TextureSemantic);
        if (compare != 0)
            return compare;
        compare = left.NameStart.CompareTo(right.NameStart);
        if (compare != 0)
            return compare;
        compare = left.NameEnd.CompareTo(right.NameEnd);
        if (compare != 0)
            return compare;
        compare = left.SamplerState.CompareTo(right.SamplerState);
        return compare != 0
            ? compare
            : left.TextureTableOrdinal.CompareTo(right.TextureTableOrdinal);
    }

}

internal static class MapRenderMaterialTextureSelector
{
    internal static bool TryResolveFirst(
        IReadOnlyList<MaterialTextureDef> textures,
        uint? preferredHash,
        byte? requiredSemantic,
        Func<MaterialTextureDef, GfxImageAsset?> resolveImage,
        out MaterialTextureDef? texture,
        out GfxImageAsset? image)
    {
        ArgumentNullException.ThrowIfNull(textures);
        ArgumentNullException.ThrowIfNull(resolveImage);

        foreach (MaterialTextureDef candidate in textures)
        {
            if (preferredHash.HasValue &&
                candidate.NameHash != preferredHash.Value)
            {
                continue;
            }
            if (requiredSemantic.HasValue &&
                candidate.Semantic != requiredSemantic.Value)
            {
                continue;
            }

            GfxImageAsset? candidateImage = resolveImage(candidate);
            if (candidateImage is null)
                continue;

            texture = candidate;
            image = candidateImage;
            return true;
        }

        texture = null;
        image = null;
        return false;
    }
}
