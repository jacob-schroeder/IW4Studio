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
        MapFace[] explicitlySelectedFaces = session.Selection.Items.OfType<BrushFaceSelection>()
            .Where(face => face.Brush.Faces.Contains(face.Face) && session.Visibility.CanSelect(session.Document, face.Brush))
            .Select(face => face.Face).Distinct().ToArray();
        MapFace[] faces = explicitlySelectedFaces.Length > 0 ? explicitlySelectedFaces :
            SurfaceEditing.GetFaces(session).Select(face => face.Face).ToArray();
        MapTerrain[] terrains = explicitlySelectedFaces.Length > 0 ? [] :
            session.Selection.Items.Select(EditorSelection.Owner).OfType<MapTerrain>()
                .Concat(session.Selection.Items.OfType<MapEntity>().SelectMany(entity => entity.Terrains))
                .Where(terrain => session.Visibility.CanSelect(session.Document, terrain)).Distinct().ToArray();
        ApplyMaterial(session, material, faces, terrains);
    }

    internal static void ApplyMaterial(EditorSession session, string material, BrushFaceSelection[] faces) =>
        ApplyMaterial(session, material, faces.Where(face => session.Visibility.CanSelect(session.Document, face.Brush))
            .Select(face => face.Face).Distinct().ToArray(), []);

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
        return session.Selection.Items.Where(item => SelectionGeometry.CanTransform(item) &&
            session.Visibility.CanSelect(session.Document, item)).ToArray();
    }
}
