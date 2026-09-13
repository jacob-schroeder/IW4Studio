using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Rendering;

internal static class PointEntityGeometry
{
    internal static bool IsPointEntity(MapEntity entity) => entity.ClassName != "worldspawn" &&
        entity.Brushes.Count == 0 && entity.Terrains.Count == 0 && entity.PreservedPrimitives.Count == 0;

    internal static MapBrush CreateBrush(MapEntity entity)
    {
        var bounds = EditorSession.EntityBounds(entity);
        return MapBrush.CreateBox(bounds.Min, bounds.Max, "");
    }
}
