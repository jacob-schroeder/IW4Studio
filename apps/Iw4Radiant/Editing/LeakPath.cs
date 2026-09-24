using System.Globalization;
using System.Numerics;

namespace Iw4Radiant.Editing;

// Imported Radiant pointfile data. It is never part of the map document.
internal sealed class LeakPath
{
    internal const int MaximumPoints = 0x2000;
    internal IReadOnlyList<Vector3> Points { get; }
    internal string FileName { get; }

    private LeakPath(string fileName, Vector3[] points)
    {
        FileName = fileName;
        Points = points;
    }

    internal static LeakPath Read(string path)
    {
        if (new FileInfo(path).Length > 2_200_000)
            throw new FormatException("Pointfile is too large for 8,192 points.");
        var points = new List<Vector3>();
        using var reader = new StreamReader(path);
        string? line;
        int lineNumber = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (line.Length > 256)
                throw new FormatException($"Line {lineNumber}: point line exceeds 256 characters.");
            string[] values = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (values.Length != 3 ||
                !float.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
                !float.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) ||
                !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
                throw new FormatException($"Line {lineNumber}: expected three finite X Y Z numbers separated by spaces.");
            if (points.Count == MaximumPoints)
                throw new FormatException($"Line {lineNumber}: pointfile exceeds {MaximumPoints} points.");
            points.Add(new Vector3(x, y, z));
        }
        if (points.Count < 2)
            throw new FormatException("Pointfile needs at least two X Y Z points to draw a path.");
        return new LeakPath(Path.GetFileName(path), points.ToArray());
    }
}
