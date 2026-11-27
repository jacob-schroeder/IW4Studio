using System.Text.Json;
using IW4.AI.XModel;
using IW4.Game.Assets.XModel;
using IW4.Game.Zone;
using IW4.Loaders.Database;
using IW4.Loaders.Database.Planning;
using IW4.Runtime.Assets;
using Microsoft.ML;
using Microsoft.ML.Data;

const string usage = """
    IW4.AI.Training STOCK_FASTFILE_DIRECTORY OUTPUT.tsv
    IW4.AI.Training train STOCK_LODS.tsv MODEL.zip
    IW4.AI.Training assess MODEL.zip STOCK_FASTFILE_DIRECTORY TARGET.ff XMODEL_NAME
    """;
if (args is ["--help"] or ["-h"])
{
    Console.WriteLine(usage);
    return 0;
}

try
{
    switch (args)
    {
        case ["train", var dataset, var modelFile]:
            Train(dataset, modelFile);
            break;
        case ["assess", var profile, var directory, var fastfile, var modelName]:
            Assess(profile, directory, fastfile, modelName);
            break;
        case [var root, var output]:
            PrepareStockLods(root, output);
            break;
        default:
            Console.Error.WriteLine(usage);
            return 2;
    }
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}

static void Train(string dataset, string destination)
{
    if (!string.Equals(Path.GetExtension(destination), ".zip", StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("The saved ML.NET model requires a .zip destination.");
    string output = Path.GetFullPath(destination);
    if (string.Equals(Path.GetFullPath(dataset), output, StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("The dataset and model must have different paths.");

    string[] header = File.ReadLines(dataset).First().Split('\t');
    var columns = new List<TextLoader.Column>();
    foreach (var property in typeof(LodFeatures).GetProperties())
    {
        int index = Array.IndexOf(header, property.Name);
        if (index < 0)
            throw new InvalidDataException($"Dataset is missing the {property.Name} column.");
        DataKind kind = property.PropertyType == typeof(float) ? DataKind.Single :
            property.PropertyType == typeof(int) ? DataKind.Int32 : DataKind.Byte;
        columns.Add(new TextLoader.Column(property.Name, kind, index));
    }
    var ml = new MLContext();
    var loader = ml.Data.CreateTextLoader(new TextLoader.Options
    {
        Columns = columns.ToArray(), Separators = ['\t'], HasHeader = true,
        AllowQuoting = true, MissingRealsAsNaNs = true
    });
    var rows = ml.Data.CreateEnumerable<LodFeatures>(loader.Load(dataset), reuseRowObject: false).ToArray();
    WriteFile(output, stream => ImportAdvisor.Fit(rows, stream));
    Console.WriteLine($"Fitted {rows.Length:N0} stock LOD observations. Saved {output}");
}

static void Assess(string modelPath, string stockRoot, string fastfile, string modelName)
{
    using FileStream stream = File.OpenRead(modelPath);
    var advisor = ImportAdvisor.Load(stream);
    string directory = Path.GetFullPath(stockRoot);
    var catalog = new DbZoneCatalog(directory, DbZoneCatalog.ResolveAdditionalDependencyDirectories(directory));
    using var session = new DbLoadSession();
    LoadedXZone target = LoadTarget(Path.GetFullPath(fastfile), catalog, session);
    string lookup = XAssetStableIdentity.NormalizeLookupName(modelName);
    var models = TargetModels(session, target).Where(model => model.Name is { } name &&
        XAssetStableIdentity.NormalizeLookupName(name) == lookup).ToArray();
    if (models.Length == 0)
        throw new InvalidDataException($"Fastfile '{fastfile}' has no owned XModel named '{modelName}'.");

    var reports = models.Select(model => ImportAdvisor.AssessModel(model, stockReference: advisor)).ToArray();
    Console.WriteLine(JsonSerializer.Serialize(new
        {
            ModelName = lookup,
            FastFile = fastfile,
            Lods = reports.SelectMany(report => report.Lods),
            Advisories = reports.Select(report => new { report.Warnings, report.LodTransitions })
        },
        new JsonSerializerOptions { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
}

static void PrepareStockLods(string directory, string destination)
{
    string root = Path.GetFullPath(directory);
    if (!Directory.Exists(root))
        throw new DirectoryNotFoundException($"Stock fastfile directory does not exist: {root}");

    string output = Path.GetFullPath(destination);
    if (string.Equals(Path.GetExtension(output), ".ff", StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("Output must not be a .ff file.", nameof(destination));
    string? outputDirectory = Path.GetDirectoryName(output);
    if (outputDirectory is null || !Directory.Exists(outputDirectory))
        throw new DirectoryNotFoundException($"Output directory does not exist: {outputDirectory}");
    if (Directory.Exists(output))
        throw new ArgumentException("The output path must name a file.", nameof(destination));

    string[] fastfiles = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Where(path => string.Equals(Path.GetExtension(path), ".ff", StringComparison.OrdinalIgnoreCase))
        .OrderBy(path => Path.GetRelativePath(root, path), StringComparer.Ordinal)
        .ToArray();
    if (fastfiles.Length == 0)
        throw new InvalidDataException($"No .ff files were found in {root}.");

    var catalog = new DbZoneCatalog(root, DbZoneCatalog.ResolveAdditionalDependencyDirectories(root));
    var observations = new Dictionary<(string ModelName, LodFeatures Features), SortedSet<string>>();
    int skippedModels = 0;
    int skippedLods = 0;
    foreach (string fastfile in fastfiles)
    {
        string relativePath = Path.GetRelativePath(root, fastfile).Replace('\\', '/');
        Console.WriteLine($"Reading {relativePath}");
        using var session = new DbLoadSession();
        LoadedXZone target = LoadTarget(fastfile, catalog, session);

        foreach (XModelAsset model in TargetModels(session, target))
        {
            string? name = model.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                skippedModels++;
                Console.Error.WriteLine($"Skipping unnamed XModel in {relativePath}.");
                continue;
            }

            string normalizedName = XAssetStableIdentity.NormalizeLookupName(name);
            int lodCount = model.NumLods == 0 ? model.Lods.Count : model.NumLods;
            if (lodCount is <= 0 or > 4 || lodCount > model.Lods.Count ||
                model.MaxLoadedLod >= lodCount)
            {
                skippedModels++;
                Console.Error.WriteLine($"Skipping XModel {name} in {relativePath}: invalid active LOD range.");
                continue;
            }

            for (int lodIndex = model.MaxLoadedLod; lodIndex < lodCount; lodIndex++)
            {
                LodFeatures? features = ImportAdvisor.DescribeLod(model, lodIndex);
                if (features is null)
                {
                    skippedLods++;
                    Console.Error.WriteLine($"Skipping XModel {name} LOD {lodIndex} in {relativePath}: geometry unavailable.");
                    continue;
                }

                var key = (normalizedName, features);
                if (!observations.TryGetValue(key, out SortedSet<string>? sources))
                {
                    sources = new SortedSet<string>(StringComparer.Ordinal);
                    observations.Add(key, sources);
                }
                sources.Add(relativePath);
            }
        }
    }

    if (observations.Count == 0)
        throw new InvalidDataException("No usable XModel LOD observations were found; no TSV was written.");

    StockLod[] rows = observations
        .Select(pair => new StockLod(pair.Key.Features, pair.Key.ModelName, JsonSerializer.Serialize(pair.Value)))
        .OrderBy(row => row.ModelName, StringComparer.Ordinal)
        .ThenBy(row => row.LodIndex)
        .ThenBy(row => row.SourceFastfiles, StringComparer.Ordinal)
        .ToArray();

    WriteFile(output, stream =>
    {
        var ml = new MLContext();
        ml.Data.SaveAsText(ml.Data.LoadFromEnumerable(rows), stream, headerRow: true, schema: false, forceDense: true);
    });
    Console.WriteLine($"Wrote {rows.Length} LOD rows to {output}; skipped {skippedModels} models and {skippedLods} unavailable LODs.");
}

static void WriteFile(string path, Action<Stream> write)
{
    string temporary = $"{path}.{Guid.NewGuid():N}.tmp";
    try
    {
        using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            write(stream);
        File.Move(temporary, path, overwrite: true);
    }
    finally
    {
        if (File.Exists(temporary))
            File.Delete(temporary);
    }
}

static IEnumerable<XModelAsset> TargetModels(DbLoadSession session, LoadedXZone target) =>
    session.AssetPool.Slots.SelectMany(slot => slot.Providers)
        .Where(provider => provider.Owner == target.Context.ZoneOwner &&
            provider.AssetType == XAssetType.XModel && !provider.IsReferencePlaceholder)
        .OrderBy(provider => provider.RegistrationSequence)
        .Select(provider => provider.Asset)
        .OfType<XModelAsset>();

static LoadedXZone LoadTarget(string path, DbZoneCatalog catalog, DbLoadSession session)
{
    DbZoneLoadPlan plan;
    if (DefaultMpZoneLoadPlanner.SupportsTarget(path))
        plan = new DefaultMpZoneLoadPlanner(catalog).Build(path, DbZonePlanScope.ThroughTarget);
    else if (DefaultSpZoneLoadPlanner.SupportsTarget(path))
        plan = new DefaultSpZoneLoadPlanner(catalog).Build(path, DbZonePlanScope.ThroughTarget);
    else
        return session.DB_LoadXZone(path, XZoneFlags.DB_ZONE_DEV);

    return DbLoadPlanExecutor.Execute(plan, session).Target;
}

internal sealed record StockLod : LodFeatures
{
    public StockLod(LodFeatures features, string modelName, string sourceFastfiles) : base(features)
    {
        ModelName = modelName;
        SourceFastfiles = sourceFastfiles;
    }

    public string ModelName { get; init; }
    public string SourceFastfiles { get; init; }
}
