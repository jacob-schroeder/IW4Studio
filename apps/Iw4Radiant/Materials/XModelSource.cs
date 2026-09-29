using System.Numerics;
using IW4.Formats.SourceFormat.XModel;
using IW4.Formats.XModel;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.Physics;
using IW4.Game.Assets.XModel;
using IW4.Game.Codecs.XModel;

namespace Iw4Radiant.Materials;

internal sealed class XModelSource
{
    private const int DObjSkelMatSize = 0x40;
    private const int MaximumIndexedCollisionVertices = 20_000;
    private const int MaximumCollisionTrianglesPerGroup = 40_000;
    private readonly Lazy<XModelExportDocument> _document;
    private readonly string? _sourceRoot;

    internal XModelSource(string name, string sourcePath, Action<XModelExportDocument>? loaded = null,
        string? sourceRoot = null)
    {
        Name = name;
        SourcePath = sourcePath;
        _sourceRoot = sourceRoot;
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
    internal (Vector3[] Points, int[] Triangles)[]? LoadCollisionParts()
    {
        if (_sourceRoot is null || !File.Exists(Path.Combine(_sourceRoot, "xmodel_native", Name + ".json")))
            return null;

        XModelAsset model = new XModelNativeExchange().Link(_sourceRoot, Name,
            material => new MaterialAsset { Info = new MaterialInfo { Name = material } },
            preset => new PhysPresetAsset { Name = preset },
            collmap => new PhysCollmapAsset { Name = collmap }).Model;
        if (model.CollSurfs.Count != model.NumCollSurfs)
            throw new InvalidDataException($"XModel '{Name}' has inconsistent collision surface counts.");
        if (model.CollLod == byte.MaxValue)
        {
            if (model.CollSurfs.Count != 0)
                throw new InvalidDataException($"XModel '{Name}' has collision surfaces without a collision LOD.");
            return [];
        }
        if (model.CollLod >= model.NumLods || model.CollLod >= model.Lods.Count ||
            model.Lods[model.CollLod].ModelSurfs is not { } provider ||
            provider.Surfaces.Count < model.Lods[model.CollLod].NumSurfs)
            throw new InvalidDataException($"XModel '{Name}' has an incomplete collision LOD.");

        var parts = new List<(Vector3[] Points, int[] Triangles)>();
        int collisionIndex = 0, indexedVertices = 0;
        foreach (XSurface surface in provider.Surfaces.Take(model.Lods[model.CollLod].NumSurfs))
        {
            if (surface.VertListCount != surface.VertList.Count ||
                surface.TriIndices.Count != surface.TriCount * 3 ||
                surface.Verts0.Count != surface.VertCount * XSurfaceVertexCodec.StreamStride)
                throw new InvalidDataException($"XModel '{Name}' has incomplete collision surface geometry.");
            int firstVertex = 0, firstTriangle = 0;
            foreach (XRigidVertList rigid in surface.VertList)
            {
                if (collisionIndex >= model.CollSurfs.Count || rigid is null ||
                    rigid.BoneOffset % DObjSkelMatSize != 0 ||
                    rigid.BoneOffset / DObjSkelMatSize >= model.NumBones ||
                    firstVertex + rigid.VertCount > surface.VertCount ||
                    rigid.TriOffset < firstTriangle || rigid.TriOffset + rigid.TriCount > surface.TriCount)
                    throw new InvalidDataException($"XModel '{Name}' has inconsistent collision rigid groups.");
                XModelCollSurf collision = model.CollSurfs[collisionIndex++];
                if (collision.BoneIndex != rigid.BoneOffset / DObjSkelMatSize)
                    throw new InvalidDataException($"XModel '{Name}' collision bone does not match its rigid group.");
                bool solid = (collision.Contents & 1) != 0;
                if (solid && rigid.TriCount > MaximumCollisionTrianglesPerGroup)
                    throw new NotSupportedException($"XModel '{Name}' has too much indexed collision geometry for player clip.");
                var indices = new HashSet<int>();
                for (int index = 0; index < rigid.TriCount * 3; index++)
                {
                    int vertex = surface.TriIndices[rigid.TriOffset * 3 + index];
                    if (vertex < firstVertex || vertex >= firstVertex + rigid.VertCount)
                        throw new InvalidDataException($"XModel '{Name}' has invalid collision triangle vertices.");
                    if (solid && indices.Add(vertex) && ++indexedVertices > MaximumIndexedCollisionVertices)
                        throw new NotSupportedException($"XModel '{Name}' has too much indexed collision geometry for player clip.");
                }
                if (solid)
                {
                    if (indices.Count == 0)
                        throw new InvalidDataException($"XModel '{Name}' has an empty solid collision group.");
                    Vector3[] points = new Vector3[indices.Count];
                    var localIndices = new Dictionary<int, int>(indices.Count);
                    int next = 0;
                    foreach (int vertex in indices)
                    {
                        if (!XSurfaceVertexCodec.TryReadReasonablePosition(surface.Verts0, vertex, out Vector3 point))
                            throw new InvalidDataException($"XModel '{Name}' has invalid collision vertex coordinates.");
                        localIndices.Add(vertex, next);
                        points[next++] = point;
                    }
                    int[] triangles = new int[rigid.TriCount * 3];
                    for (int index = 0; index < triangles.Length; index++)
                        triangles[index] = localIndices[surface.TriIndices[rigid.TriOffset * 3 + index]];
                    parts.Add((points, triangles));
                }
                firstVertex += rigid.VertCount;
                firstTriangle = rigid.TriOffset + rigid.TriCount;
            }
        }
        if (collisionIndex != model.CollSurfs.Count)
            throw new InvalidDataException($"XModel '{Name}' collision surface count does not match its rigid groups.");
        return parts.ToArray();
    }

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
        if (!XModelExportReader.TryRead(reader, out var document, out var issues, skipDegenerateTriangles: true) || document is null)
            throw new InvalidDataException($"XModel '{path}': " + string.Join("; ", issues.Take(4)
                .Select(issue => $"line {issue.Line}: {issue.Message}")));
        if (document.Vertices.Count == 0 || document.Triangles.Count == 0)
            throw new InvalidDataException($"XModel '{path}' has no model geometry.");
        return document;
    }
}
