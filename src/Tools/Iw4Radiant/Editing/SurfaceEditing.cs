using Iw4Radiant.MapSource;

namespace Iw4Radiant.Editing;

internal static class SurfaceEditing
{
    internal static IEnumerable<BrushFaceSelection> GetFaces(EditorSelection selection)
    {
        var seen = new HashSet<MapFace>(ReferenceEqualityComparer.Instance);
        foreach (object item in selection.Items)
        {
            IEnumerable<BrushFaceSelection> faces = item switch
            {
                BrushFaceSelection face when face.Brush.Faces.Contains(face.Face) => [face],
                MapBrush brush => brush.Faces.Select(face => new BrushFaceSelection(brush, face)),
                MapEntity entity => entity.Brushes.SelectMany(brush =>
                    brush.Faces.Select(face => new BrushFaceSelection(brush, face))),
                _ => []
            };
            foreach (var face in faces)
                if (seen.Add(face.Face)) yield return face;
        }
    }

    internal static void ApplyProjection(EditorSession session, SurfaceProjection edits)
    {
        var faces = GetFaces(session.Selection).ToArray();
        if (faces.Length == 0)
            throw new ArgumentException("Select brush faces or brushes to edit their surface projection.");
        var changes = faces.Select(selection =>
        {
            var projection = edits with { Suffix = SurfaceProjection.Parse(selection.Face.Projection).Suffix };
            projection.GetMapping(selection.Face.Normal);
            return (selection.Face, Projection: projection.Format());
        }).Where(change => change.Face.Projection != change.Projection).ToArray();
        if (changes.Length == 0) return;
        session.Edit(() =>
        {
            foreach (var change in changes) change.Face.Projection = change.Projection;
        });
    }

    internal static void Fit(EditorSession session, float repeatsX, float repeatsY)
    {
        var faces = GetFaces(session.Selection).ToArray();
        if (faces.Length == 0)
            throw new ArgumentException("Select brush faces or brushes to fit their textures.");
        var changes = faces.Select(selection =>
        {
            var polygon = selection.Brush.GetPolygons().FirstOrDefault(polygon => ReferenceEquals(polygon.Face, selection.Face))
                ?? throw new ArgumentException("Cannot fit a texture to a face without a valid polygon.");
            return (selection.Face, Projection: SurfaceProjection.Parse(selection.Face.Projection).Fit(polygon, repeatsX, repeatsY).Format());
        }).Where(change => change.Face.Projection != change.Projection).ToArray();
        if (changes.Length == 0) return;
        session.Edit(() =>
        {
            foreach (var change in changes) change.Face.Projection = change.Projection;
        });
    }
}
