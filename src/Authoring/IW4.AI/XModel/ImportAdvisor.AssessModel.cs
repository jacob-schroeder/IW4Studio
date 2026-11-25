using IW4.Game.Assets.XModel;

namespace IW4.AI.XModel;

public static partial class ImportAdvisor
{
    private static readonly Lazy<ImportAdvisorModel?> ShippedReference = new(LoadShippedReference);

    public static XModelImportReport AssessModel(XModelAsset model, XModelUsage? usage = null,
        ImportAdvisorModel? stockReference = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        stockReference ??= ShippedReference.Value;
        var lods = new List<XModelLodAssessment>();
        var warnings = new List<string>();
        var transitions = new List<string>();
        var risks = new List<(int LodIndex, int? AdjacentLodIndex, string Reason)>();
        int count = model.NumLods == 0 ? model.Lods.Count : model.NumLods;
        if (count > 4 || count > model.Lods.Count || model.MaxLoadedLod >= count)
            warnings.Add("The model has no valid active LOD range to assess.");
        else
        {
            LodFeatures? previous = null;
            for (int index = model.MaxLoadedLod; index < count; index++)
            {
                LodFeatures? features = DescribeLod(model, index);
                ImportAssessment? assessment = features is null ? null : stockReference?.Assess(features);
                lods.Add(new(index, features, assessment));
                if (features is null)
                    warnings.Add($"LOD {index} has no available surface geometry to assess.");
                else
                {
                    AppendStreamWarning(model.Lods[index], index, warnings, risks);
                    if (previous is not null)
                        CompareLods(previous, features, assessment, transitions, warnings, risks);
                }
                previous = features;
            }
        }

        if (stockReference is null)
            warnings.Add("The stock XModel reference could not be loaded. Stock percentile comparisons are unavailable.");
        foreach (XModelLodAssessment lod in lods)
            if (lod.Assessment is { } assessment)
            {
                foreach (ImportWarning warning in assessment.Warnings)
                    warnings.Add(warning.Reason);
                // A single stock percentile outlier remains a diagnostic. Two
                // measured performance burdens on one LOD justify interruption.
                ImportWarning[] burdens = assessment.HasSufficientEvidence
                    ? assessment.Warnings.Where(warning => IsPerformanceBurden(warning.Feature)).ToArray()
                    : [];
                if (burdens.Length >= 2)
                    risks.AddRange(burdens.Select(warning => (lod.LodIndex, (int?)null, warning.Reason)));
            }
        return new XModelImportReport(lods, warnings, transitions, usage?.Description)
        {
            ImportRisks = risks.ToArray()
        };
    }

    private static bool IsPerformanceBurden(string feature) => feature is
        nameof(LodFeatures.TriangleCount) or nameof(LodFeatures.VertexCount) or
        nameof(LodFeatures.SurfaceCount) or nameof(LodFeatures.SkinnedVertexCount) or
        nameof(LodFeatures.SkinningInfluenceCount) or nameof(LodFeatures.LitSurfacePassCount) or
        nameof(LodFeatures.AlphaTestedLitSurfaceCount) or nameof(LodFeatures.BlendedLitSurfaceCount);

    private static ImportAdvisorModel? LoadShippedReference()
    {
        try
        {
            using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Advisors", "xmodel-import-advisor.zip"));
            return Load(stream);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException
            or FormatException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private static void AppendStreamWarning(XModelLodInfo lod, int index, List<string> warnings,
        List<(int LodIndex, int? AdjacentLodIndex, string Reason)> risks)
    {
        if (lod.ModelSurfs is not { } mesh)
            return;
        int local0 = 0, local1 = 0, affected = 0;
        foreach (XSurface surface in mesh.Surfaces.Take(lod.NumSurfs))
        {
            if (!surface.Deformed || surface.VertCount == 0)
                continue;
            bool stream0 = (surface.StreamFlags & XSurfaceStreamFlags.Verts0InLarge) == 0;
            bool stream1 = (surface.StreamFlags & XSurfaceStreamFlags.Verts1InLarge) == 0;
            if (stream0) local0++;
            if (stream1) local1++;
            if (stream0 || stream1) affected++;
        }
        if (affected > 0)
        {
            string reason = $"LOD {index} routes vertex data from {affected:N0} deformed {(affected == 1 ? "surface" : "surfaces")} to RSX-local memory " +
                $"(stream 0 on {local0:N0}, stream 1 on {local1:N0}). Rebuild the mesh with both deformed vertex streams " +
                "in the LARGE block (main memory) to match stock PS3 models.";
            warnings.Add(reason);
            risks.Add((index, null, reason));
        }
    }

    private static void CompareLods(LodFeatures previous, LodFeatures current, ImportAssessment? assessment,
        List<string> transitions, List<string> warnings,
        List<(int LodIndex, int? AdjacentLodIndex, string Reason)> risks)
    {
        var metrics = new (string Key, string Label, float From, float To)[]
        {
            (nameof(LodFeatures.TriangleCount), "triangles", previous.TriangleCount, current.TriangleCount),
            (nameof(LodFeatures.SkinnedVertexCount), "skinned vertices", previous.SkinnedVertexCount, current.SkinnedVertexCount),
            (nameof(LodFeatures.SkinningInfluenceCount), "skinning influences", previous.SkinningInfluenceCount, current.SkinningInfluenceCount)
        };
        string transition = $"LOD {previous.LodIndex} → {current.LodIndex}";
        var details = new List<string>();
        var stalled = new List<string>();
        bool unusuallyHighStalled = false;
        bool anyReduction = metrics.Any(m => m.To < m.From);
        foreach (var (key, label, from, to) in metrics)
        {
            if (!float.IsFinite(from) || !float.IsFinite(to))
            {
                details.Add($"{label} unavailable");
                continue;
            }
            string retention = from > 0 ? $" ({to / from:P1} retained)" : "";
            details.Add($"{label} {from:N0} → {to:N0}{retention}");
            // A few unchanged weights need no warning when the other burdens fall.
            // Still call out an unusually high burden that survives the switch.
            if (to > from || (to > 0 && to == from &&
                (!anyReduction || assessment?.Warnings.Any(w => w.Feature == key) == true)))
            {
                stalled.Add($"{label} ({from:N0} → {to:N0})");
                if (to > 0 && to >= from && assessment is { HasSufficientEvidence: true } &&
                    assessment.Warnings.Any(warning => warning.Feature == key))
                    unusuallyHighStalled = true;
            }
        }
        transitions.Add($"{transition}. {string.Join(", ", details)}.");
        if (stalled.Count > 0)
        {
            string reason = $"{transition} carries forward or increases {string.Join(", ", stalled)}. " +
                $"Review the lower-detail mesh for LOD {current.LodIndex}.";
            warnings.Add(reason);
            if (unusuallyHighStalled)
                risks.Add((current.LodIndex, previous.LodIndex, reason));
        }
    }
}
