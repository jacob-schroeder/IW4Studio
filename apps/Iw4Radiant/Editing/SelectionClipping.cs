using System.Numerics;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Editing;

internal static class SelectionClipping
{
    internal static int Apply(EditorSession session, Plane plane)
    {
        MapBrush[] brushes = session.Selection.Items.Select(EditorSelection.Owner).OfType<MapBrush>().Distinct().ToArray();
        if (brushes.Length == 0) throw new ArgumentException("Select one or more brushes before drawing a clip line.");
        var replacements = new List<(MapEntity Owner, MapBrush Brush, MapBrush[] Pieces)>();
        foreach (MapBrush brush in brushes)
        {
            var split = BrushClipping.Split(brush, plane);
            MapBrush[] pieces = session.ClipMode switch
            {
                ClipMode.KeepFront => split.Front is { } front ? [front] : [],
                ClipMode.KeepBack => split.Back is { } back ? [back] : [],
                _ => new[] { split.Back, split.Front }.OfType<MapBrush>().ToArray()
            };
            if (split.Back is null || split.Front is null)
                if (pieces.Length == 1) continue;
            MapEntity owner = session.Document.Entities.First(entity => entity.Brushes.Contains(brush));
            replacements.Add((owner, brush, pieces));
        }
        if (replacements.Count == 0) return 0;
        session.Edit(() =>
        {
            var selected = new List<object>();
            foreach (object item in session.Selection.Items)
            {
                var replacement = replacements.FirstOrDefault(candidate =>
                    ReferenceEquals(candidate.Brush, EditorSelection.Owner(item)));
                if (replacement.Brush is null) selected.Add(item);
                else selected.AddRange(replacement.Pieces);
            }
            foreach (var replacement in replacements)
            {
                int index = replacement.Owner.Brushes.IndexOf(replacement.Brush);
                replacement.Owner.Brushes.RemoveAt(index);
                replacement.Owner.Brushes.InsertRange(index, replacement.Pieces);
            }
            session.Selection.SetRange(selected);
        });
        return replacements.Count;
    }
}
