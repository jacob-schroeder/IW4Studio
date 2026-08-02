namespace IW4.Studio.MapEditor.Compilation.Persistence;

/// <summary>
/// Initial Save As composition boundary for the two-owner duplication
/// candidate. This stays explicit until a composite Gfx/Col owner builder can
/// validate the combined operation graph.
/// </summary>
internal static class StaticModelDuplicationSaveCompositionPolicy
{
    public static IReadOnlyList<string> Validate(
        bool hasDuplicationCandidate,
        bool hasSuppressionCandidate,
        bool hasTranslationCandidate,
        bool hasRemovalCandidate,
        bool hasNestedMapEntsCandidate)
    {
        if (!hasDuplicationCandidate)
            return [];

        var diagnostics = new List<string>(2);
        if (hasNestedMapEntsCandidate)
        {
            diagnostics.Add(
                "A nested MapEnts patch and static-model duplication both " +
                "replace the ColMap owner. Their composite owner validator " +
                "is not implemented, so this save must be split across " +
                "reopened outputs.");
        }
        if (hasSuppressionCandidate ||
            hasTranslationCandidate ||
            hasRemovalCandidate)
        {
            diagnostics.Add(
                "Static-model duplication cannot share a candidate with " +
                "suppression, translation, or removal because each operation " +
                "replaces the GfxMap and ColMap owners. Split the save across " +
                "reopened outputs.");
        }

        return diagnostics.AsReadOnly();
    }
}
