using System.Numerics;
using Iw4Radiant.Editing;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Viewports.Orthographic;

internal sealed class OrthographicTerrainStroke
{
    private Vector2 _lastStamp;

    internal bool Begin(EditorSession session, Vector2 point, bool lower, Action beginEdit)
    {
        _lastStamp = point;
        bool changed = Stamp(session, point, lower, beginEdit);
        if (changed) session.Refresh();
        return changed;
    }

    internal bool Move(EditorSession session, Vector2 point, bool lower, Action beginEdit)
    {
        float spacing = Math.Max(1, session.SculptRadius * 0.15f);
        float distance = Vector2.Distance(_lastStamp, point);
        if (distance < spacing) return false;
        Vector2 direction = Vector2.Normalize(point - _lastStamp);
        bool changed = false;
        // Consume a long pointer segment completely so fast movement does not leave gaps.
        int count = checked((int)MathF.Floor(distance / spacing));
        for (int i = 0; i < count; i++)
        {
            _lastStamp += direction * spacing;
            changed |= Stamp(session, _lastStamp, lower, beginEdit);
        }
        if (changed) session.Refresh();
        return changed;
    }

    private static bool Stamp(EditorSession session, Vector2 point, bool lower, Action beginEdit)
    {
        var terrains = session.Selection.Items.Select(EditorSelection.Owner).OfType<MapTerrain>().Distinct().ToArray();
        if (terrains.Length == 0) return false;
        beginEdit();
        bool changed = false;
        foreach (MapTerrain terrain in terrains)
            changed |= session.SculptMode switch
            {
                TerrainSculptMode.RaiseLower => TerrainEditing.RaiseLower(terrain, point, session.SculptRadius,
                    session.SculptStrength * (lower ? -1 : 1)),
                TerrainSculptMode.Smooth => TerrainEditing.Smooth(terrain, point, session.SculptRadius,
                    Math.Clamp(session.SculptStrength / 100f, 0, 1)),
                TerrainSculptMode.Flatten => TerrainEditing.Flatten(terrain, point, session.SculptRadius,
                    session.FlattenHeight, Math.Clamp(session.SculptStrength / 100f, 0, 1)),
                _ => false
            };
        return changed;
    }
}
