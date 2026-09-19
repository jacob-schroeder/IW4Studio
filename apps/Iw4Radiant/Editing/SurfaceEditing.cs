using Iw4Radiant.MapSource;

namespace Iw4Radiant.Editing;

internal static class SurfaceEditing
{
    internal enum TextureTransform { FlipU, FlipV, Rotate90 }

    internal static IEnumerable<BrushFaceSelection> GetFaces(EditorSession session)
    {
        var seen = new HashSet<MapFace>(ReferenceEqualityComparer.Instance);
        foreach (object item in session.Selection.Items)
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
                if (session.Visibility.CanSelect(session.Document, face.Brush) && seen.Add(face.Face)) yield return face;
        }
    }

    internal static void ApplyProjection(EditorSession session, SurfaceProjection edits)
    {
        var faces = GetFaces(session).ToArray();
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
        var faces = GetFaces(session).ToArray();
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

    internal static void TransformTexture(EditorSession session, TextureTransform transform)
    {
        MapFace[] faces = GetFaces(session).Select(selection => selection.Face).ToArray();
        if (faces.Length == 0)
            throw new ArgumentException("Select brush faces or brushes before transforming their textures.");
        var changes = faces.Select(face =>
        {
            SurfaceProjection projection = SurfaceProjection.Parse(face.Projection);
            projection = transform switch
            {
                TextureTransform.FlipU => projection with { Width = projection.Width == 0 ? -128 : -projection.Width },
                TextureTransform.FlipV => projection with { Height = projection.Height == 0 ? -128 : -projection.Height },
                TextureTransform.Rotate90 => projection with { Rotation = NormalizeRotation(projection.Rotation + 90) },
                _ => throw new ArgumentOutOfRangeException(nameof(transform))
            };
            _ = projection.GetMapping(face.Normal);
            return (Face: face, Projection: projection.Format());
        }).Where(change => change.Face.Projection != change.Projection).ToArray();
        if (changes.Length == 0) return;
        session.Edit(() =>
        {
            foreach (var change in changes) change.Face.Projection = change.Projection;
        });
    }

    private static float NormalizeRotation(float rotation)
    {
        rotation %= 360;
        return rotation < 0 ? rotation + 360 : rotation;
    }
}
