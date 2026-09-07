using System.Globalization;
using IW4.Assets.D3dbsp;
using D3dbspLinker.Conversion;
using D3dbspLinker.Inspection;

return Run(args);

static int Run(string[] args)
{
    try
    {
        return args switch
        {
            ["inspect", string input] => Inspect(input),
            ["inspect-fastfile", string input] => InspectFastFile(input),
            ["find-fastfile-assets", string input, string contains] =>
                FindFastFileAssets(input, contains),
            ["inspect-pair", string d3dbsp, string fastFile] => InspectPair(d3dbsp, fastFile),
            ["to-d3dbsp", string fastFile, string output] => ToD3dbsp(fastFile, output),
            ["to-fastfile", string d3dbsp, string template, string assetName, string output,
                .. string[] optionsAndDependencies] =>
                ToFastFile(d3dbsp, template, assetName, output, optionsAndDependencies),
            ["rewrite", string input, string output] => Rewrite(input, output),
            _ => Usage()
        };
    }
    catch (Exception exception) when (exception is
        ArgumentException or
        InvalidDataException or
        NotSupportedException or
        IOException or
        UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"error: {exception.Message}");
        return 1;
    }
}

static int InspectPair(string d3dbsp, string fastFile)
{
    MapPairInspector.Inspect(d3dbsp, fastFile);
    return 0;
}

static int ToFastFile(
    string d3dbsp,
    string template,
    string assetName,
    string output,
    IReadOnlyList<string> optionsAndDependencies)
{
    bool forceFullbright = false;
    bool worldOnly = false;
    bool useSourceMaterials = false;
    var lightmapImageNames = new List<(string PrimaryImageName, string SecondaryImageName)>();
    string? outdoorImageName = null;
    float[]? outdoorLookupMatrix = null;
    var dependencies = new List<string>();
    var providerFastFiles = new List<string>();
    var distinctProviderFastFiles = new HashSet<string>(StringComparer.Ordinal);
    var additionalXModelNames = new List<string>();
    var distinctXModelNames = new HashSet<string>(StringComparer.Ordinal);
    var staticScriptModelNames = new HashSet<string>(StringComparer.Ordinal);
    var additionalMaterialNames = new List<string>();
    var distinctMaterialNames = new HashSet<string>(StringComparer.Ordinal);
    var additionalFxNames = new List<string>();
    var distinctFxNames = new HashSet<string>(StringComparer.Ordinal);
    var additionalSoundNames = new List<string>();
    var distinctSoundNames = new HashSet<string>(StringComparer.Ordinal);
    var rawFilePaths = new Dictionary<string, string>(StringComparer.Ordinal);
    for (int index = 0; index < optionsAndDependencies.Count; index++)
    {
        string value = optionsAndDependencies[index];
        if (string.Equals(value, "--fullbright", StringComparison.Ordinal))
        {
            if (forceFullbright)
                throw new ArgumentException("The --fullbright option may be supplied only once.");
            forceFullbright = true;
            continue;
        }
        if (string.Equals(value, "--world-only", StringComparison.Ordinal))
        {
            if (worldOnly)
                throw new ArgumentException("The --world-only option may be supplied only once.");
            worldOnly = true;
            continue;
        }
        if (string.Equals(value, "--source-materials", StringComparison.Ordinal))
        {
            if (useSourceMaterials)
                throw new ArgumentException("The --source-materials option may be supplied only once.");
            useSourceMaterials = true;
            continue;
        }
        if (string.Equals(value, "--lightmap", StringComparison.Ordinal))
        {
            string primary = ReadRequiredOptionValue(
                optionsAndDependencies, ref index, "--lightmap", "a primary image name and a secondary image name");
            string secondary = ReadRequiredOptionValue(
                optionsAndDependencies, ref index, "--lightmap", "a secondary image name");
            lightmapImageNames.Add((primary, secondary));
            continue;
        }
        if (string.Equals(value, "--outdoor-image", StringComparison.Ordinal))
        {
            if (outdoorImageName is not null)
                throw new ArgumentException("The --outdoor-image option may be supplied only once.");
            outdoorImageName = ReadRequiredOptionValue(
                optionsAndDependencies, ref index, "--outdoor-image", "an exact image name");
            continue;
        }
        if (string.Equals(value, "--outdoor-lookup-matrix", StringComparison.Ordinal))
        {
            if (outdoorLookupMatrix is not null)
                throw new ArgumentException("The --outdoor-lookup-matrix option may be supplied only once.");
            string matrix = ReadRequiredOptionValue(
                optionsAndDependencies, ref index, "--outdoor-lookup-matrix", "16 comma-separated finite floats");
            outdoorLookupMatrix = matrix.Split(',').Select(component =>
            {
                if (!float.TryParse(component, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) ||
                    !float.IsFinite(parsed))
                {
                    throw new ArgumentException("The --outdoor-lookup-matrix option requires finite floats.");
                }
                return parsed;
            }).ToArray();
            continue;
        }
        if (string.Equals(value, "--xmodel", StringComparison.Ordinal))
        {
            string name = ReadRequiredOptionValue(
                optionsAndDependencies,
                ref index,
                "--xmodel",
                "an exact XModel name");
            if (!distinctXModelNames.Add(name))
            {
                throw new ArgumentException(
                    $"The --xmodel option names XModel '{name}' more than once.");
            }
            additionalXModelNames.Add(name);
            continue;
        }
        if (string.Equals(value, "--static-script-model", StringComparison.Ordinal))
        {
            string name = ReadRequiredOptionValue(
                optionsAndDependencies, ref index, "--static-script-model", "an exact script_model XModel name");
            if (!staticScriptModelNames.Add(name))
                throw new ArgumentException($"The --static-script-model option names '{name}' more than once.");
            continue;
        }
        if (string.Equals(value, "--material", StringComparison.Ordinal))
        {
            string name = ReadRequiredOptionValue(
                optionsAndDependencies,
                ref index,
                "--material",
                "an exact Material name");
            if (!distinctMaterialNames.Add(name))
            {
                throw new ArgumentException(
                    $"The --material option names Material '{name}' more than once.");
            }
            additionalMaterialNames.Add(name);
            continue;
        }
        if (string.Equals(value, "--provider-fastfile", StringComparison.Ordinal))
        {
            string path = ReadRequiredOptionValue(
                optionsAndDependencies,
                ref index,
                "--provider-fastfile",
                "a provider-only fastfile path");
            if (!distinctProviderFastFiles.Add(path))
            {
                throw new ArgumentException(
                    $"The --provider-fastfile option names '{path}' more than once.");
            }
            providerFastFiles.Add(path);
            continue;
        }
        if (string.Equals(value, "--fx", StringComparison.Ordinal))
        {
            string name = ReadRequiredOptionValue(
                optionsAndDependencies,
                ref index,
                "--fx",
                "an exact FxEffectDef name");
            if (!distinctFxNames.Add(name))
            {
                throw new ArgumentException(
                    $"The --fx option names FxEffectDef '{name}' more than once.");
            }
            additionalFxNames.Add(name);
            continue;
        }
        if (string.Equals(value, "--sound", StringComparison.Ordinal))
        {
            string name = ReadRequiredOptionValue(
                optionsAndDependencies,
                ref index,
                "--sound",
                "an exact Sound alias name");
            if (!distinctSoundNames.Add(name))
            {
                throw new ArgumentException(
                    $"The --sound option names Sound '{name}' more than once.");
            }
            additionalSoundNames.Add(name);
            continue;
        }
        if (string.Equals(value, "--rawfile", StringComparison.Ordinal))
        {
            string mapping = ReadRequiredOptionValue(
                optionsAndDependencies,
                ref index,
                "--rawfile",
                "a wire-name=source-path mapping");
            int separator = mapping.IndexOf('=');
            if (separator <= 0 || separator == mapping.Length - 1)
            {
                throw new ArgumentException(
                    "The --rawfile option requires a wire-name=source-path mapping.");
            }
            string name = mapping[..separator];
            string path = mapping[(separator + 1)..];
            if (!rawFilePaths.TryAdd(name, path))
            {
                throw new ArgumentException(
                    $"The --rawfile option maps RawFile '{name}' more than once.");
            }
            continue;
        }
        dependencies.Add(value);
    }

    if (useSourceMaterials && !worldOnly)
        throw new ArgumentException("The --source-materials option requires --world-only.");
    if (staticScriptModelNames.Count != 0 && (!worldOnly || !useSourceMaterials))
        throw new ArgumentException("The --static-script-model option requires --world-only and --source-materials.");

    FastFileConverter.FromD3dbsp(
        d3dbsp,
        template,
        assetName,
        output,
        forceFullbright,
        worldOnly,
        useSourceMaterials,
        dependencies,
        providerFastFiles,
        additionalXModelNames,
        additionalMaterialNames,
        additionalFxNames,
        additionalSoundNames,
        rawFilePaths,
        lightmapImageNames,
        outdoorImageName,
        outdoorLookupMatrix ?? [],
        staticScriptModelNames);
    return 0;
}

static string ReadRequiredOptionValue(
    IReadOnlyList<string> arguments,
    ref int index,
    string option,
    string valueDescription)
{
    if (++index >= arguments.Count ||
        arguments[index].StartsWith("--", StringComparison.Ordinal))
    {
        throw new ArgumentException(
            $"The {option} option requires {valueDescription}.");
    }

    return arguments[index];
}

static int ToD3dbsp(string fastFile, string output)
{
    FastFileConverter.ToD3dbsp(fastFile, output);
    return 0;
}

static int InspectFastFile(string input)
{
    FastFileInspector.Inspect(input);
    return 0;
}

static int FindFastFileAssets(string input, string contains)
{
    FastFileInspector.FindAssets(input, contains);
    return 0;
}

static int Inspect(string input)
{
    string path = Path.GetFullPath(input);
    D3dbspFile file = D3dbspFile.Read(path);

    Console.WriteLine($"file: {path}");
    Console.WriteLine("ident: IBSP");
    Console.WriteLine("version: 22");
    Console.WriteLine($"chunks: {file.Lumps.Count}");
    Console.WriteLine();
    Console.WriteLine("index  id    type                         offset       length       unit       count");

    for (int index = 0; index < file.Lumps.Count; index++)
    {
        D3dbspLump lump = file.Lumps[index];
        int? elementSize = D3dbspLumpFacts.GetV22ElementSize(lump.Type);
        string count = elementSize is { } size && lump.Data.Length % size == 0
            ? (lump.Data.Length / size).ToString()
            : "-";
        string type = Enum.IsDefined(lump.Type) ? lump.Type.ToString() : "Unknown";

        Console.WriteLine(
            $"{index,5}  0x{(uint)lump.Type:X2}  {type,-28} " +
            $"0x{file.GetPayloadOffset(index),8:X8}  {lump.Length,11}  " +
            $"{elementSize?.ToString() ?? "-",9}  {count,10}");
    }

    return 0;
}

static int Rewrite(string input, string output)
{
    string inputPath = Path.GetFullPath(input);
    string outputPath = Path.GetFullPath(output);
    if (string.Equals(inputPath, outputPath, StringComparison.Ordinal))
        throw new ArgumentException("Input and output paths must be different.");

    D3dbspFile.Read(inputPath).Write(outputPath);
    Console.WriteLine($"wrote: {outputPath}");
    return 0;
}

static int Usage()
{
    Console.Error.WriteLine("usage:");
    Console.Error.WriteLine("  D3dbspLinker inspect <input.d3dbsp>");
    Console.Error.WriteLine("  D3dbspLinker inspect-fastfile <input.ff>");
    Console.Error.WriteLine("  D3dbspLinker find-fastfile-assets <input.ff> <name-contains>");
    Console.Error.WriteLine("  D3dbspLinker inspect-pair <input.d3dbsp> <input.ff>");
    Console.Error.WriteLine("  D3dbspLinker to-d3dbsp <input.ff> <output.d3dbsp>");
    Console.Error.WriteLine(
        "  D3dbspLinker to-fastfile <input.d3dbsp> <template.ff> <map-asset-name> <output.ff> [--fullbright] [--world-only [--source-materials]] [--provider-fastfile <provider-only.ff>]... [--lightmap <primary-image> <secondary-image>]... [--outdoor-image <image> --outdoor-lookup-matrix <16-comma-separated-floats>] [--xmodel <exact-name>]... [--static-script-model <exact-name>]... [--material <exact-name>]... [--fx <exact-name>]... [--sound <exact-name>]... [--rawfile <wire-name=source-path>]... [dependency.ff ...]");
    Console.Error.WriteLine("  Lighting images must be owned by --provider-fastfile inputs; --lightmap order defines atlas indices. Supplied lighting cannot use --fullbright.");
    Console.Error.WriteLine("  D3dbspLinker rewrite <input.d3dbsp> <output.d3dbsp>");
    return 2;
}
