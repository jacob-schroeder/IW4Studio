using System.Numerics;
using IW4.Formats.XModel;

namespace Iw4Radiant.Materials;

internal sealed class XModelSource
{
    private readonly Lazy<XModelExportDocument> _document;

    internal XModelSource(string name, string sourcePath, Action<XModelExportDocument>? loaded = null)
    {
        Name = name;
        SourcePath = sourcePath;
        _document = new Lazy<XModelExportDocument>(() =>
        {
            var document = Read(sourcePath);
            loaded?.Invoke(document);
            return document;
        });
    }

    internal XModelSource(string name, XModelExportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Name = name;
        SourcePath = string.Empty;
        _document = new Lazy<XModelExportDocument>(() => document);
    }

    internal string Name { get; }
    internal string SourcePath { get; }
    internal XModelExportDocument Document => _document.Value;
    internal (Vector3 Min, Vector3 Max) Bounds
    {
        get
        {
            var vertices = Document.Vertices;
            return (vertices.Select(vertex => vertex.Position).Aggregate(Vector3.Min),
                vertices.Select(vertex => vertex.Position).Aggregate(Vector3.Max));
        }
    }

    private static XModelExportDocument Read(string path)
    {
        using var reader = File.OpenText(path);
        if (!XModelExportReader.TryRead(reader, out var document, out var issues) || document is null)
            throw new InvalidDataException($"XModel '{path}': " + string.Join("; ", issues.Take(4)
                .Select(issue => $"line {issue.Line}: {issue.Message}")));
        if (document.Vertices.Count == 0 || document.Triangles.Count == 0)
            throw new InvalidDataException($"XModel '{path}' has no model geometry.");
        return document;
    }
}
