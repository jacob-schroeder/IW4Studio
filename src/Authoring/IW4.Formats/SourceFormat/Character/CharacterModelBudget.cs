using IW4.Game.Assets.XModel;

namespace IW4.Formats.SourceFormat.Character;

/// <summary>Conservative authoring budgets for custom player geometry, not engine format limits.</summary>
public static class CharacterModelBudget
{
    // Rounded above Rangers Assault A plus head A (8,662 vertices / 9,858
    // triangles / 17 surfaces) and viewhands (3,732 / 5,442 / 6) at LOD0.
    // These constrain individual models; they do not guarantee a frame rate.
    private const int BodyVertices = 9_000;
    private const int BodyTriangles = 10_000;
    private const int BodySections = 18;
    private const int HandsVertices = 4_500;
    private const int HandsTriangles = 6_000;
    private const int HandsSections = 6;

    public static string Summary =>
        $"PS3 budget per detail level: body including head — {BodyVertices:N0} vertices, " +
        $"{BodyTriangles:N0} triangles, {BodySections} mesh sections. " +
        $"Hands — {HandsVertices:N0} vertices, {HandsTriangles:N0} triangles, {HandsSections} mesh sections. " +
        "Final vertex counts include UV seams and hard edges. Oversized source geometry is simplified during import; final models must fit these limits.";

    public static void ValidateBody(XModelAsset model) =>
        Validate(model, "Player body (including head)", BodyVertices, BodyTriangles, BodySections);

    public static void ValidateHands(XModelAsset model) =>
        Validate(model, "Player hands", HandsVertices, HandsTriangles, HandsSections);

    public static CharacterLodCounts Limits(bool viewHands) => viewHands
        ? new CharacterLodCounts(HandsVertices, HandsTriangles, HandsSections)
        : new CharacterLodCounts(BodyVertices, BodyTriangles, BodySections);

    public static CharacterLodCounts Count(IReadOnlyList<XSurface> surfaces) => new(
        surfaces.Sum(surface => (long)surface.VertCount),
        surfaces.Sum(surface => (long)surface.TriCount),
        surfaces.Count);

    public static bool Fits(CharacterLodCounts counts, CharacterLodCounts limits) =>
        counts.Vertices <= limits.Vertices && counts.Triangles <= limits.Triangles &&
        counts.Sections <= limits.Sections;

    private static void Validate(
        XModelAsset model, string label, int maxVertices, int maxTriangles, int maxSections)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.NumLods == 0 || model.NumLods > model.Lods.Count)
            throw new InvalidDataException($"{label} '{model.Name}' has no valid detail levels to assess against the PS3 budget.");

        long previousTriangles = long.MaxValue;
        long previousVertices = long.MaxValue;
        for (int index = 0; index < model.NumLods; index++)
        {
            IReadOnlyList<XSurface> surfaces = model.Lods[index].ModelSurfs?.Surfaces ??
                throw new InvalidDataException($"{label} '{model.Name}' is missing geometry at LOD{index}.");
            ValidateLod(model.Name, surfaces, index, label, maxVertices, maxTriangles, maxSections);
            CharacterLodCounts counts = Count(surfaces);
            if (counts.Triangles >= previousTriangles || counts.Vertices > previousVertices)
                throw new InvalidDataException(
                    $"{label} '{model.Name}' does not become cheaper at LOD{index}: " +
                    $"{counts.Triangles:N0} triangles and {counts.Vertices:N0} vertices, compared with " +
                    $"{previousTriangles:N0} triangles and {previousVertices:N0} vertices at LOD{index - 1}. " +
                    "Each distance LOD must have fewer triangles and no more vertices than the preceding LOD. " +
                    "Re-import the Blender model through Create > Factions to generate reduced LODs.");
            previousTriangles = counts.Triangles;
            previousVertices = counts.Vertices;
        }
    }

    private static void ValidateLod(
        string? modelName, IReadOnlyList<XSurface> surfaces, int index,
        string label, int maxVertices, int maxTriangles, int maxSections)
    {
        if (surfaces.Count == 0)
            throw new InvalidDataException($"{label} '{modelName}' has no geometry at LOD{index}.");
        CharacterLodCounts counts = Count(surfaces);
        if (Fits(counts, new CharacterLodCounts(maxVertices, maxTriangles, maxSections)))
            return;

        throw new InvalidDataException(
            $"{label} '{modelName}' exceeds the PS3 authoring budget at LOD{index}: " +
            $"{counts.Vertices:N0} vertices (maximum {maxVertices:N0}), " +
            $"{counts.Triangles:N0} triangles (maximum {maxTriangles:N0}), " +
            $"{counts.Sections} mesh sections (maximum {maxSections}). " +
            "Reduce the mesh density and material sections in Blender, then use Create > Factions to import the reduced model. " +
            "Final vertex counts include UV seams and hard edges.");
    }
}

public readonly record struct CharacterLodCounts(long Vertices, long Triangles, int Sections);
