using IW4.Assets.Assets.XModel;
using IW4.Assets.XModel.Export;

namespace IW4.Studio.Documents;

public sealed class XModelLodDraft
{
    internal XModelLodDraft(int slotIndex, float distance, XModelLodInfo? baselineLod, XModelExportDocument? importedDocument, string? importSource)
    {
        SlotIndex = slotIndex;
        Distance = distance;
        BaselineLod = baselineLod;
        ImportedDocument = importedDocument;
        ImportSource = importSource;
    }

    public int SlotIndex { get; }
    public float Distance { get; }
    public XModelLodInfo? BaselineLod { get; }
    public XModelExportDocument? ImportedDocument { get; }
    public string? ImportSource { get; }
    public bool IsOccupied => BaselineLod is not null || ImportedDocument is not null;
    public bool IsImported => ImportedDocument is not null;
    internal XModelLodDraft Clone() => new(SlotIndex, Distance, BaselineLod, ImportedDocument is null ? null : Freeze(ImportedDocument), ImportSource);

    internal static XModelExportDocument Freeze(XModelExportDocument document) => new(
        Array.AsReadOnly(document.Bones.Select(b => new XModelExportBone(b.Name, b.ParentIndex, b.GlobalOffset, b.GlobalRotation)).ToArray()),
        Array.AsReadOnly(document.Vertices.Select(v => new XModelExportVertex(v.Position, Array.AsReadOnly(v.Weights.Select(w => new XModelExportBoneWeight(w.BoneIndex, w.Weight)).ToArray()))).ToArray()),
        Array.AsReadOnly(document.Triangles.Select(t => new XModelExportTriangle(t.ObjectIndex, t.MaterialIndex, Copy(t.First), Copy(t.Second), Copy(t.Third))).ToArray()),
        Array.AsReadOnly(document.Objects.Select(o => new XModelExportObject(o.SurfaceIdentity)).ToArray()),
        Array.AsReadOnly(document.Materials.Select(m => new XModelExportMaterial(m.Name, m.ColorMapPath)).ToArray()));

    private static XModelExportCorner Copy(XModelExportCorner c) => new(c.VertexIndex, c.Normal, c.Color, c.Uv0);
}

internal static class XModelLodAssemblyValidator
{
    public static IReadOnlyList<AssetValidationIssue> Validate(XModelDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var issues = new List<AssetValidationIssue>();
        IReadOnlyList<XModelLodDraft> lods = draft.LodAssembly;
        bool emptySeen = false;
        for (int i = 0; i < lods.Count; i++)
        {
            XModelLodDraft lod = lods[i];
            if (!lod.IsOccupied) emptySeen = true;
            else if (emptySeen) Error(issues, $"xmodel.lods[{i}]", "Active LODs must form a contiguous prefix.");
            if (!float.IsFinite(lod.Distance) || lod.Distance < 0) Error(issues, $"xmodel.lods[{i}].distance", "LOD distance must be finite and nonnegative.");
            if (i > 0 && lod.IsOccupied && lods[i - 1].IsOccupied && lod.Distance <= lods[i - 1].Distance) Error(issues, $"xmodel.lods[{i}].distance", "Active LOD distances must be strictly increasing.");
            if (lod.ImportedDocument is not null) ValidateImported(draft.Model, lod.ImportedDocument, i, issues);
        }
        if (!lods.Any(lod => lod.IsOccupied)) Error(issues, "xmodel.lods", "An XModel must retain at least one active LOD.");
        if (draft.CollisionLod != 0xFF && (draft.CollisionLod >= lods.Count || !lods[draft.CollisionLod].IsOccupied)) Error(issues, "xmodel.collLod", "Collision LOD must select an active LOD or None.");
        if (draft.CollisionLod != 0xFF && lods[draft.CollisionLod].IsImported) Error(issues, "xmodel.collLod", "Imported collision LODs cannot apply until collision trees are compiled.");
        if (draft.HasStagedAssemblyChanges) Error(issues, "xmodel.lods", "LOD assembly is staged locally; runtime XSurface compilation and material remapping are required before Apply.");
        return Array.AsReadOnly(issues.ToArray());
    }

    private static void ValidateImported(XModelAsset model, XModelExportDocument doc, int lodIndex, List<AssetValidationIssue> issues)
    {
        if (doc.Vertices.Count == 0 || doc.Triangles.Count == 0 || doc.Objects.Count == 0) Error(issues, $"xmodel.lods[{lodIndex}]", "Imported LOD must contain vertices, triangles, and objects.");
        if (!XModelExportSkeletonProjector.TryProject(
                model,
                out IReadOnlyList<XModelExportBone> baselineBones,
                out IReadOnlyList<string> skeletonBlockers))
        {
            foreach (string blocker in skeletonBlockers)
                Error(issues, $"xmodel.lods[{lodIndex}].bones", blocker);
        }
        if (doc.Bones.Count != baselineBones.Count)
        {
            Error(
                issues,
                $"xmodel.lods[{lodIndex}].bones",
                "Imported skeleton bone count does not match the XModel.");
        }
        for (int i = 0; i < Math.Min(doc.Bones.Count, baselineBones.Count); i++)
        {
            XModelExportBone imported = doc.Bones[i];
            XModelExportBone baseline = baselineBones[i];
            if (!string.Equals(imported.Name, baseline.Name, StringComparison.Ordinal) ||
                imported.ParentIndex != baseline.ParentIndex)
            {
                Error(
                    issues,
                    $"xmodel.lods[{lodIndex}].bones[{i}]",
                    "Imported skeleton order, name, or parent does not match the XModel.");
            }
        }
        var materialNames = model.Materials.Select(m => m?.Info.Name).ToArray();
        for (int i = 0; i < doc.Materials.Count; i++)
        {
            string name = doc.Materials[i].Name;
            int matches = materialNames.Count(existing => string.Equals(existing, name, StringComparison.Ordinal));
            if (matches != 1 || i >= materialNames.Length || !string.Equals(materialNames[i], name, StringComparison.Ordinal)) Error(issues, $"xmodel.lods[{lodIndex}].materials[{i}]", "Imported material must uniquely match the existing material slot at the same ordinal.");
        }
        foreach ((XModelExportVertex vertex, int index) in doc.Vertices.Select((value, index) => (value, index)))
            if (vertex.Weights.Count > 4) Error(issues, $"xmodel.lods[{lodIndex}].vertices[{index}]", "Imported vertices may use at most four bone influences.");
        foreach (IGrouping<int, XModelExportTriangle> surface in doc.Triangles.GroupBy(t => t.ObjectIndex))
        {
            int[] materials = surface.Select(t => t.MaterialIndex).Distinct().ToArray();
            if (materials.Length != 1) Error(issues, $"xmodel.lods[{lodIndex}].objects[{surface.Key}]", "Each imported object must use exactly one material.");
            if (surface.Count() > ushort.MaxValue) Error(issues, $"xmodel.lods[{lodIndex}].objects[{surface.Key}]", "Imported surface triangle count exceeds the XSurface ushort limit.");
            int expanded = surface.SelectMany(t => new[] { t.First, t.Second, t.Third }).Distinct().Count();
            if (expanded > ushort.MaxValue) Error(issues, $"xmodel.lods[{lodIndex}].objects[{surface.Key}]", "Imported surface expanded vertex count exceeds the XSurface ushort limit.");
        }
        foreach (int objectIndex in Enumerable.Range(0, doc.Objects.Count))
            if (!doc.Triangles.Any(triangle => triangle.ObjectIndex == objectIndex)) Error(issues, $"xmodel.lods[{lodIndex}].objects[{objectIndex}]", "Imported objects must contain at least one triangle.");
        if (doc.Objects.Count > byte.MaxValue) Error(issues, $"xmodel.lods[{lodIndex}].objects", "Imported surface count exceeds the XModel byte limit.");
    }
    private static void Error(List<AssetValidationIssue> issues, string path, string message) => issues.Add(new(path, message, AssetValidationSeverity.Error));
}
