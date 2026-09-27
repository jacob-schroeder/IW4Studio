using System.Numerics;
using Iw4Radiant.Compilation;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;
using JoltPhysicsSharp;

namespace Iw4Radiant.Viewports.Camera;

// The placement and glass previews use the same static world collision selection.
internal static class CameraStaticPhysicsCollision
{
    internal static (Vector3[][] Brushes, Vector3[] TerrainVertices, IndexedTriangle[] TerrainTriangles) Prepare(
        EditorSession session, Func<string, MaterialSource?> resolveMaterial,
        IReadOnlySet<MapEntity> excludedEntities, IReadOnlySet<MapBrush> excludedBrushes,
        string label, bool requireStaticWorld = true)
    {
        var brushes = new List<Vector3[]>();
        var terrainVertices = new List<Vector3>();
        var terrainTriangles = new List<IndexedTriangle>();
        foreach (MapEntity entity in session.Document.Entities)
        {
            if (excludedEntities.Contains(entity)) continue;
            if (entity.ClassName is "worldspawn" or "func_group") AddSurfaces(entity);
            else if (PrefabLibrary.IsPrefab(entity))
            {
                MapDocument preview = session.Prefabs.GetPreview(entity, session.FilePath) ??
                    throw new ArgumentException(session.Prefabs.Error(entity, session.FilePath) ??
                        "A prefab collision source is unavailable.");
                foreach (MapEntity part in preview.Entities.Where(part => part.ClassName is "worldspawn" or "func_group"))
                    AddSurfaces(part);
            }
        }
        if (requireStaticWorld && brushes.Count == 0 && terrainTriangles.Count == 0)
            throw new InvalidOperationException($"{label} needs solid world/group brush or terrain collision.");
        return (brushes.ToArray(), terrainVertices.ToArray(), terrainTriangles.ToArray());

        void AddSurfaces(MapEntity entity)
        {
            if (entity.PreservedPrimitives.Count != 0)
                throw new NotSupportedException($"{label} cannot classify preserved map primitives.");
            foreach (MapBrush brush in entity.Brushes)
            {
                if (excludedBrushes.Contains(brush)) continue;
                BrushGeometry.Validate(brush);
                BrushKind kind = BrushContents.ReadForCompilation(brush);
                if (kind is BrushKind.NonColliding or BrushKind.WeaponClip ||
                    brush.Faces.All(face => ClipBrushMaterial.IsPlayerClip(face.Material))) continue;
                bool solid = false, nonsolid = false;
                foreach (MapFace face in brush.Faces)
                {
                    if (ClipBrushMaterial.IsPlayerClip(face.Material)) { nonsolid = true; continue; }
                    if (CaulkMaterial.IsCaulk(face.Material)) { solid = true; continue; }
                    MaterialSource material = resolveMaterial(face.Material) ??
                        throw new InvalidDataException($"{label} material '{face.Material}' is unavailable.");
                    if (material.IsSky || material.IsWater) nonsolid = true; else solid = true;
                }
                if (solid && nonsolid)
                    throw new NotSupportedException($"{label} cannot classify brushes mixing solid, sky, water or player-clip faces.");
                if (!solid) continue;
                Vector3[] points = brush.GetVertices().ToArray();
                if (points.Length < 4 || points.Any(point => !Finite(point)))
                    throw new InvalidDataException($"{label} found an invalid convex world brush.");
                brushes.Add(points);
            }
            foreach (MapTerrain terrain in entity.Terrains)
            {
                if (TerrainContents.ReadNonColliding(terrain)) continue;
                if (ClipBrushMaterial.IsPlayerClip(terrain.Material)) continue;
                MaterialSource material = resolveMaterial(terrain.Material) ??
                    throw new InvalidDataException($"{label} material '{terrain.Material}' is unavailable.");
                if (material.IsSky || material.IsWater) continue;
                MapSurfaceCompiler.ValidateTerrain(terrain);
                MapTerrain surface = terrain.GetSurface();
                int first = terrainVertices.Count;
                terrainVertices.AddRange(surface.Vertices);
                foreach ((int a, int b, int c) in surface.GetTriangles())
                {
                    Vector3 normal = Vector3.Cross(surface.Vertices[b] - surface.Vertices[a],
                        surface.Vertices[c] - surface.Vertices[a]);
                    if (normal.LengthSquared() <= 0.00000001f && terrain.IsCurve) continue;
                    if (!float.IsFinite(normal.LengthSquared()) || normal.LengthSquared() <= 0)
                        throw new InvalidDataException($"{label} terrain has a degenerate triangle.");
                    terrainTriangles.Add(new IndexedTriangle(first + a, first + b, first + c));
                }
            }
        }
    }

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
