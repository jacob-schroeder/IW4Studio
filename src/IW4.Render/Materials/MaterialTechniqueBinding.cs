using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Render.Materials;

/// <summary>
/// Immutable material-owned technique resolution used by one frame planner.
/// The sorted index is captured from the material draw-surface key rebuilt by
/// the runtime material post-load pass.
/// </summary>
public sealed class MaterialTechniqueBinding
{
    internal MaterialTechniqueBinding(
        MaterialAsset material,
        MaterialTechniqueSetAsset techniqueSet,
        IReadOnlyList<MaterialTechniqueSlot> resolvedTechniqueSlots)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(techniqueSet);
        ArgumentNullException.ThrowIfNull(resolvedTechniqueSlots);
        if (material.TechniqueSet is not null &&
            !ReferenceEquals(material.TechniqueSet, techniqueSet) &&
            !SharesCanonicalPoolSlot(material.TechniqueSet, techniqueSet))
        {
            throw new ArgumentException(
                "The resolved technique set is not owned by the supplied material.",
                nameof(techniqueSet));
        }

        Material = material;
        TechniqueSet = techniqueSet;
        MaterialSortedIndex = material.Info.DrawSurf.MaterialSortedIndex;
        TechniqueSlots = Array.AsReadOnly(resolvedTechniqueSlots.ToArray());
    }

    public int MaterialSortedIndex { get; }

    public MaterialAsset Material { get; }

    public MaterialTechniqueSetAsset TechniqueSet { get; }

    public IReadOnlyList<MaterialTechniqueSlot> TechniqueSlots { get; }

    private static bool SharesCanonicalPoolSlot(
        MaterialTechniqueSetAsset first,
        MaterialTechniqueSetAsset second) =>
        first.RuntimeAddress?.AssetPoolAddress is { } firstAddress &&
        second.RuntimeAddress?.AssetPoolAddress is { } secondAddress &&
        firstAddress == secondAddress;
}
