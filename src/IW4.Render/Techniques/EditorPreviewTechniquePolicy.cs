using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.TechniqueSet;

namespace IW4.Render.Techniques;

/// <summary>
/// Deterministic asset-only technique ordering for EditorPreview. This is an
/// editor policy, not a claim about the native per-draw runtime selector.
/// </summary>
public static class EditorPreviewTechniquePolicy
{
    // These slots are the standard lit and emissive comparison slots used by
    // the PS3 material post-load path. EditorPreview tries them before the
    // remaining surface slots, but never treats target-building slots 0..3 or
    // light-volume/tooling slots 27..36 as automatic normal-camera candidates.
    public const int PreferredLitTechniqueSlot =
        (int)MaterialTechniqueType.Lit;
    public const int PreferredEmissiveTechniqueSlot =
        (int)MaterialTechniqueType.Emissive;

    public static IReadOnlyList<int> OrderCandidateSlots(
        IReadOnlyList<MaterialTechniqueSlot> techniqueSlots)
    {
        ArgumentNullException.ThrowIfNull(techniqueSlots);

        var available = new SortedDictionary<int, MaterialTechniqueSlot>();
        foreach (MaterialTechniqueSlot slot in techniqueSlots)
        {
            ArgumentNullException.ThrowIfNull(slot);
            if ((uint)slot.Index >= MaterialAsset.TechniqueSlotCount)
            {
                throw new InvalidDataException(
                    $"Editor technique slot {slot.Index} is outside the 37-slot material table.");
            }

            if (slot.Technique is null)
                continue;
            if (!available.TryAdd(slot.Index, slot))
            {
                throw new InvalidDataException(
                    $"Editor technique policy received duplicate populated slot {slot.Index}.");
            }
        }

        var result = new List<int>(available.Count);
        AddPreferred(PreferredLitTechniqueSlot);
        AddPreferred(PreferredEmissiveTechniqueSlot);
        result.AddRange(available.Keys.Where(slot =>
            slot is >= (int)MaterialTechniqueType.Unlit and
                <= (int)MaterialTechniqueType.LitInstancedSunDfog &&
            !result.Contains(slot)));
        return result.AsReadOnly();

        void AddPreferred(int slot)
        {
            if (available.ContainsKey(slot))
                result.Add(slot);
        }
    }
}
