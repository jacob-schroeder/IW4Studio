using IW4.Formats.D3dbsp;
using IW4.Linker.D3dbsp;

namespace MapConverter.Game.IW3.PC.Models;

internal static class Iw3StaticModelNameReader
{
    internal static Iw3MapModelReferences ReadFromConvertedD3dbsp(
        string path,
        IReadOnlySet<string>? staticScriptModelNames = null,
        Func<IReadOnlyDictionary<string, string>, string, string, string>? rewriteEntityProperty = null)
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
        byte[]? rewrittenEntities = rewriteEntityProperty is null ? null :
            D3dbspAssetLinker.RewriteEntityPropertyValues(file.GetRequiredData(D3dbspLumpType.Entities), rewriteEntityProperty);
        return new Iw3MapModelReferences(
            Array.AsReadOnly(staticModelNames
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray()),
            rewrittenEntities is null ? file.GetNamedEntityModelNames() :
                D3dbspAssetLinker.ReadNamedEntityModelNames(rewrittenEntities),
            rewrittenEntities);
    }
}

internal sealed record Iw3MapModelReferences(
    IReadOnlyList<string> StaticModelNames,
    IReadOnlyList<string> NamedEntityModelNames,
    byte[]? RewrittenEntities);
