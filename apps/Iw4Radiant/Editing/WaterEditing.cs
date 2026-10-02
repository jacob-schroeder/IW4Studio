using Iw4Radiant.Materials;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Editing;

internal static class WaterEditing
{
    internal static MapBrush[] GetBrushes(EditorSession session)
    {
        object[] owners = session.Selection.Items.Select(EditorSelection.Owner).Distinct().ToArray();
        return owners.Length > 0 && owners.All(owner => owner is MapBrush brush &&
            session.Document.World.Brushes.Contains(brush) && session.Visibility.CanSelect(session.Document, brush))
            ? owners.Cast<MapBrush>().ToArray() : [];
    }

    internal static bool HasWater(MapBrush brush, Func<string, MaterialSource?> resolveMaterial) =>
        brush.Faces.Any(face => resolveMaterial(face.Material)?.IsWater == true);

    internal static bool IsWaterVolume(MapBrush brush, Func<string, MaterialSource?> resolveMaterial)
    {
        if (brush.Faces.Count == 0 || !brush.Faces.All(face => resolveMaterial(face.Material)?.IsWater == true))
            return false;
        try { return BrushContents.ReadForCompilation(brush) == BrushKind.Structural; }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException) { return false; }
    }

    internal static void Apply(EditorSession session, IReadOnlyList<MapBrush> brushes, MaterialSource material)
    {
        if (!material.IsWater) throw new ArgumentException("Choose a water appearance.");
        if (brushes.Count == 0 || brushes.Any(brush => !session.Document.World.Brushes.Contains(brush) ||
            !session.Visibility.CanSelect(session.Document, brush)))
            throw new ArgumentException("Select world brushes to make water. Brushes inside an entity cannot be water volumes.");
        foreach (MapBrush brush in brushes)
        {
            // Validate before editing; retain unsupported native directives rather than silently discarding them.
            _ = BrushContents.ReadForCompilation(brush);
            if (brush.Faces.Count == 0) throw new ArgumentException("Water needs a closed brush volume.");
            if (material.Ocean is { } ocean)
            {
                MapPolygon[] tops = brush.GetPolygons().Where(OceanSurfaceGeometry.IsTop).ToArray();
                if (tops.Length == 0) throw new ArgumentException("Ocean waves need a horizontal top surface.");
                foreach (MapPolygon top in tops) _ = OceanSurfaceGeometry.Subdivide(top, ocean).Count();
            }
        }
        session.Edit(() =>
        {
            foreach (MapBrush brush in brushes)
            {
                BrushContents.Set(brush, BrushKind.Structural);
                foreach (MapFace face in brush.Faces) face.Material = material.Name;
            }
            session.Selection.SetRange(brushes);
        });
    }
}
