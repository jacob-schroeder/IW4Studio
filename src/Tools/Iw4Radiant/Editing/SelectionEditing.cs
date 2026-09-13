using System.Numerics;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Editing;

internal static class SelectionEditing
{
    internal static void Delete(EditorSession session)
    {
        object[] selected = WholeObjects(session);
        if (selected.Length == 0) return;
        session.Edit(() =>
        {
            foreach (object item in selected)
            {
                foreach (MapEntity entity in session.Document.Entities)
                {
                    if (item is MapBrush brush) entity.Brushes.Remove(brush);
                    if (item is MapTerrain terrain) entity.Terrains.Remove(terrain);
                }
                if (item is MapEntity selectedEntity) session.Document.Entities.Remove(selectedEntity);
            }
            session.Selection.Clear();
        });
    }

    internal static void Duplicate(EditorSession session)
    {
        object[] selected = WholeObjects(session);
        if (selected.Length == 0) return;
        session.Edit(() =>
        {
            var copies = new List<object>();
            foreach (object item in selected)
            {
                if (item is MapEntity selectedEntity)
                {
                    MapEntity copy = selectedEntity.Clone();
                    session.Document.Entities.Add(copy);
                    copies.Add(copy);
                    continue;
                }
                foreach (MapEntity entity in session.Document.Entities)
                {
                    if (item is MapBrush brush && entity.Brushes.Contains(brush))
                    {
                        MapBrush copy = brush.Clone(); entity.Brushes.Add(copy); copies.Add(copy); break;
                    }
                    if (item is MapTerrain terrain && entity.Terrains.Contains(terrain))
                    {
                        MapTerrain copy = terrain.Clone(); entity.Terrains.Add(copy); copies.Add(copy); break;
                    }
                }
            }
            session.Selection.SetRange(copies);
            SelectionTransforms.Translate(session, new Vector3(session.GridSize, session.GridSize, 0));
        });
    }

    internal static void ApplyMaterial(EditorSession session, string material)
    {
        MapFace[] faces = SurfaceEditing.GetFaces(session.Selection).Select(face => face.Face).ToArray();
        MapTerrain[] terrains = session.Selection.Items.Select(EditorSelection.Owner).OfType<MapTerrain>()
            .Concat(session.Selection.Items.OfType<MapEntity>().SelectMany(entity => entity.Terrains)).Distinct().ToArray();
        ApplyMaterial(session, material, faces, terrains);
    }

    internal static void ApplyMaterial(EditorSession session, string material, MapFace[] faces) =>
        ApplyMaterial(session, material, faces, []);

    internal static void ValidateMaterial(string material)
    {
        if (string.IsNullOrWhiteSpace(material) || material.Any(char.IsWhiteSpace) || material.Any(char.IsControl) ||
            material.Contains("//", StringComparison.Ordinal) || material.Contains("/*", StringComparison.Ordinal) ||
            material.IndexOfAny(['"', '{', '}', '(', ')', ';']) >= 0)
            throw new ArgumentException("A material must be a single Radiant asset name.");
    }

    private static void ApplyMaterial(EditorSession session, string material, MapFace[] faces, MapTerrain[] terrains)
    {
        ValidateMaterial(material);
        session.Material = material;
        if (faces.All(face => face.Material == material) && terrains.All(terrain => terrain.Material == material))
        {
            session.Refresh();
            return;
        }
        session.Edit(() =>
        {
            foreach (MapFace face in faces) face.Material = material;
            foreach (MapTerrain terrain in terrains) terrain.Material = material;
        });
    }

    private static object[] WholeObjects(EditorSession session)
    {
        if (session.Selection.Items.Any(item => item is BrushFaceSelection or BrushVertexSelection or TerrainVertexSelection))
            throw new ArgumentException("Select whole objects to duplicate or delete. Use the clipper to remove part of a brush.");
        return session.Selection.Items.Where(SelectionGeometry.CanTransform).ToArray();
    }
}
