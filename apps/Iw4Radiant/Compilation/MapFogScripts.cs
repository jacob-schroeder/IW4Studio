using System.Globalization;
using System.Text;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Compilation;

internal sealed record MapFogScripts(string Name, string Source)
{
    internal static MapFogScripts? Create(MapDocument document, string mapName)
    {
        if (!MapFogProperties.TryRead(document.World, out MapFogProperties? fog, out string? error))
            throw new InvalidDataException(error);
        if (fog is not { } settings) return null;
        if (mapName.Length == 0 || mapName.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character != '_'))
            throw new InvalidDataException("A map with fog needs a filename containing only letters, numbers, and underscores.");
        string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        string source = "main()\r\n{\r\n\tsetExpFog( " +
            string.Join(", ", Number(settings.StartDistance), Number(settings.HalfDistance),
                Number(settings.Color.X), Number(settings.Color.Y), Number(settings.Color.Z),
                Number(settings.MaxOpacity), "0") + " );\r\n}\r\n";
        return new MapFogScripts($"maps/mp/{mapName}_fog.gsc", source);
    }

    internal (string Name, string Path) WriteTo(string directory)
    {
        string path = Path.Combine(directory, Name.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path) ??
            throw new InvalidDataException("The fog script output has no directory."));
        File.WriteAllText(path, Source, new UTF8Encoding(false));
        return (Name, path);
    }
}
