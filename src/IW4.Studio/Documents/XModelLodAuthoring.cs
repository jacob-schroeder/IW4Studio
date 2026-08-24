using IW4.Assets.Assets.XModel;
using IW4.Assets.Assets.Material;
using IW4.AssetExchange.XModel;
using System.Numerics;

namespace IW4.Studio.Documents;

public sealed class XModelLodDraft
{
    internal XModelLodDraft(int slotIndex, float distance, XModelLodInfo? baselineLod, XModelExportDocument? importedDocument, string? importSource, IReadOnlyList<XModelMaterialMapping?>? materialMappings = null)
    {
        SlotIndex = slotIndex;
        Distance = distance;
        BaselineLod = baselineLod;
        ImportedDocument = importedDocument;
        ImportSource = importSource;
        MaterialMappings = Array.AsReadOnly((materialMappings ?? importedDocument?.Materials.Select(_ => (XModelMaterialMapping?)null).ToArray() ?? []).ToArray());
    }

    public int SlotIndex { get; }
    public float Distance { get; }
    public XModelLodInfo? BaselineLod { get; }
    public XModelExportDocument? ImportedDocument { get; }
    public string? ImportSource { get; }
    /// <summary>One explicit material and proven XModel inv-high value per imported material row.</summary>
    public IReadOnlyList<XModelMaterialMapping?> MaterialMappings { get; }
    public bool IsOccupied => BaselineLod is not null || ImportedDocument is not null;
    public bool IsImported => ImportedDocument is not null;
    internal XModelLodDraft Clone() => new(SlotIndex, Distance, BaselineLod, ImportedDocument is null ? null : Freeze(ImportedDocument), ImportSource, MaterialMappings);

    internal static XModelExportDocument Freeze(XModelExportDocument document) => new(
        Array.AsReadOnly(document.Bones.Select(b => new XModelExportBone(b.Name, b.ParentIndex, b.GlobalOffset, b.GlobalRotation)).ToArray()),
        Array.AsReadOnly(document.Vertices.Select(v => new XModelExportVertex(v.Position, Array.AsReadOnly(v.Weights.Select(w => new XModelExportBoneWeight(w.BoneIndex, w.Weight)).ToArray()))).ToArray()),
        Array.AsReadOnly(document.Triangles.Select(t => new XModelExportTriangle(t.ObjectIndex, t.MaterialIndex, Copy(t.First), Copy(t.Second), Copy(t.Third))).ToArray()),
        Array.AsReadOnly(document.Objects.Select(o => new XModelExportObject(o.SurfaceIdentity)).ToArray()),
        Array.AsReadOnly(document.Materials.Select(m => new XModelExportMaterial(m.Name, m.ColorMapPath)
        {
            ImportMaterial = m.ImportMaterial is null
                ? null
                : new XModelImportMaterial(
                    m.ImportMaterial.BaseColorFactor,
                    m.ImportMaterial.BaseColorImage is null
                        ? null
                        : new XModelImportImage(
                            m.ImportMaterial.BaseColorImage.Width,
                            m.ImportMaterial.BaseColorImage.Height,
                            Array.AsReadOnly(m.ImportMaterial.BaseColorImage.RgbaBytes.ToArray())),
                    m.ImportMaterial.NormalImage is null
                        ? null
                        : new XModelImportImage(
                            m.ImportMaterial.NormalImage.Width,
                            m.ImportMaterial.NormalImage.Height,
                            Array.AsReadOnly(m.ImportMaterial.NormalImage.RgbaBytes.ToArray())),
                    m.ImportMaterial.NormalScale,
                    m.ImportMaterial.DoubleSided,
                    m.ImportMaterial.AlphaMode,
                    m.ImportMaterial.AlphaCutoff,
                    Array.AsReadOnly(m.ImportMaterial.Warnings.ToArray()))
        }).ToArray()));

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
            if (lod.ImportedDocument is not null) ValidateImported(draft.Model, lod, i, issues);
        }
        if (!lods.Any(lod => lod.IsOccupied)) Error(issues, "xmodel.lods", "An XModel must retain at least one active LOD.");
        if (draft.CollisionLod != 0xFF && (draft.CollisionLod >= lods.Count || !lods[draft.CollisionLod].IsOccupied)) Error(issues, "xmodel.collLod", "Collision LOD must select an active LOD or None.");
        foreach ((XModelCollSurf surface, int index) in draft.CollisionSurfaces.Select((value, index) => (value, index)))
            if (!float.IsFinite(surface.Bounds.MidPoint.X) || !float.IsFinite(surface.Bounds.MidPoint.Y) || !float.IsFinite(surface.Bounds.MidPoint.Z) || !float.IsFinite(surface.Bounds.HalfSize.X) || !float.IsFinite(surface.Bounds.HalfSize.Y) || !float.IsFinite(surface.Bounds.HalfSize.Z) || surface.Bounds.HalfSize.X < 0 || surface.Bounds.HalfSize.Y < 0 || surface.Bounds.HalfSize.Z < 0 || surface.BoneIndex >= draft.Model.NumBones)
                Error(issues, $"xmodel.collSurfs[{index}]", "Collision surface bounds must be finite/nonnegative and reference an existing bone.");
        return Array.AsReadOnly(issues.ToArray());
    }

    private static void ValidateImported(XModelAsset model, XModelLodDraft lod, int lodIndex, List<AssetValidationIssue> issues)
    {
        XModelExportDocument doc = lod.ImportedDocument!;
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
            else if (!SameBindPose(imported, baseline))
            {
                Error(issues, $"xmodel.lods[{lodIndex}].bones[{i}]", "Imported bind offset or rotation does not match the XModel.");
            }
        }
        for (int i = 0; i < doc.Materials.Count; i++)
        {
            if (i >= lod.MaterialMappings.Count || lod.MaterialMappings[i]?.Material is null)
                Error(issues, $"xmodel.lods[{lodIndex}].materials[{i}]", "No compatible IW4 XModel render template with a proven inv-high value is available.");
        }
        foreach ((XModelExportVertex vertex, int index) in doc.Vertices.Select((value, index) => (value, index)))
            if (vertex.Weights.Count > 4) Error(issues, $"xmodel.lods[{lodIndex}].vertices[{index}]", "Imported vertices may use at most four bone influences.");
        foreach (IGrouping<int, XModelExportTriangle> surface in doc.Triangles.GroupBy(t => t.ObjectIndex))
        {
            foreach (IGrouping<int, XModelExportTriangle> partition in surface.GroupBy(triangle => triangle.MaterialIndex))
            {
                if (partition.Count() > ushort.MaxValue) Error(issues, $"xmodel.lods[{lodIndex}].objects[{surface.Key}].materials[{partition.Key}]", "Imported surface triangle count exceeds the XSurface ushort limit.");
            }
        }
        foreach (int objectIndex in Enumerable.Range(0, doc.Objects.Count))
            if (!doc.Triangles.Any(triangle => triangle.ObjectIndex == objectIndex)) Error(issues, $"xmodel.lods[{lodIndex}].objects[{objectIndex}]", "Imported objects must contain at least one triangle.");
        if (doc.Triangles.GroupBy(triangle => (triangle.ObjectIndex, triangle.MaterialIndex)).Count() > byte.MaxValue) Error(issues, $"xmodel.lods[{lodIndex}].objects", "Imported object/material surface count exceeds the XModel byte limit.");
    }
    private static bool SameBindPose(XModelExportBone left, XModelExportBone right)
    {
        const float tolerance = 0.0005f;
        if (Vector3.Distance(left.GlobalOffset, right.GlobalOffset) > tolerance ||
            !float.IsFinite(left.GlobalRotation.LengthSquared()) ||
            !float.IsFinite(right.GlobalRotation.LengthSquared()) ||
            left.GlobalRotation.LengthSquared() <= 0f || right.GlobalRotation.LengthSquared() <= 0f)
            return false;
        float dot = MathF.Abs(Quaternion.Dot(Quaternion.Normalize(left.GlobalRotation), Quaternion.Normalize(right.GlobalRotation)));
        return 1f - dot <= tolerance;
    }
    private static void Error(List<AssetValidationIssue> issues, string path, string message) => issues.Add(new(path, message, AssetValidationSeverity.Error));
}

public sealed record XModelMaterialMapping(
    MaterialAsset Material,
    ushort InvHighMipRadius,
    bool CreateOwnedMaterial = false);
