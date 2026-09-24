using System.Numerics;
using IW4.Formats.D3dbsp;

namespace Iw4Radiant.Rendering;

internal sealed class CompiledBspPreview
{
    internal SceneVertex[] Vertices { get; }
    internal (string Material, int Start, int Count, int WireStart, int WireCount)[] Batches { get; }
    internal (Vector3 Min, Vector3 Max) Bounds { get; }

    private CompiledBspPreview(SceneVertex[] vertices,
        (string Material, int Start, int Count, int WireStart, int WireCount)[] batches,
        (Vector3 Min, Vector3 Max) bounds)
    {
        Vertices = vertices;
        Batches = batches;
        Bounds = bounds;
    }

    internal static CompiledBspPreview Read(string path)
    {
        var surfaces = D3dbspFile.Read(path).GetRenderTriangles();
        var vertices = new List<SceneVertex>();
        var batches = new List<(string Material, int Start, int Count, int WireStart, int WireCount)>();
        Vector3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
        foreach (var material in surfaces.GroupBy(surface => surface.Material, StringComparer.Ordinal))
        {
            int start = vertices.Count;
            foreach (var surface in material)
            {
                for (int triangle = 0; triangle < surface.Vertices.Count; triangle += 3)
                {
                    // D3DBSP indices use the native clockwise winding; OpenGL uses CCW here.
                    Add(surface.Vertices[triangle]);
                    Add(surface.Vertices[triangle + 2]);
                    Add(surface.Vertices[triangle + 1]);
                }
            }
            batches.Add((material.Key, start, vertices.Count - start, 0, 0));
        }
        void Add((Vector3 Position, Vector3 Normal, Vector2 Uv, Vector4 Color) vertex)
        {
            vertices.Add(new SceneVertex(vertex.Position, vertex.Normal, vertex.Uv, vertex.Color));
            min = Vector3.Min(min, vertex.Position);
            max = Vector3.Max(max, vertex.Position);
        }
        if (vertices.Count == 0)
            throw new InvalidDataException("The d3dbsp has no render triangles to preview.");
        return new CompiledBspPreview(vertices.ToArray(), batches.ToArray(), (min, max));
    }
}
