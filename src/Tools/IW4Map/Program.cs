using IW4Map;

return Run(args);

static int Run(string[] args)
{
    try
    {
        return args switch
        {
            ["inspect", string input] => Inspect(input),
            ["inspect-fastfile", string input] => InspectFastFile(input),
            ["inspect-pair", string d3dbsp, string fastFile] => InspectPair(d3dbsp, fastFile),
            ["rewrite", string input, string output] => Rewrite(input, output),
            _ => Usage()
        };
    }
    catch (Exception exception) when (exception is
        ArgumentException or
        InvalidDataException or
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

static int InspectFastFile(string input)
{
    FastFileInspector.Inspect(input);
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
    Console.Error.WriteLine("  IW4Map inspect <input.d3dbsp>");
    Console.Error.WriteLine("  IW4Map inspect-fastfile <input.ff>");
    Console.Error.WriteLine("  IW4Map inspect-pair <input.d3dbsp> <input.ff>");
    Console.Error.WriteLine("  IW4Map rewrite <input.d3dbsp> <output.d3dbsp>");
    return 2;
}
