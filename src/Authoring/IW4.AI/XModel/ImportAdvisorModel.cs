using Microsoft.ML;

namespace IW4.AI.XModel;

/// <summary>A directional comparison against fitted stock XModel LOD measurements.</summary>
public sealed record ImportWarning(
    string Feature,
    float Actual,
    float PeerNinetyNinthPercentile,
    int PeerFeatureCount,
    string Reason);

public sealed class ImportAssessment
{
    internal ImportAssessment(int peerCount, string peerDescription, bool hasSufficientEvidence,
        IReadOnlyList<ImportWarning> warnings)
    {
        PeerCount = peerCount;
        PeerDescription = peerDescription;
        HasSufficientEvidence = hasSufficientEvidence;
        Warnings = warnings;
    }

    public int PeerCount { get; }
    public string PeerDescription { get; }
    public bool HasSufficientEvidence { get; }
    public IReadOnlyList<ImportWarning> Warnings { get; }

    public override string ToString()
    {
        if (!HasSufficientEvidence)
            return $"Insufficient stock evidence for {PeerDescription} ({PeerCount} peers with known geometry).";
        if (Warnings.Count == 0)
            return $"No unusually high measured burdens relative to {PeerDescription} ({PeerCount} stock peers).";
        return string.Join(Environment.NewLine, Warnings.Select(warning => warning.Reason));
    }
}

/// <summary>
/// A loaded, offline stock reference. It identifies unusually high feature values;
/// it does not predict frame time, prove PS3 safety, or validate an imported asset.
/// </summary>
public sealed class ImportAdvisorModel
{
    private readonly IReadOnlyDictionary<string, float> _learned;

    internal ImportAdvisorModel(IReadOnlyDictionary<string, float> learned) => _learned = learned;

    public ImportAssessment Assess(LodFeatures candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        string kind = float.IsFinite(candidate.SkinnedVertexCount)
            ? candidate.SkinnedVertexCount > 0 ? "skinned" : "rigid"
            : "unknown skinning";
        string peerDescription = $"LOD {candidate.LodIndex} {kind} peers";
        if (candidate.LodIndex is < 0 or > 3 ||
            !float.IsFinite(candidate.SkinnedVertexCount) || candidate.SkinnedVertexCount < 0)
            return new ImportAssessment(0, peerDescription, false, []);

        string prefix = ImportAdvisor.PeerPrefix(candidate.LodIndex, candidate.SkinnedVertexCount > 0);
        int peerCount = GetCount(prefix + "/count");
        var warnings = new List<ImportWarning>();
        bool comparable = false;
        foreach (ImportAdvisor.FeatureSpec feature in ImportAdvisor.Features)
        {
            float actual = feature.Read(candidate);
            if (!float.IsFinite(actual) || actual < 0)
                continue;

            string metric = prefix + "/" + feature.Name;
            int measuredCount = GetCount(metric + "/count");
            int positiveCount = GetCount(metric + "/positive");
            float cutoff = Get(metric + "/p99");
            // Sample and positive-support requirements are conservative heuristics;
            // the percentile and peer counts are fitted from stock observations.
            if (measuredCount < ImportAdvisor.MinimumFeaturePeers ||
                positiveCount < ImportAdvisor.MinimumPositivePeers ||
                !float.IsFinite(cutoff) || cutoff <= 0)
                continue;
            comparable = true;
            if (actual <= cutoff)
                continue;

            string reason = $"LOD {candidate.LodIndex} has {actual:N0} {feature.Label.ToLowerInvariant()}. " +
                $"The 99th percentile across {measuredCount:N0} comparable stock LODs is {cutoff:N0}.";
            warnings.Add(new ImportWarning(feature.Name, actual, cutoff, measuredCount, reason));
        }
        return new ImportAssessment(peerCount, peerDescription,
            peerCount >= ImportAdvisor.MinimumFeaturePeers && comparable, warnings.ToArray());
    }

    private float Get(string key) => _learned.TryGetValue(key, out float value) ? value : float.NaN;
    private int GetCount(string key)
    {
        float value = Get(key);
        return float.IsFinite(value) && value >= 0 && value <= int.MaxValue ? (int)value : 0;
    }
}

public static partial class ImportAdvisor
{
    internal const int MinimumFeaturePeers = 100;
    internal const int MinimumPositivePeers = 10;

    internal static readonly FeatureSpec[] Features =
    [
        new(nameof(LodFeatures.TriangleCount), "Triangles", x => x.TriangleCount),
        new(nameof(LodFeatures.VertexCount), "Vertices", x => x.VertexCount),
        new(nameof(LodFeatures.SurfaceCount), "Surfaces", x => x.SurfaceCount),
        new(nameof(LodFeatures.BoneCount), "Bones", x => x.BoneCount),
        new(nameof(LodFeatures.SkinnedVertexCount), "Skinned vertices", x => x.SkinnedVertexCount),
        new(nameof(LodFeatures.SkinningInfluenceCount), "Skinning influences", x => x.SkinningInfluenceCount),
        new(nameof(LodFeatures.DistinctMaterialCount), "Distinct materials", x => x.DistinctMaterialCount),
        new(nameof(LodFeatures.MaxTexturesPerMaterial), "Textures on one material", x => x.MaxTexturesPerMaterial),
        new(nameof(LodFeatures.LitSurfacePassCount), "Lit surface passes", x => x.LitSurfacePassCount),
        new(nameof(LodFeatures.AlphaTestedLitSurfaceCount), "Alpha-tested Lit surfaces", x => x.AlphaTestedLitSurfaceCount),
        new(nameof(LodFeatures.BlendedLitSurfaceCount), "Blended Lit surfaces", x => x.BlendedLitSurfaceCount)
    ];

    /// <summary>
    /// Fits empirical 99th percentiles by active LOD index and observed skinning
    /// peer group, then saves the learned lookup as one native ML.NET model.
    /// Finite zero measurements participate; unavailable measurements do not.
    /// </summary>
    public static void Fit(IEnumerable<LodFeatures> stockLods, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(stockLods);
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
            throw new ArgumentException("The model destination must be writable.", nameof(destination));

        var groups = stockLods
            .Where(x => x is not null && x.LodIndex is >= 0 and <= 3 &&
                float.IsFinite(x.SkinnedVertexCount) && x.SkinnedVertexCount >= 0)
            .GroupBy(x => (x.LodIndex, Skinned: x.SkinnedVertexCount > 0))
            .ToArray();
        if (groups.Length == 0)
            throw new InvalidDataException("No stock LODs have a valid index and known rigid/skinned peer group.");

        var entries = new List<LookupMapRow> { new() { Key = "version", Value = 1 } };
        foreach (var group in groups)
        {
            LodFeatures[] peers = group.ToArray();
            string prefix = PeerPrefix(group.Key.LodIndex, group.Key.Skinned);
            entries.Add(new LookupMapRow { Key = prefix + "/count", Value = peers.Length });
            foreach (FeatureSpec feature in Features)
            {
                float[] values = peers.Select(feature.Read)
                    .Where(value => float.IsFinite(value) && value >= 0)
                    .OrderBy(value => value)
                    .ToArray();
                if (values.Length == 0)
                    continue;
                string metric = prefix + "/" + feature.Name;
                entries.Add(new LookupMapRow { Key = metric + "/count", Value = values.Length });
                entries.Add(new LookupMapRow { Key = metric + "/positive", Value = values.Count(value => value > 0) });
                entries.Add(new LookupMapRow
                {
                    Key = metric + "/p99",
                    Value = values[(int)Math.Ceiling(values.Length * 0.99) - 1]
                });
            }
        }

        var ml = new MLContext();
        IDataView lookup = ml.Data.LoadFromEnumerable(entries);
        IDataView input = ml.Data.LoadFromEnumerable(new[] { new LookupRow { Key = "version" } });
        ITransformer transformer = ml.Transforms.Conversion.MapValue(
            nameof(LookupMapRow.Value), lookup,
            lookup.Schema[nameof(LookupMapRow.Key)], lookup.Schema[nameof(LookupMapRow.Value)],
            nameof(LookupRow.Key)).Fit(input);
        ml.Model.Save(transformer, input.Schema, destination);
    }

    /// <summary>Loads a fitted single-file ML.NET stock reference once for repeated assessments.</summary>
    public static ImportAdvisorModel Load(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
            throw new ArgumentException("The model source must be readable.", nameof(source));

        var ml = new MLContext();
        ITransformer transformer = ml.Model.Load(source, out _);
        var keys = new List<LookupRow> { new() { Key = "version" } };
        for (int lod = 0; lod < 4; lod++)
        for (int skinning = 0; skinning < 2; skinning++)
        {
            string prefix = PeerPrefix(lod, skinning == 1);
            keys.Add(new LookupRow { Key = prefix + "/count" });
            foreach (FeatureSpec feature in Features)
            {
                string metric = prefix + "/" + feature.Name;
                keys.Add(new LookupRow { Key = metric + "/count" });
                keys.Add(new LookupRow { Key = metric + "/positive" });
                keys.Add(new LookupRow { Key = metric + "/p99" });
            }
        }
        IDataView scored = transformer.Transform(ml.Data.LoadFromEnumerable(keys));
        Dictionary<string, float> learned = ml.Data.CreateEnumerable<LookupMapRow>(scored, reuseRowObject: false)
            .ToDictionary(row => row.Key, row => row.Value, StringComparer.Ordinal);
        if (!learned.TryGetValue("version", out float version) || version != 1)
            throw new InvalidDataException("The import advisor model has an unsupported version.");
        return new ImportAdvisorModel(learned);
    }

    internal static string PeerPrefix(int lodIndex, bool skinned) =>
        $"lod{lodIndex}/{(skinned ? "skinned" : "rigid")}";

    internal sealed record FeatureSpec(string Name, string Label, Func<LodFeatures, float> Read);

    internal sealed class LookupMapRow
    {
        public LookupMapRow() { }
        public string Key { get; set; } = "";
        public float Value { get; set; }
    }

    internal sealed class LookupRow
    {
        public LookupRow() { }
        public string Key { get; set; } = "";
    }
}
