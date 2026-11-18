using IW4.Game.Assets.Material;
using IW4.Game.Assets.TechniqueSet;

namespace IW4.Studio.Documents;

public sealed record ShaderUsage(
    MaterialTechniqueSetAsset TechniqueSet,
    int TechniqueSlot,
    MaterialTechniqueAsset Technique,
    int PassIndex,
    MaterialPassAsset Pass,
    IReadOnlyList<MaterialAsset> Materials)
{
    internal static IReadOnlyList<ShaderUsage> Capture(
        MaterialShaderAsset shader,
        IEnumerable<MaterialTechniqueSetAsset> techniqueSets,
        IEnumerable<MaterialAsset> materials)
    {
        ILookup<MaterialTechniqueSetAsset, MaterialAsset> materialsByTechnique = materials
            .Where(material => material.TechniqueSet is not null)
            .ToLookup<MaterialAsset, MaterialTechniqueSetAsset>(
                material => material.TechniqueSet!, ReferenceEqualityComparer.Instance);
        var usages = new List<ShaderUsage>();
        foreach (MaterialTechniqueSetAsset techniqueSet in
                 techniqueSets.Concat(materialsByTechnique.Select(group => group.Key))
                     .Distinct<MaterialTechniqueSetAsset>(ReferenceEqualityComparer.Instance))
        {
            foreach (MaterialTechniqueSlot slot in techniqueSet.TechniqueSlots)
            {
                if (slot.Technique is not { } technique)
                    continue;
                for (int passIndex = 0; passIndex < technique.Passes.Count; passIndex++)
                {
                    MaterialPassAsset pass = technique.Passes[passIndex];
                    MaterialShaderAsset? bound = shader.Kind == MaterialShaderKind.Vertex
                        ? pass.VertexShader
                        : pass.PixelShader;
                    if (!ReferenceEquals(bound, shader))
                        continue;

                    IReadOnlyList<MaterialAsset> boundMaterials = Array.AsReadOnly(
                        materialsByTechnique[techniqueSet]
                            .Where(material =>
                                (uint)slot.Index < (uint)material.StateBitsEntries.Count &&
                                material.StateBitsEntries[slot.Index].StateBitsIndex + passIndex <
                                    material.StateBits.Count)
                            .OrderBy(material => material.Info.Name, StringComparer.OrdinalIgnoreCase)
                            .ToArray());
                    usages.Add(new ShaderUsage(techniqueSet, slot.Index, technique, passIndex, pass, boundMaterials));
                }
            }
        }

        return Array.AsReadOnly(usages
            .OrderBy(usage => usage.TechniqueSet.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(usage => usage.TechniqueSlot)
            .ThenBy(usage => usage.PassIndex)
            .ToArray());
    }
}
