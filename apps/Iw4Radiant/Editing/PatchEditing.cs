using System.Numerics;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Editing;

internal static class PatchEditing
{
    internal static TerrainVertexSelection[] SelectedVertices(EditorSession session) => session.Selection.Items
        .OfType<TerrainVertexSelection>()
        .Where(vertex => vertex.Terrain.IsCurve && (uint)vertex.Index < vertex.Terrain.Vertices.Length)
        .Distinct()
        .ToArray();

    internal static void SetLocked(EditorSession session, bool locked)
    {
        TerrainVertexSelection[] vertices = SelectedVertices(session);
        if (vertices.Length == 0)
            throw new ArgumentException("Select one or more curved-patch control points in Vertex mode.");
        session.SetPatchVertexLock(vertices, locked);
    }

    internal static void Weld(EditorSession session)
    {
        TerrainVertexSelection[] vertices = SelectedVertices(session);
        if (vertices.Length < 2)
            throw new ArgumentException("Select at least two curved-patch control points to weld.");
        if (vertices.Any(session.IsPatchVertexLocked))
            throw new ArgumentException("Unlock the selected patch control points before welding them.");
        Vector3 target = vertices.Select(vertex => vertex.Terrain.Vertices[vertex.Index])
            .Aggregate(Vector3.Zero, (sum, point) => sum + point / vertices.Length);
        if (!BrushGeometry.IsFinite(target))
            throw new ArgumentException("The welded patch position exceeds the supported coordinate range.");
        session.Edit(() =>
        {
            foreach (TerrainVertexSelection vertex in vertices)
                vertex.Terrain.Vertices[vertex.Index] = target;
        });
    }
}
