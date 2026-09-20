using System.Numerics;
using Iw4Radiant.MapSource;
using Iw4Radiant.Materials;

namespace Iw4Radiant.Editing;

internal static class SkyEditing
{
    internal static BrushFaceSelection[] SelectedWorldFaces(EditorSession session)
    {
        var brushes = session.Document.World.Brushes.ToHashSet();
        BrushFaceSelection[] explicitFaces = session.Selection.Items.OfType<BrushFaceSelection>()
            .Where(face => face.Brush.Faces.Contains(face.Face)).ToArray();
        IEnumerable<BrushFaceSelection> faces = explicitFaces.Length > 0 ? explicitFaces : SurfaceEditing.GetFaces(session);
        return faces.Where(face => brushes.Contains(face.Brush) &&
            session.Visibility.CanSelect(session.Document, face.Brush)).ToArray();
    }

    internal static void Apply(EditorSession session, MaterialSource material)
    {
        var faces = SelectedWorldFaces(session);
        if (faces.Length == 0) throw new ArgumentException("Select world brush faces or world brushes to apply a sky.");
        RequireReadableSky(material);
        SelectionEditing.ApplyMaterial(session, material.Name, faces);
    }

    internal static (Vector3 Min, Vector3 Max)? EnclosureBounds(EditorSession session)
    {
        IEnumerable<object> geometry = session.Selection.Items.SelectMany(item => item is MapEntity entity
            ? entity.Brushes.Cast<object>().Concat(entity.Terrains)
            : [item]);
        return SelectionGeometry.Bounds(geometry.Where(item => item is not MapBrush brush || brush.GetVertices().Count > 0));
    }

    internal static void CreateEnclosure(EditorSession session, MaterialSource material, float padding)
    {
        float thickness = session.GridSize;
        if (!float.IsFinite(thickness) || thickness <= 0)
            throw new ArgumentException("The grid size must be positive and finite.");
        if (!float.IsFinite(padding) || padding < thickness)
            throw new ArgumentException("Sky enclosure padding must be finite and at least one grid unit.");
        if (EnclosureBounds(session) is not { } bounds)
            throw new ArgumentException("Select brush or terrain geometry to enclose.");
        Vector3 innerMin = bounds.Min - new Vector3(padding), innerMax = bounds.Max + new Vector3(padding);
        Vector3 outerMin = innerMin - new Vector3(thickness), outerMax = innerMax + new Vector3(thickness);
        if (!BrushGeometry.IsFinite(outerMin) || !BrushGeometry.IsFinite(outerMax) ||
            !Less(outerMin, innerMin) || !Less(innerMin, bounds.Min) ||
            !Less(bounds.Max, innerMax) || !Less(innerMax, outerMax))
            throw new ArgumentException("The selection, padding and grid size cannot form finite, distinct enclosure bounds.");
        RequireReadableSky(material);
        MapBrush[] walls =
        [
            MapBrush.CreateBox(outerMin, new(outerMax.X, outerMax.Y, innerMin.Z), material.Name),
            MapBrush.CreateBox(new(outerMin.X, outerMin.Y, innerMax.Z), outerMax, material.Name),
            MapBrush.CreateBox(new(outerMin.X, outerMin.Y, innerMin.Z), new(innerMin.X, outerMax.Y, innerMax.Z), material.Name),
            MapBrush.CreateBox(new(innerMax.X, outerMin.Y, innerMin.Z), new(outerMax.X, outerMax.Y, innerMax.Z), material.Name),
            MapBrush.CreateBox(innerMin with { Y = outerMin.Y }, new(innerMax.X, innerMin.Y, innerMax.Z), material.Name),
            MapBrush.CreateBox(new(innerMin.X, innerMax.Y, innerMin.Z), new(innerMax.X, outerMax.Y, innerMax.Z), material.Name)
        ];
        foreach (var wall in walls) BrushGeometry.Validate(wall);
        session.Edit(() =>
        {
            session.Document.World.Brushes.AddRange(walls);
            session.Selection.SetRange(walls);
        });

        static bool Less(Vector3 a, Vector3 b) => a.X < b.X && a.Y < b.Y && a.Z < b.Z;
    }

    internal static void SelectFaces(EditorSession session, string material)
    {
        var faces = session.Document.World.Brushes.SelectMany(brush => brush.Faces
            .Where(face => face.Material == material).Select(face => new BrushFaceSelection(brush, face))).ToArray();
        if (faces.Length == 0) return;
        session.Tool = EditorTool.Face;
        session.SelectRange(faces);
    }

    private static void RequireReadableSky(MaterialSource material)
    {
        if (!material.IsSky) throw new ArgumentException("Choose an authored sky material.");
        SelectionEditing.ValidateMaterial(material.Name);
        MaterialImages.LoadCube(material, int.MaxValue);
    }
}
