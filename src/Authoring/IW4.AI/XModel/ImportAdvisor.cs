using IW4.Game.Assets.Material;
using IW4.Game.Assets.TechniqueSet;
using IW4.Game.Assets.XModel;
using IW4.Runtime.Assets;

namespace IW4.AI.XModel;

public static partial class ImportAdvisor
{
    /// <summary>
    /// Describes the same compiled geometry for stock and imported models.
    /// Returns null for an inactive LOD or missing surface geometry.
    /// </summary>
    public static LodFeatures? DescribeLod(XModelAsset model, int lodIndex)
    {
        ArgumentNullException.ThrowIfNull(model);
        int lodCount = model.NumLods == 0 ? model.Lods.Count : model.NumLods;
        if (lodCount > 4 || lodCount > model.Lods.Count || lodIndex < model.MaxLoadedLod || lodIndex >= lodCount)
            return null;

        XModelLodInfo lod = model.Lods[lodIndex];
        if (lod.ModelSurfs is not { } mesh || lod.NumSurfs == 0 ||
            lod.NumSurfs > mesh.Surfaces.Count || mesh.Name?.StartsWith(',') == true)
            return null;

        int triangles = 0, vertices = 0;
        float skinnedVertices = 0, influences = 0;
        var materialNames = new HashSet<string>(StringComparer.Ordinal);
        bool allMaterialsNamed = true;
        float maxTextures = 0, litPasses = 0, alphaTestedSurfaces = 0, blendedSurfaces = 0;
        for (int i = 0; i < lod.NumSurfs; i++)
        {
            XSurface surface = mesh.Surfaces[i];
            triangles += surface.TriCount;
            vertices += surface.VertCount;
            var skinning = CountSkinning(surface);
            skinnedVertices += skinning.Vertices;
            influences += skinning.Influences;

            int materialIndex = lod.SurfIndex + i;
            MaterialAsset? material = materialIndex < model.Materials.Count ? model.Materials[materialIndex] : null;
            string? serializedName = material?.Info.Name;
            string? name = serializedName is null ? null : XAssetStableIdentity.NormalizeLookupName(serializedName);
            if (string.IsNullOrWhiteSpace(name))
                allMaterialsNamed = false;
            else
                materialNames.Add(name);

            if (string.IsNullOrWhiteSpace(serializedName) || XAssetStableIdentity.IsReferenceName(serializedName) ||
                material is null)
            {
                maxTextures = litPasses = alphaTestedSurfaces = blendedSurfaces = float.NaN;
                continue;
            }

            // A name-only editor material also has TextureCount == 0. Only
            // accept a measured zero from a complete PS3 material header.
            bool hasTextureCount = material.TextureCount > 0 || material.StateBitsEntries.Count == MaterialAsset.TechniqueSlotCount;
            maxTextures = Math.Max(maxTextures, hasTextureCount ? material.TextureCount : float.NaN);
            var lit = DescribeLitSurface(material);
            litPasses += lit.Passes;
            alphaTestedSurfaces += lit.AlphaTested;
            blendedSurfaces += lit.Blended;
        }

        return new LodFeatures
        {
            LodIndex = lodIndex,
            LodRampType = (byte)model.LodRampType,
            LodCount = lodCount,
            LodDistance = float.IsFinite(lod.Dist) && lod.Dist >= 0 ? lod.Dist : float.NaN,
            TriangleCount = triangles,
            VertexCount = vertices,
            SurfaceCount = lod.NumSurfs,
            BoneCount = model.NumBones,
            SkinnedVertexCount = skinnedVertices,
            SkinningInfluenceCount = influences,
            DistinctMaterialCount = allMaterialsNamed ? materialNames.Count : float.NaN,
            MaxTexturesPerMaterial = maxTextures,
            LitSurfacePassCount = litPasses,
            AlphaTestedLitSurfaceCount = alphaTestedSurfaces,
            BlendedLitSurfaceCount = blendedSurfaces
        };
    }

    private static (float Vertices, float Influences) CountSkinning(XSurface surface)
    {
        XSurfaceVertexInfo info = surface.VertexInfo;
        int blended = info.Blend0 + info.Blend1 + info.Blend2 + info.Blend3;
        if (surface.VertListCount != surface.VertList.Count ||
            surface.VertList.Sum(group => (int)group.VertCount) + blended != surface.VertCount)
            return (float.NaN, float.NaN);

        return (blended, info.Blend0 + 2 * info.Blend1 + 3 * info.Blend2 + 4 * info.Blend3);
    }

    private static (float Passes, float AlphaTested, float Blended) DescribeLitSurface(MaterialAsset material)
    {
        const int slotIndex = (int)MaterialTechniqueType.Lit;
        MaterialTechniqueSetAsset? set = material.TechniqueSet;
        if (set is null || set.Name?.StartsWith(',') == true || set.TechniqueSlots.Count != MaterialAsset.TechniqueSlotCount)
            return (float.NaN, float.NaN, float.NaN);

        MaterialTechniqueSlot slot = set.TechniqueSlots[slotIndex];
        if (slot.Type != MaterialTechniqueType.Lit)
            return (float.NaN, float.NaN, float.NaN);
        if (slot.Technique is not { } technique)
            return slot.Pointer.Raw == 0 ? (0, 0, 0) : (float.NaN, float.NaN, float.NaN);

        if (material.StateBitsEntries.Count != MaterialAsset.TechniqueSlotCount)
            return (technique.PassCount, float.NaN, float.NaN);
        int stateStart = material.StateBitsEntries[slotIndex].StateBitsIndex;
        bool alphaTested = false, blended = false;
        for (int pass = 0; pass < technique.PassCount; pass++)
        {
            int index = stateStart + pass;
            if (index >= material.StateBits.Count || material.StateBits[index].LoadBits.Count < 2)
                return (technique.PassCount, float.NaN, float.NaN);

            uint flags = material.StateBits[index].LoadBits[0];
            alphaTested |= (flags & (uint)GfxStateBits0Flags.AlphaTestDisabled) == 0;
            blended |= (flags & GfxStateBitsEncoding.BlendOperationRgbMask) != 0;
        }
        return (technique.PassCount, alphaTested ? 1 : 0, blended ? 1 : 0);
    }
}
