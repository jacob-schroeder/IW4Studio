using IW4.Assets.D3dbsp;

namespace MapConverter.Game.IW3.PC.Models;

internal static class Iw3StaticModelNameReader
{
    internal static Iw3MapModelReferences ReadFromConvertedD3dbsp(
        string path,
        IReadOnlySet<string>? staticScriptModelNames = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "The converted IW4 d3dbsp does not exist.",
                fullPath);
        }

        D3dbspFile file = D3dbspFile.Read(fullPath);
        IReadOnlyList<string> staticModelNames = file.GetStaticModelNames(staticScriptModelNames);
        if (staticModelNames.Count == 0)
        {
            throw new InvalidDataException(
                $"The converted IW4 d3dbsp '{fullPath}' contains no static-model references.");
        }
        return new Iw3MapModelReferences(
            Array.AsReadOnly(staticModelNames
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray()),
            file.GetNamedEntityModelNames());
    }
}

internal sealed record Iw3MapModelReferences(
    IReadOnlyList<string> StaticModelNames,
    IReadOnlyList<string> NamedEntityModelNames);
