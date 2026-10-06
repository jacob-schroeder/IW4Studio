using System.Numerics;

namespace Iw4Radiant.MapSource;

internal sealed class TerrainNormals
{
    private readonly Dictionary<(Vector3 Position, string Material, string Smoothing), Vector3> smoothNormals = [];

    internal TerrainNormals(IEnumerable<MapTerrain> terrains)
    {
        // Equal positions on adjoining patches share a normal. Materials/smoothing groups stay independent.
        foreach (MapTerrain terrain in terrains)
        foreach (var (a, b, c) in terrain.GetTriangles())
        {
            Vector3 normal = Vector3.Cross(terrain.Vertices[b] - terrain.Vertices[a], terrain.Vertices[c] - terrain.Vertices[a]);
            if (normal.LengthSquared() <= 0.00000001f || terrain.Smoothing == "smoothing_hard") continue;
            foreach (int vertex in new[] { a, b, c })
            {
                var key = (terrain.Vertices[vertex], terrain.Material, terrain.Smoothing ?? "smoothing_smooth");
                smoothNormals[key] = smoothNormals.GetValueOrDefault(key) + normal;
            }
        }
    }

    internal Vector3 At(MapTerrain terrain, int index, Vector3 faceNormal)
    {
        if (terrain.Smoothing == "smoothing_hard") return faceNormal;
        Vector3 normal = smoothNormals[(terrain.Vertices[index], terrain.Material, terrain.Smoothing ?? "smoothing_smooth")];
        return BrushGeometry.IsFinite(normal) && normal.LengthSquared() > 0.00000001f
            ? Vector3.Normalize(normal)
            : throw new InvalidDataException("Terrain has opposing normals or a degenerate tangent. Split the mesh at the hard edge.");
    }
}
